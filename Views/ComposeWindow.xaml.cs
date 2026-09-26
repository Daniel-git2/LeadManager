using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class ComposeWindow : Window
{
    private readonly ComposeViewModel _viewModel;

    // Placeholders go into whichever of subject/body was used last.
    private TextBox _lastField;

    public ComposeWindow(ComposeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _lastField = BodyBox;

        EventHandler onCloseRequested = (_, _) => Close();
        viewModel.CloseRequested += onCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= onCloseRequested;

        var workArea = SystemParameters.WorkArea;
        Height = Math.Min(Height, workArea.Height - 40);
        Width = Math.Min(Width, workArea.Width - 40);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsSending)
        {
            // Let the current email finish cleanly; the window can close once sending has stopped.
            e.Cancel = true;
            if (MessageBox.Show(this, "Stop sending? Emails that already went out stay sent.", "Sending emails",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes)
            {
                _viewModel.StopCommand.Execute(null);
            }
        }
        base.OnClosing(e);
    }

    private void Field_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _lastField = (TextBox)sender;

    private void InsertPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        var token = "{{" + (string)((FrameworkElement)sender).Tag + "}}";
        _lastField.Focus();
        _lastField.SelectedText = token;
        _lastField.CaretIndex = _lastField.SelectionStart + token.Length;
    }

    private void TemplateMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = TemplateMenuButton.ContextMenu!;
        menu.DataContext = DataContext;
        menu.PlacementTarget = TemplateMenuButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
