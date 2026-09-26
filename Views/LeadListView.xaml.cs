using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class LeadListView : UserControl
{
    public LeadListView()
    {
        InitializeComponent();
        LeadsGrid.SelectionChanged += (_, _) =>
        {
            if (LeadsGrid.SelectedItem is { } item)
            {
                LeadsGrid.ScrollIntoView(item);
            }
        };
    }

    private LeadListViewModel ViewModel => (LeadListViewModel)DataContext;

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void LeadsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows open a lead; double-clicking a column header or the empty area does nothing.
        if (ItemsControl.ContainerFromElement(LeadsGrid, e.OriginalSource as DependencyObject) is DataGridRow row)
        {
            ViewModel.ViewLeadCommand.Execute(row.Item);
        }
    }

    private void LeadsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (LeadsGrid.SelectedItem is null)
        {
            return;
        }
        if (e.Key == Key.Enter)
        {
            ViewModel.ViewLeadCommand.Execute(LeadsGrid.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            ViewModel.DeleteLeadCommand.Execute(LeadsGrid.SelectedItem);
            e.Handled = true;
        }
    }
}
