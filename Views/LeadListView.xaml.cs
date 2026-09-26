using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using LeadManager.Models;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class LeadListView : UserControl
{
    public LeadListView()
    {
        InitializeComponent();
    }

    private LeadListViewModel ViewModel => (LeadListViewModel)DataContext;

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void LeadsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = LeadsGrid.SelectedItems.Cast<Lead>().ToList();
        ViewModel.UpdateSelection(selected);

        var shown = LeadsGrid.Items.Count;
        SelectAllBox.IsChecked = selected.Count == 0 ? false : selected.Count == shown ? true : null;

        if (e.AddedItems.Count == 1 && selected.Count == 1)
        {
            LeadsGrid.ScrollIntoView(selected[0]);
        }
    }

    private void SelectAllBox_Click(object sender, RoutedEventArgs e)
    {
        // A partly ticked box means "some"; clicking it selects everything shown.
        if (LeadsGrid.SelectedItems.Count == LeadsGrid.Items.Count)
        {
            LeadsGrid.UnselectAll();
        }
        else
        {
            LeadsGrid.SelectAll();
        }
    }

    // A row's checkbox adds or removes just that row, like Ctrl+click, instead of replacing the selection.
    private void RowCheckBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>((DependencyObject)sender) is { } row)
        {
            row.IsSelected = !row.IsSelected;
            e.Handled = true;
        }
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e) => LeadsGrid.UnselectAll();

    private void StatusMenuButton_Click(object sender, RoutedEventArgs e) => OpenMenu(StatusMenuButton);

    private void FollowUpMenuButton_Click(object sender, RoutedEventArgs e) => OpenMenu(FollowUpMenuButton);

    private void LeadsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows open a lead; double-clicking a header, a checkbox or the empty area does nothing.
        if (e.OriginalSource is DependencyObject source
            && FindParent<CheckBox>(source) is null
            && ItemsControl.ContainerFromElement(LeadsGrid, source) is DataGridRow row)
        {
            ViewModel.ViewLeadCommand.Execute(row.Item);
        }
    }

    private void LeadsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when LeadsGrid.SelectedItems.Count > 0:
                LeadsGrid.UnselectAll();
                e.Handled = true;
                break;
            case Key.Enter when LeadsGrid.SelectedItem is { } item:
                ViewModel.ViewLeadCommand.Execute(LeadsGrid.CurrentItem as Lead ?? item);
                e.Handled = true;
                break;
            case Key.Delete when LeadsGrid.SelectedItems.Count > 0:
                ViewModel.DeleteLeadCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OpenMenu(Button button)
    {
        var menu = button.ContextMenu!;
        menu.DataContext = DataContext;
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current is not null;
             current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }
        return null;
    }
}
