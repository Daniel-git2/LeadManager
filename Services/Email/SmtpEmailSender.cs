using System.IO;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace LeadManager.Services.Email;

/// <param name="StopBatch">True when later emails would fail the same way (bad password, server unreachable).</param>
/// <param name="Warning">Sent, but something else went wrong, e.g. the Sent-folder copy.</param>
public sealed record SendOutcome(bool Sent, string? Error = null, bool StopBatch = false, string? Warning = null);

public interface IEmailSender
{
    /// <summary>Signs in to the server(s) without sending anything. Returns a description of what worked.</summary>
    Task<string> TestAsync(EmailSettings settings, CancellationToken cancellationToken);

    /// <summary>Opens a connection for sending a batch of emails.</summary>
    Task<IEmailSession> ConnectAsync(EmailSettings settings, CancellationToken cancellationToken);
}

public interface IEmailSession : IAsyncDisposable
{
    Task<SendOutcome> SendAsync(MimeMessage message, CancellationToken cancellationToken);
}

/// <summary>An error with a message fit to show the user.</summary>
public sealed class EmailException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Sends over SMTP with MailKit, optionally filing a copy in the IMAP Sent folder.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<string> TestAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        using (var smtp = await ConnectSmtpAsync(settings, cancellationToken))
        {
            await smtp.DisconnectAsync(true, cancellationToken);
        }
        if (!settings.SaveToSentFolder)
        {
            return $"Signed in to {settings.SmtpHost}. Ready to send.";
        }
        using var imap = await ConnectImapAsync(settings, cancellationToken);
        var sent = await FindSentFolderAsync(imap, settings, cancellationToken);
        await imap.DisconnectAsync(true, cancellationToken);
        return $"Signed in to {settings.SmtpHost} for sending, and copies will go to your “{sent.FullName}” folder.";
    }

    public async Task<IEmailSession> ConnectAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        var smtp = await ConnectSmtpAsync(settings, cancellationToken);
        return new Session(settings, smtp);
    }

    private static async Task<SmtpClient> ConnectSmtpAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        var smtp = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await smtp.ConnectAsync(settings.SmtpHost.Trim(), settings.SmtpPort, ToSocketOptions(settings.SmtpSecurity), cancellationToken);
            if (settings.Username.Trim().Length > 0)
            {
                await smtp.AuthenticateAsync(settings.Username.Trim(), settings.Password, cancellationToken);
            }
            return smtp;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            smtp.Dispose();
            throw Describe(ex, "sending", settings.SmtpHost, settings.SmtpPort);
        }
    }

    private static async Task<ImapClient> ConnectImapAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        var imap = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await imap.ConnectAsync(settings.ImapHost.Trim(), settings.ImapPort, ToSocketOptions(settings.ImapSecurity), cancellationToken);
            await imap.AuthenticateAsync(settings.Username.Trim(), settings.Password, cancellationToken);
            return imap;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            imap.Dispose();
            throw Describe(ex, "the Sent folder copy (IMAP)", settings.ImapHost, settings.ImapPort);
        }
    }

    private static async Task<IMailFolder> FindSentFolderAsync(ImapClient imap, EmailSettings settings, CancellationToken cancellationToken)
    {
        if (settings.SentFolder.Trim() is { Length: > 0 } configured)
        {
            try
            {
                return await imap.GetFolderAsync(configured, cancellationToken);
            }
            catch (FolderNotFoundException)
            {
                throw new EmailException($"There's no folder called “{configured}” on the mail server. Leave the Sent folder blank to find it automatically.");
            }
        }

        if ((imap.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0 && imap.GetFolder(SpecialFolder.Sent) is { } special)
        {
            return special;
        }

        // No special-use flags: look for the usual names at the top level and under the inbox.
        string[] names = ["Sent", "Sent Items", "Sent Messages", "Sent Mail"];
        var candidates = new List<IMailFolder>();
        foreach (var root in new[] { imap.GetFolder(imap.PersonalNamespaces[0]), imap.Inbox })
        {
            candidates.AddRange(await root.GetSubfoldersAsync(false, cancellationToken));
        }
        return candidates.FirstOrDefault(f => names.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            ?? throw new EmailException("Couldn't find your Sent folder. Type its name in Email settings, or turn off the Sent folder copy.");
    }

    private static SecureSocketOptions ToSocketOptions(MailSecurity security) => security switch
    {
        MailSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        MailSecurity.StartTls => SecureSocketOptions.StartTls,
        MailSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };

    private static EmailException Describe(Exception ex, string purpose, string host, int port) => ex switch
    {
        EmailException email => email,
        AuthenticationException => new EmailException($"The server rejected the username or password for {purpose}.", ex),
        SslHandshakeException => new EmailException($"Couldn't start a secure connection to {host}:{port}. Check the port and security setting.", ex),
        SocketException or TimeoutException or IOException => new EmailException($"Couldn't reach {host}:{port}. Check the server name, port and your internet connection.", ex),
        _ => new EmailException($"Problem with {purpose}: {ex.Message}", ex),
    };

    private sealed class Session(EmailSettings settings, SmtpClient smtp) : IEmailSession
    {
        private ImapClient? _imap;
        private IMailFolder? _sentFolder;
        private string? _sentFolderProblem;

        public async Task<SendOutcome> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            try
            {
                if (!smtp.IsConnected)
                {
                    // The server dropped us between emails (common during long pauses); sign in again.
                    await smtp.ConnectAsync(settings.SmtpHost.Trim(), settings.SmtpPort, ToSocketOptions(settings.SmtpSecurity), cancellationToken);
                    if (settings.Username.Trim().Length > 0)
                    {
                        await smtp.AuthenticateAsync(settings.Username.Trim(), settings.Password, cancellationToken);
                    }
                }
                await smtp.SendAsync(message, cancellationToken);
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.RecipientNotAccepted)
            {
                return new SendOutcome(false, $"The server refused this address: {ex.Message}");
            }
            catch (SmtpCommandException ex) when (ex.ErrorCode == SmtpErrorCode.MessageNotAccepted)
            {
                return new SendOutcome(false, $"The server refused this message: {ex.Message}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new SendOutcome(false, Describe(ex, "sending", settings.SmtpHost, settings.SmtpPort).Message, StopBatch: true);
            }

            return new SendOutcome(true, Warning: await CopyToSentAsync(message, cancellationToken));
        }

        public async ValueTask DisposeAsync()
        {
            await DisconnectQuietly(smtp);
            if (_imap is not null)
            {
                await DisconnectQuietly(_imap);
            }
        }

        // Returns a warning when the copy couldn't be made; the email itself has already gone.
        private async Task<string?> CopyToSentAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            if (!settings.SaveToSentFolder || _sentFolderProblem is not null)
            {
                return _sentFolderProblem;
            }
            try
            {
                if (_imap is not { IsConnected: true })
                {
                    _imap?.Dispose();
                    _imap = await ConnectImapAsync(settings, cancellationToken);
                    _sentFolder = await FindSentFolderAsync(_imap, settings, cancellationToken);
                }
                await _sentFolder!.AppendAsync(message, MessageFlags.Seen, cancellationToken);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Don't retry for every email in the batch; report it once per email instead.
                _sentFolderProblem = $"Sent, but no copy was saved to your Sent folder: {Describe(ex, "the Sent folder copy", settings.ImapHost, settings.ImapPort).Message}";
                return _sentFolderProblem;
            }
        }

        private static async Task DisconnectQuietly(IMailService client)
        {
            try
            {
                if (client.IsConnected)
                {
                    await client.DisconnectAsync(true);
                }
            }
            catch (Exception)
            {
                // Closing politely is a courtesy; the connection is going away either way.
            }
            client.Dispose();
        }
    }
}
