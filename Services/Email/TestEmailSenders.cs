using System.IO;
using MimeKit;

namespace LeadManager.Services.Email;

/// <summary>
/// Test environment: saves each email as an .eml file (opens in Outlook or Windows Mail) instead of sending it.
/// </summary>
public sealed class OutboxEmailSender(string folder) : IEmailSender
{
    public Task<string> TestAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        return Task.FromResult($"Test emails will be saved in {folder}. Nothing is sent.");
    }

    public Task<IEmailSession> ConnectAsync(EmailSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        return Task.FromResult<IEmailSession>(new Session(folder));
    }

    private sealed class Session(string folder) : IEmailSession
    {
        public async Task<SendOutcome> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            var to = message.To.Mailboxes.FirstOrDefault()?.Address ?? "unknown";
            var safeTo = string.Concat(to.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            var path = Path.Combine(folder, $"{DateTime.Now:yyyy-MM-dd HHmmss-fff} {safeTo}.eml");
            try
            {
                await message.WriteToAsync(path, cancellationToken);
                return new SendOutcome(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SendOutcome(false, $"Couldn't save the test email: {ex.Message}", StopBatch: true);
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Test environment: sends through the real mail server, but every email goes to one address (yours), with the
/// lead it was meant for shown in the subject.
/// </summary>
public sealed class RedirectingEmailSender(IEmailSender inner, string recipient) : IEmailSender
{
    public async Task<string> TestAsync(EmailSettings settings, CancellationToken cancellationToken) =>
        $"{await inner.TestAsync(settings, cancellationToken)} Test emails will all go to {recipient}.";

    public async Task<IEmailSession> ConnectAsync(EmailSettings settings, CancellationToken cancellationToken) =>
        new Session(await inner.ConnectAsync(settings, cancellationToken), recipient);

    private sealed class Session(IEmailSession inner, string recipient) : IEmailSession
    {
        public Task<SendOutcome> SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            var intended = string.Join(", ", message.To.Mailboxes.Select(m => m.Address));
            message.Headers.Replace("X-LeadManager-Intended-To", intended);
            message.Subject = $"[Test for {intended}] {message.Subject}";
            message.To.Clear();
            message.To.Add(MailboxAddress.Parse(recipient));
            return inner.SendAsync(message, cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
