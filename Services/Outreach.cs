using LeadManager.Data;
using LeadManager.Models;
using LeadManager.Services.Email;
using LeadManager.ViewModels;

namespace LeadManager.Services;

/// <summary>Emailing leads from any screen: the composer, email settings and each lead's email history.</summary>
public sealed class Outreach(LeadStore store, EmailRepository emails, EmailSettingsStore settings, IEmailSender sender, IDialogService dialogs)
{
    public EmailRepository Emails => emails;

    public EmailSettingsStore Settings => settings;

    /// <summary>Opens the composer for these leads (setting up email first if needed). Returns how many were sent.</summary>
    public int Compose(IReadOnlyList<Lead> leads)
    {
        if (leads.Count == 0 || (!settings.Current.IsConfigured && !OpenSettings(firstTime: true)))
        {
            return 0;
        }
        var composer = new ComposeViewModel(store, this, sender, dialogs, leads);
        dialogs.ShowCompose(composer);
        store.SavePending();
        return composer.SentCount;
    }

    /// <summary>Opens email settings; true when they were saved.</summary>
    public bool OpenSettings(bool firstTime = false) =>
        dialogs.ShowEmailSettings(new EmailSettingsViewModel(settings, sender, firstTime));

    public List<SentEmail> HistoryFor(Lead lead) => emails.GetSentTo(lead.Id);
}
