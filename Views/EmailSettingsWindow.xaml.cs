using System.Windows;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class EmailSettingsWindow : Window
{
    private readonly EmailSettingsViewModel _viewModel;

    public EmailSettingsWindow(EmailSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        EventHandler onSaved = (_, _) => DialogResult = true;
        viewModel.Saved += onSaved;
        Closed += (_, _) => viewModel.Saved -= onSaved;

        Height = Math.Min(Height, SystemParameters.WorkArea.Height - 40);
    }

    // PasswordBox can't be data-bound (by design, so the password isn't kept around in bindings).
    private void PasswordInput_PasswordChanged(object sender, RoutedEventArgs e) => _viewModel.Password = PasswordInput.Password;
}
