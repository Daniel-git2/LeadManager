using System.Windows;
using LeadManager.Services;
using LeadManager.ViewModels;
using Microsoft.Win32;

namespace LeadManager.Views;

public sealed class DialogService : IDialogService
{
    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

    // Prompts belong to whichever window the user is in, e.g. the lead window when deleting from it.
    private static Window? Owner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public bool Confirm(string message, string title) =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void ShowError(string message, string title) =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK);

    public string? PickCsvToOpen()
    {
        var dialog = new OpenFileDialog { Title = "Import leads from a CSV file", Filter = CsvFilter };
        return ShowFileDialog(dialog) ? dialog.FileName : null;
    }

    public string? PickCsvToSave(string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Title = "Export leads to a CSV file", Filter = CsvFilter, FileName = suggestedFileName };
        return ShowFileDialog(dialog) ? dialog.FileName : null;
    }

    public void ShowLead(LeadDetailViewModel viewModel) =>
        new LeadWindow(viewModel) { Owner = Owner }.ShowDialog();

    public bool ShowNewLead(NewLeadViewModel viewModel) =>
        new NewLeadWindow(viewModel) { Owner = Owner }.ShowDialog() == true;

    private static bool ShowFileDialog(CommonDialog dialog) =>
        (Owner is { } owner ? dialog.ShowDialog(owner) : dialog.ShowDialog()) == true;

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult) =>
        Owner is { } owner
            ? MessageBox.Show(owner, message, title, buttons, image, defaultResult)
            : MessageBox.Show(message, title, buttons, image, defaultResult);
}
