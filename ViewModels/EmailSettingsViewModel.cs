using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Services.Email;
using MimeKit;

namespace LeadManager.ViewModels;

public sealed record SecurityOption(MailSecurity Value, string Label);

/// <summary>The email settings window: provider preset, account, and a way to check it works before saving.</summary>
public sealed partial class EmailSettingsViewModel : ObservableObject
{
    private readonly EmailSettingsStore _store;
    private readonly IEmailSender _sender;
    private bool _applyingPreset;

    public EmailSettingsViewModel(EmailSettingsStore store, IEmailSender sender, bool firstTime)
    {
        _store = store;
        _sender = sender;
        IsFirstTime = firstTime;

        var saved = store.Current;
        _fromName = saved.FromName;
        _fromAddress = saved.FromAddress;
        _smtpHost = saved.SmtpHost;
        _smtpPort = saved.SmtpPort;
        _smtpSecurity = saved.SmtpSecurity;
        _username = saved.Username;
        _saveToSentFolder = saved.SaveToSentFolder;
        _imapHost = saved.ImapHost;
        _imapPort = saved.ImapPort;
        _imapSecurity = saved.ImapSecurity;
        _sentFolder = saved.SentFolder;
        _pauseSeconds = saved.PauseSeconds;
        _footer = saved.Footer;
        HasSavedPassword = saved.Password.Length > 0;
        _selectedPreset = saved.SmtpHost.Length == 0
            ? null
            : EmailPreset.All.FirstOrDefault(p => p.SmtpHost.Equals(saved.SmtpHost, StringComparison.OrdinalIgnoreCase)) ?? EmailPreset.All[^1];
    }

    /// <summary>Raised after a successful save, so the window can close.</summary>
    public event EventHandler? Saved;

    public bool IsFirstTime { get; }

    public IReadOnlyList<EmailPreset> Presets => EmailPreset.All;

    public IReadOnlyList<SecurityOption> SecurityOptions { get; } =
    [
        new(MailSecurity.SslOnConnect, "SSL/TLS"),
        new(MailSecurity.StartTls, "STARTTLS"),
        new(MailSecurity.Auto, "Automatic"),
        new(MailSecurity.None, "None (not secure)"),
    ];

    /// <summary>Typed into the password box. Left blank, the saved password is kept.</summary>
    public string Password { get; set; } = "";

    public bool HasSavedPassword { get; }

    public string PasswordHint => HasSavedPassword ? "Saved. Leave blank to keep it." : "";

    public string PresetHelp => SelectedPreset?.Help ?? "Pick your email provider to fill in the server settings.";

