using System.IO;
using System.Windows;
using LeadManager.Data;
using LeadManager.Services;
using LeadManager.Services.Email;
using LeadManager.ViewModels;
using LeadManager.Views;

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
        var database = new AppDatabase(Path.Combine(dataFolder, "leads.db"));
        var store = new LeadStore(new LeadRepository(database));
        var dialogs = new DialogService();
        var outreach = new Outreach(
            store,
            new EmailRepository(database),
            new EmailSettingsStore(Path.Combine(dataFolder, "email-settings.json")),
            new SmtpEmailSender(),
            dialogs);

        MainWindow = new MainWindow(new MainViewModel(store, dialogs, outreach));
        MainWindow.Show();
    }
}
