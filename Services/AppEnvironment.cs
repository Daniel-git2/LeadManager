using System.IO;

namespace LeadManager.Services;

/// <summary>
/// Which copy of the data the app is running against. Production uses the main data folder; Test has its own
/// database and email settings in a subfolder, and never emails leads.
/// </summary>
public sealed record AppEnvironment(bool IsTest, string BaseFolder)
{
    public const string CommandLineSwitch = "--env";

    public string Name => IsTest ? "Test" : "Production";

    public string DataFolder => IsTest ? Path.Combine(BaseFolder, "Test") : BaseFolder;

    public string DatabasePath => Path.Combine(DataFolder, "leads.db");

    public string EmailSettingsPath => Path.Combine(DataFolder, "email-settings.json");

    /// <summary>Where the test environment saves emails instead of sending them.</summary>
    public string OutboxFolder => Path.Combine(DataFolder, "Outbox");

    public AppEnvironment Production => this with { IsTest = false };

    public AppEnvironment Other => this with { IsTest = !IsTest };

    public string CommandLineArguments => $"{CommandLineSwitch} {Name.ToLowerInvariant()}";

    /// <summary>
    /// Test when started with "--env test" (or "--test"), or when LEADMANAGER_ENV=test; Production otherwise.
    /// </summary>
    public static AppEnvironment FromCommandLine(IReadOnlyList<string> args, string baseFolder)
    {
        string? requested = null;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                requested = "test";
            }
            else if (arg.StartsWith(CommandLineSwitch + "=", StringComparison.OrdinalIgnoreCase))
            {
                requested = arg[(CommandLineSwitch.Length + 1)..];
            }
            else if (arg.Equals(CommandLineSwitch, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                requested = args[++i];
            }
        }
        requested ??= Environment.GetEnvironmentVariable("LEADMANAGER_ENV");
        return new AppEnvironment(requested?.Trim().Equals("test", StringComparison.OrdinalIgnoreCase) == true, baseFolder);
    }
}
