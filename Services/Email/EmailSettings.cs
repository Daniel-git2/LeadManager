using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LeadManager.Services.Email;

public enum MailSecurity
{
    /// <summary>Encrypted from the first byte (usually port 465 for SMTP, 993 for IMAP).</summary>
    SslOnConnect,

    /// <summary>Starts plain, then upgrades (usually port 587).</summary>
    StartTls,

    /// <summary>Let the library decide from the port.</summary>
    Auto,

    /// <summary>No encryption. Only for local test servers.</summary>
    None,
}

/// <summary>How to reach the mail server and who the emails come from.</summary>
public sealed class EmailSettings
{
    public string FromName { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 465;

    public MailSecurity SmtpSecurity { get; set; } = MailSecurity.SslOnConnect;

    public string Username { get; set; } = "";

    /// <summary>Held in memory only; saved encrypted by <see cref="EmailSettingsStore"/>.</summary>
    [JsonIgnore]
    public string Password { get; set; } = "";

    /// <summary>Put a copy of each sent email in the Sent folder over IMAP (many SMTP servers don't).</summary>
    public bool SaveToSentFolder { get; set; } = true;

    public string ImapHost { get; set; } = "";

    public int ImapPort { get; set; } = 993;

    public MailSecurity ImapSecurity { get; set; } = MailSecurity.SslOnConnect;

    /// <summary>Blank means find the Sent folder automatically.</summary>
    public string SentFolder { get; set; } = "";

    /// <summary>Wait between emails in a batch, so a burst doesn't look like spam to mail providers.</summary>
    public int PauseSeconds { get; set; } = 10;

    /// <summary>Added under every email, e.g. a postal address and an opt-out line.</summary>
    public string Footer { get; set; } = "";

    [JsonIgnore]
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost) && !string.IsNullOrWhiteSpace(FromAddress);

    public EmailSettings Clone() => (EmailSettings)MemberwiseClone();
}

public sealed record EmailPreset(
    string Name, string SmtpHost, int SmtpPort, MailSecurity SmtpSecurity,
    string ImapHost, int ImapPort, MailSecurity ImapSecurity, bool SaveToSentFolder, string Help)
{
    public static IReadOnlyList<EmailPreset> All { get; } =
    [
        new("Namecheap Private Email", "mail.privateemail.com", 465, MailSecurity.SslOnConnect, "mail.privateemail.com", 993, MailSecurity.SslOnConnect, true,
            "Use your full email address as the username and your mailbox password."),
        new("Google Workspace / Gmail", "smtp.gmail.com", 465, MailSecurity.SslOnConnect, "imap.gmail.com", 993, MailSecurity.SslOnConnect, false,
            "Use an app password (Google Account → Security → App passwords). Gmail files sent mail itself, so the IMAP copy is off."),
        new("Microsoft 365 / Outlook.com", "smtp.office365.com", 587, MailSecurity.StartTls, "outlook.office365.com", 993, MailSecurity.SslOnConnect, false,
            "Your admin must allow SMTP sign-in with a password; many Microsoft 365 accounts block it."),
        new("Zoho Mail", "smtp.zoho.com", 465, MailSecurity.SslOnConnect, "imap.zoho.com", 993, MailSecurity.SslOnConnect, true,
            "Use your full email address and password, or an app-specific password if you use two-factor sign-in."),
        new("Other", "", 465, MailSecurity.SslOnConnect, "", 993, MailSecurity.SslOnConnect, true,
            "Your email provider's help pages list these as “SMTP” (sending) and “IMAP” (receiving) settings."),
    ];
}

/// <summary>
/// Saves settings as JSON next to the database. The password is encrypted with Windows DPAPI, so only
/// this Windows account on this PC can read it back.
/// </summary>
public sealed class EmailSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LeadManager.EmailSettings");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    private readonly string _path;

    public EmailSettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    /// <summary>A copy of the saved settings; change it and pass it to <see cref="Save"/>.</summary>
    public EmailSettings Current { get; private set; }

    public void Save(EmailSettings settings)
    {
        var file = new SettingsFile
        {
            Settings = settings,
            EncryptedPassword = settings.Password.Length == 0
                ? null
                : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(settings.Password), Entropy, DataProtectionScope.CurrentUser)),
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(file, JsonOptions));
        Current = settings.Clone();
    }

    private EmailSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new EmailSettings();
        }
        try
        {
            var file = JsonSerializer.Deserialize<SettingsFile>(File.ReadAllText(_path), JsonOptions);
            var settings = file?.Settings ?? new EmailSettings();
            if (file?.EncryptedPassword is { } encrypted)
            {
                settings.Password = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(Convert.FromBase64String(encrypted), Entropy, DataProtectionScope.CurrentUser));
            }
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or CryptographicException or FormatException or IOException)
        {
            // A damaged file, or one copied from another PC/user: start over rather than fail to open.
            return new EmailSettings();
        }
    }

    private sealed class SettingsFile
    {
        public EmailSettings Settings { get; set; } = new();

        public string? EncryptedPassword { get; set; }
    }
}
