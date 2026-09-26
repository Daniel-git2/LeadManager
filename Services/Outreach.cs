using LeadManager.Data;
using LeadManager.Models;
using LeadManager.Services.Email;
using LeadManager.ViewModels;

namespace LeadManager.Services;

/// <summary>Emailing leads from any screen: the composer, email settings and each lead's email history.</summary>
/// <param name="smtp">The real sender. In the test environment it's only used to redirect emails to you.</param>
public sealed class Outreach(
    LeadStore store, EmailRepository emails, EmailSettingsStore settings, AppEnvironment environment, IEmailSender smtp, IDialogService dialogs)
{
    public EmailRepository Emails => emails;

    public EmailSettingsStore Settings => settings;

    public AppEnvironment Environment => environment;

    /// <summary>
    /// The saved settings, with a placeholder sender filled in when the test environment is only saving
    /// emails to a folder and no account has been set up.
    /// </summary>
    public EmailSettings CurrentSettings
    {
        get
        {
            var current = settings.Current.Clone();
            if (IsSavingToFolder && !EmailRenderer.IsSendableAddress(current.FromAddress))
            {
                current.FromAddress = "test@example.com";
                current.FromName = current.FromName.Length > 0 ? current.FromName : "Lead Manager test";
            }
            return current;
        }
    }

    /// <summary>True when emails can go out without visiting settings first.</summary>
    public bool IsReady => IsReadyWith(settings.Current);

    /// <summary>Explains where emails go in the test environment; null in production.</summary>
    public string? TestModeNotice => TestModeNoticeFor(settings.Current);

    /// <summary>Short form for the main window's test banner; null in production.</summary>
    public string? TestModeSummary =>
        !environment.IsTest ? null
        : settings.Current.TestDelivery == TestDelivery.SaveToFolder
            ? "Separate test data. Emails are saved to the outbox, not sent."
            : $"Separate test data. Emails go to {settings.Current.TestRecipient.Trim()}, never to leads.";

    private bool IsSavingToFolder => environment.IsTest && settings.Current.TestDelivery == TestDelivery.SaveToFolder;

    /// <summary>Opens the composer for these leads (setting up email first if needed). Returns how many were sent.</summary>
    public int Compose(IReadOnlyList<Lead> leads)
    {
        if (leads.Count == 0 || (!IsReady && !OpenSettings(firstTime: true)))
        {
            return 0;
        }
        var composer = new ComposeViewModel(store, this, dialogs, leads);
        dialogs.ShowCompose(composer);
        store.SavePending();
        return composer.SentCount;
    }

    /// <summary>Opens email settings; true when they were saved.</summary>
    public bool OpenSettings(bool firstTime = false) =>
        dialogs.ShowEmailSettings(new EmailSettingsViewModel(settings, this, firstTime));

    public List<SentEmail> HistoryFor(Lead lead) => emails.GetSentTo(lead.Id);

    /// <summary>How emails would be delivered with these settings. In test, never to the real lead.</summary>
    public IEmailSender SenderFor(EmailSettings candidate) =>
        !environment.IsTest ? smtp
        : candidate.TestDelivery == TestDelivery.SaveToFolder ? new OutboxEmailSender(environment.OutboxFolder)
        : new RedirectingEmailSender(smtp, candidate.TestRecipient.Trim());

    public bool IsReadyWith(EmailSettings candidate) =>
        environment.IsTest && candidate.TestDelivery == TestDelivery.SaveToFolder
        || candidate.IsConfigured && (!environment.IsTest || EmailRenderer.IsSendableAddress(candidate.TestRecipient));

    public string? TestModeNoticeFor(EmailSettings candidate) =>
        !environment.IsTest ? null
        : candidate.TestDelivery == TestDelivery.SaveToFolder
            ? "Test environment: emails are saved as files in the Outbox folder. Nothing is sent to anyone."
            : $"Test environment: every email goes to {candidate.TestRecipient.Trim()} instead of the lead.";
}