    /// <summary>Server fields start open only when no preset covers them.</summary>
    public bool ShowServerDetails => SelectedPreset is null || SelectedPreset.SmtpHost.Length == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetHelp))]
    private EmailPreset? _selectedPreset;

    [ObservableProperty]
    private string _fromName;

    [ObservableProperty]
    private string _fromAddress;

    [ObservableProperty]
    private string _smtpHost;

    [ObservableProperty]
    private int _smtpPort;

    [ObservableProperty]
    private MailSecurity _smtpSecurity;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private bool _saveToSentFolder;

    [ObservableProperty]
    private string _imapHost;

    [ObservableProperty]
    private int _imapPort;

    [ObservableProperty]
    private MailSecurity _imapSecurity;

    [ObservableProperty]
    private string _sentFolder;

    [ObservableProperty]
    private int _pauseSeconds;

    [ObservableProperty]
    private string _footer;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(SendTestEmailCommand), nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _statusIsError;

    partial void OnSelectedPresetChanged(EmailPreset? value)
    {
        if (value is null || value.SmtpHost.Length == 0)
        {
            return;
        }
        _applyingPreset = true;
        SmtpHost = value.SmtpHost;
        SmtpPort = value.SmtpPort;
        SmtpSecurity = value.SmtpSecurity;
        ImapHost = value.ImapHost;
        ImapPort = value.ImapPort;
        ImapSecurity = value.ImapSecurity;
        SaveToSentFolder = value.SaveToSentFolder;
        _applyingPreset = false;
    }

    partial void OnFromAddressChanged(string? oldValue, string newValue)
    {
        // The username is almost always the email address; keep them in step until it's set on purpose.
        if (Username.Length == 0 || Username.Equals(oldValue, StringComparison.OrdinalIgnoreCase))
        {
            Username = newValue.Trim();
        }
    }

    partial void OnSmtpHostChanged(string value)
    {
        if (!_applyingPreset && SelectedPreset is { } preset && !preset.SmtpHost.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            SelectedPreset = EmailPreset.All[^1];
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task TestAsync()
    {
        if (Validate() is { } problem)
        {
            ShowStatus(problem, isError: true);
            return;
        }
        await RunAsync("Signing in…", async token => await _sender.TestAsync(Build(), token));
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task SendTestEmailAsync()
    {
        if (Validate() is { } problem)
        {
            ShowStatus(problem, isError: true);
            return;
        }
        var settings = Build();
        await RunAsync($"Sending a test email to {settings.FromAddress}…", async token =>
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName.Trim(), settings.FromAddress.Trim()));
            message.To.Add(new MailboxAddress(settings.FromName.Trim(), settings.FromAddress.Trim()));
            message.Subject = "Lead Manager test email";
            message.Body = new TextPart("plain") { Text = "This is a test from Lead Manager. If you can read this, sending works." };
            await using var session = await _sender.ConnectAsync(settings, token);
            var outcome = await session.SendAsync(message, token);
            if (!outcome.Sent)
            {
                throw new EmailException(outcome.Error ?? "The email wasn't sent.");
            }
            return outcome.Warning ?? $"Test email sent to {settings.FromAddress}. Check your inbox{(settings.SaveToSentFolder ? " and your Sent folder" : "")}.";
        });
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Save()
    {
        if (Validate() is { } problem)
        {
            ShowStatus(problem, isError: true);
            return;
        }
        try
        {
            _store.Save(Build());
            Saved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Couldn't save the settings: {ex.Message}", isError: true);
        }
    }

    private bool IsIdle() => !IsBusy;

    private async Task RunAsync(string busyText, Func<CancellationToken, Task<string>> action)
    {
        IsBusy = true;
        ShowStatus(busyText, isError: false);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            ShowStatus(await action(timeout.Token), isError: false);
        }
        catch (EmailException ex)
        {
            ShowStatus(ex.Message, isError: true);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("The server took too long to answer.", isError: true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowStatus(string text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
    }

    private string? Validate()
    {
        if (!EmailRenderer.IsSendableAddress(FromAddress))
        {
            return "Enter the email address you send from.";
        }
        if (SmtpHost.Trim().Length == 0 || SmtpPort is < 1 or > 65535)
        {
            return "Enter the sending (SMTP) server and port, or pick your provider above.";
        }
        if (SaveToSentFolder && (ImapHost.Trim().Length == 0 || ImapPort is < 1 or > 65535))
        {
            return "Enter the IMAP server and port, or turn off the Sent folder copy.";
        }
        if (Username.Trim().Length > 0 && Password.Length == 0 && !HasSavedPassword)
        {
            return "Enter your email password.";
        }
        if (PauseSeconds is < 0 or > 600)
        {
            return "The pause between emails should be between 0 and 600 seconds.";
        }
        return null;
    }

    private EmailSettings Build() => new()
    {
        FromName = FromName.Trim(),
        FromAddress = FromAddress.Trim(),
        SmtpHost = SmtpHost.Trim(),
        SmtpPort = SmtpPort,
        SmtpSecurity = SmtpSecurity,
        Username = Username.Trim(),
        Password = Password.Length > 0 ? Password : _store.Current.Password,
        SaveToSentFolder = SaveToSentFolder,
        ImapHost = ImapHost.Trim(),
        ImapPort = ImapPort,
        ImapSecurity = ImapSecurity,
        SentFolder = SentFolder.Trim(),
        PauseSeconds = PauseSeconds,
        Footer = Footer.Trim(),
    };
}
