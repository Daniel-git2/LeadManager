using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Services;
using LeadManager.Services.Email;
using MimeKit;

namespace LeadManager.ViewModels;

public sealed record SecurityOption(MailSecurity Value, string Label);

/// <summary>The email settings window: provider preset, account, and a way to check it works before saving.</summary>
public sealed partial class EmailSettingsViewModel : ObservableObject
{
    private readonly EmailSettingsStore _store;
    private readonly Outreach _outreach;
    private bool _applyingPreset;
    private bool _loading;

    // Used when the password box is left blank: the saved password, or one copied from production.
    private string _fallbackPassword;

    public EmailSettingsViewModel(EmailSettingsStore store, Outreach outreach, bool firstTime)
    {
        _store = store;
        _outreach = outreach;
        IsFirstTime = firstTime;
        Load(store.Current);
        _fallbackPassword = store.Current.Password;
        _passwordHint = _fallbackPassword.Length > 0 ? "Saved. Leave blank to keep it." : "";
    }

    /// <summary>Raised after a successful save, so the window can close.</summary>
    public event EventHandler? Saved;

    public bool IsFirstTime { get; }

    public bool IsTestEnvironment => _outreach.Environment.IsTest;

    public string OutboxFolder => _outreach.Environment.OutboxFolder;

    public bool CanCopyFromProduction => IsTestEnvironment && File.Exists(_outreach.Environment.Production.EmailSettingsPath);

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

    public string PresetHelp => SelectedPreset?.Help ?? "Pick your email provider to fill in the server settings.";

    /// <summary>Server fields start open only when no preset covers them.</summary>
    public bool ShowServerDetails => SelectedPreset is null || SelectedPreset.SmtpHost.Length == 0;

