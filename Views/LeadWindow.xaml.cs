using System.ComponentModel;
using System.Windows;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class LeadWindow : Window
{
    private readonly LeadDetailViewModel _viewModel;

    public LeadWindow(LeadDetailViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        EventHandler onCloseRequested = (_, _) => Close();
        viewModel.CloseRequested += onCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= onCloseRequested;

        // Fit smaller screens rather than opening taller than the desktop.
        var workArea = SystemParameters.WorkArea;
        Height = Math.Min(Height, workArea.Height - 40);
        Width = Math.Min(Width, workArea.Width - 40);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Store.SavePending();
        base.OnClosing(e);
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
