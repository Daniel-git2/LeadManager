using LeadManager.ViewModels;

namespace LeadManager.Services;

/// <summary>Windows and prompts the view models can open without knowing about WPF windows.</summary>
public interface IDialogService
{
    bool Confirm(string message, string title);

    void ShowError(string message, string title);

    /// <summary>Asks for a line of text; null when cancelled.</summary>
    string? PromptText(string title, string prompt, string initialText);

    string? PickCsvToOpen();

    string? PickCsvToSave(string suggestedFileName);

    string? PickMsgToOpen();

    /// <summary>Opens the lead window and returns once it's closed.</summary>
    void ShowLead(LeadDetailViewModel viewModel);

    /// <summary>Opens the new-lead form; true when a lead was added.</summary>
    bool ShowNewLead(NewLeadViewModel viewModel);

    /// <summary>Opens the email composer and returns once it's closed.</summary>
    void ShowCompose(ComposeViewModel viewModel);

    /// <summary>Opens email settings; true when they were saved.</summary>
    bool ShowEmailSettings(EmailSettingsViewModel viewModel);
}
