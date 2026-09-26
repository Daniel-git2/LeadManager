using System.Windows;
using LeadManager.ViewModels;

namespace LeadManager.Views;

public partial class NewLeadWindow : Window
{
    public NewLeadWindow(NewLeadViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        EventHandler onCompleted = (_, _) => DialogResult = true;
        viewModel.Completed += onCompleted;
        Closed += (_, _) => viewModel.Completed -= onCompleted;
        Loaded += (_, _) => FirstField.Focus();
    }
}
