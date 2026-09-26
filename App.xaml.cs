using System.IO;
using System.Windows;
using LeadManager.Data;
using LeadManager.ViewModels;

namespace LeadManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Something went wrong:\n\n{args.Exception.Message}", "Lead Manager", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Kept out of OneDrive on purpose: sync clients can corrupt a SQLite file that's open. Use Export CSV for backups.
        var dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeadManager");
        Directory.CreateDirectory(dataFolder);
        var repository = new LeadRepository(Path.Combine(dataFolder, "leads.db"));

        MainWindow = new MainWindow(new MainViewModel(repository));
        MainWindow.Show();
    }
}