    [ObservableProperty]
    private string _passwordHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PresetHelp))]
    private EmailPreset? _selectedPreset;

    [ObservableProperty]
    private string _fromName = "";

    [ObservableProperty]
    private string _fromAddress = "";

    [ObservableProperty]
    private string _smtpHost = "";

    [ObservableProperty]
    private int _smtpPort;

    [ObservableProperty]
    private MailSecurity _smtpSecurity;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private bool _saveToSentFolder;

    [ObservableProperty]
    private string _imapHost = "";

    [ObservableProperty]
    private int _imapPort;

    [ObservableProperty]
    private MailSecurity _imapSecurity;

    [ObservableProperty]
    private string _sentFolder = "";

    [ObservableProperty]
    private int _pauseSeconds;

    [ObservableProperty]
    private string _footer = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveTestEmailsToFolder), nameof(RedirectTestEmails))]
    private TestDelivery _testDelivery;

    [ObservableProperty]
    private string _testRecipient = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand), nameof(SendTestEmailCommand), nameof(SaveCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _status;

    [ObservableProperty]
    private bool _statusIsError;

    // Radio buttons bind to these; unticking one (because the other was picked) is ignored.
    public bool SaveTestEmailsToFolder
    {
        get => TestDelivery == TestDelivery.SaveToFolder;
        set
        {
            if (value)
            {
                TestDelivery = TestDelivery.SaveToFolder;
            }
        }
    }

    public bool RedirectTestEmails
    {
        get => TestDelivery == TestDelivery.RedirectToMe;
        set
        {
            if (value)
            {
                TestDelivery = TestDelivery.RedirectToMe;
                if (TestRecipient.Length == 0)
                {
                    TestRecipient = FromAddress.Trim();
                }
            }
        }
    }

    partial void OnSelectedPresetChanged(EmailPreset? value)
    {
        // Loading saved settings only shows which preset they match; it mustn't reset a custom port.
        if (_loading || value is null || value.SmtpHost.Length == 0)
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
        if (!_applyingPreset && !_loading && SelectedPreset is { } preset && !preset.SmtpHost.Equals(value, StringComparison.OrdinalIgnoreCase))
        {
            SelectedPreset = EmailPreset.All[^1];
        }
    }

    /// <summary>Test environment: fill in the account from production so redirecting to yourself works without retyping.</summary>
    [RelayCommand]
    private void CopyFromProduction()
    {
        var production = new EmailSettingsStore(_outreach.Environment.Production.EmailSettingsPath).Current;
        var delivery = TestDelivery;
        var recipient = TestRecipient;
        Load(production);
        TestDelivery = delivery;
        TestRecipient = recipient.Length > 0 ? recipient : production.FromAddress;
        _fallbackPassword = production.Password;
        PasswordHint = production.Password.Length > 0 ? "Using the password from production." : "";
        ShowStatus("Copied your production mail settings. Save to keep them for testing.", isError: false);
    }

    [RelayCommand]
    private void OpenOutbox()
    {
        Directory.CreateDirectory(OutboxFolder);
        System.Diagnostics.Process.Start("explorer.exe", $"\"{OutboxFolder}\"");
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task TestAsync()
    {
        if (Validate() is { } problem)
        {
            ShowStatus(problem, isError: true);
            return;
        }
        var settings = Build();
        await RunAsync("Signing in…", async token => await _outreach.SenderFor(settings).TestAsync(settings, token));
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
        var from = EmailRenderer.IsSendableAddress(settings.FromAddress) ? settings.FromAddress : "test@example.com";
        await RunAsync($"Sending a test email to {from}…", async token =>
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(settings.FromName.Trim(), from));
            message.To.Add(new MailboxAddress(settings.FromName.Trim(), from));
            message.Subject = "Lead Manager test email";
            message.Body = new TextPart("plain") { Text = "This is a test from Lead Manager. If you can read this, sending works." };
            await using var session = await _outreach.SenderFor(settings).ConnectAsync(settings, token);
            var outcome = await session.SendAsync(message, token);
            if (!outcome.Sent)
            {
                throw new EmailException(outcome.Error ?? "The email wasn't sent.");
            }
            if (IsTestEnvironment && settings.TestDelivery == TestDelivery.SaveToFolder)
            {
                return $"Test email saved in {OutboxFolder}.";
            }
            var to = IsTestEnvironment ? settings.TestRecipient : from;
            return outcome.Warning ?? $"Test email sent to {to}. Check your inbox{(settings.SaveToSentFolder ? " and your Sent folder" : "")}.";
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

    private void Load(EmailSettings saved)
    {
        _loading = true;
        FromName = saved.FromName;
        FromAddress = saved.FromAddress;
        SmtpHost = saved.SmtpHost;
        SmtpPort = saved.SmtpPort;
        SmtpSecurity = saved.SmtpSecurity;
        Username = saved.Username;
        SaveToSentFolder = saved.SaveToSentFolder;
        ImapHost = saved.ImapHost;
        ImapPort = saved.ImapPort;
        ImapSecurity = saved.ImapSecurity;
        SentFolder = saved.SentFolder;
        PauseSeconds = saved.PauseSeconds;
        Footer = saved.Footer;
        TestDelivery = saved.TestDelivery;
        TestRecipient = saved.TestRecipient;
        SelectedPreset = saved.SmtpHost.Length == 0
            ? null
            : EmailPreset.All.FirstOrDefault(p => p.SmtpHost.Equals(saved.SmtpHost, StringComparison.OrdinalIgnoreCase)) ?? EmailPreset.All[^1];
        _loading = false;
    }

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
        if (PauseSeconds is < 0 or > 600)
        {
            return "The pause between emails should be between 0 and 600 seconds.";
        }
        if (IsTestEnvironment && TestDelivery == TestDelivery.SaveToFolder)
        {
            // Nothing is sent, so no account is needed; a sender address is optional.
            return FromAddress.Trim().Length == 0 || EmailRenderer.IsSendableAddress(FromAddress)
                ? null
                : "That sender address doesn't look right. Fix it or leave it blank.";
        }
        if (IsTestEnvironment && !EmailRenderer.IsSendableAddress(TestRecipient))
        {
            return "Enter the address that should receive every test email.";
        }
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
        if (Username.Trim().Length > 0 && Password.Length == 0 && _fallbackPassword.Length == 0)
        {
            return "Enter your email password.";
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
        Password = Password.Length > 0 ? Password : _fallbackPassword,
        SaveToSentFolder = SaveToSentFolder,
        ImapHost = ImapHost.Trim(),
        ImapPort = ImapPort,
        ImapSecurity = ImapSecurity,
        SentFolder = SentFolder.Trim(),
        PauseSeconds = PauseSeconds,
        Footer = Footer.Trim(),
        TestDelivery = TestDelivery,
        TestRecipient = TestRecipient.Trim(),
    };
}
