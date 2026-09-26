using LeadManager.ViewModels;

namespace LeadManager.Services;

/// <summary>Windows and prompts the view models can open without knowing about WPF windows.</summary>
public interface IDialogService
{
    bool Confirm(string message, string title);

    void ShowError(string message, string title);

    string? PickCsvToOpen();

    string? PickCsvToSave(string suggestedFileName);

    /// <summary>Opens the lead window and returns once it's closed.</summary>
    void ShowLead(LeadDetailViewModel viewModel);

    /// <summary>Opens the new-lead form; true when a lead was added.</summary>
    bool ShowNewLead(NewLeadViewModel viewModel);
}
