using System.Diagnostics;
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
        var baseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LeadManager");
        var environment = AppEnvironment.FromCommandLine(e.Args, baseFolder);
        Directory.CreateDirectory(environment.DataFolder);

        var database = new AppDatabase(environment.DatabasePath);
        var store = new LeadStore(new LeadRepository(database));
        var dialogs = new DialogService();
        var outreach = new Outreach(
            store,
            new EmailRepository(database),
            new EmailSettingsStore(environment.EmailSettingsPath),
            environment,
            new SmtpEmailSender(),
            dialogs);
        var environments = new EnvironmentService(environment, database, store, RestartIn);

        MainWindow = new MainWindow(new MainViewModel(store, dialogs, outreach, environments));
        MainWindow.Show();
    }

    private void RestartIn(AppEnvironment target)
    {
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, target.CommandLineArguments) { UseShellExecute = false });
        Shutdown();
    }
}
