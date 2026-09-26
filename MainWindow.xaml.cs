using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using LeadManager.ViewModels;

namespace LeadManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;

        viewModel.LeadAdded += (_, _) => Dispatcher.BeginInvoke(() => ContactNameBox.Focus(), DispatcherPriority.Input);
        LeadsGrid.SelectionChanged += (_, _) =>
        {
            if (LeadsGrid.SelectedItem is { } item)
            {
                LeadsGrid.ScrollIntoView(item);
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.SavePending();
        if (_viewModel.HasUnsavedChanges)
        {
            var answer = MessageBox.Show($"{_viewModel.StatusMessage}\n\nClose anyway and lose that change?", "Lead Manager",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            e.Cancel = answer != MessageBoxResult.Yes;
        }
        base.OnClosing(e);
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void AddDatedNote_Click(object sender, RoutedEventArgs e)
    {
        // Appends a dated line so the notes double as a simple contact log.
        var existing = NotesBox.Text.TrimEnd();
        NotesBox.Text = (existing.Length > 0 ? existing + Environment.NewLine : "") + $"{DateTime.Today:yyyy-MM-dd}: ";
        NotesBox.Focus();
        NotesBox.CaretIndex = NotesBox.Text.Length;
        NotesBox.ScrollToEnd();
    }
}
