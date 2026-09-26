using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
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
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _viewModel.Store.SavePending();
        if (_viewModel.Store.SaveError is { } error)
        {
            var answer = MessageBox.Show(this, $"{error}\n\nClose anyway and lose that change?", "Lead Manager",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            e.Cancel = answer != MessageBoxResult.Yes;
        }
        base.OnClosing(e);
    }

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        _viewModel.IsLeadsPage = true;
        // Wait for the page to become visible before focusing it.
        Dispatcher.BeginInvoke(LeadListPage.FocusSearch, DispatcherPriority.Input);
    }

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = MoreButton.ContextMenu!;
        menu.DataContext = DataContext;
        menu.PlacementTarget = MoreButton;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedCsv(e) is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (DroppedCsv(e) is { } path)
        {
            _viewModel.ImportFile(path);
        }
    }

    private static string? DroppedCsv(DragEventArgs e) =>
        (e.Data.GetData(DataFormats.FileDrop) as string[])?
            .FirstOrDefault(f => Path.GetExtension(f).Equals(".csv", StringComparison.OrdinalIgnoreCase));
}
