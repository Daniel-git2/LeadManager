using System.IO;
using LeadManager.Data;

namespace LeadManager.Services;

/// <summary>Switching between production and test, and refreshing test with a copy of production.</summary>
/// <param name="restartIn">Closes the app and starts it again in the given environment.</param>
public sealed class EnvironmentService(AppEnvironment current, AppDatabase database, LeadStore store, Action<AppEnvironment> restartIn)
{
    public AppEnvironment Current => current;

    public bool ProductionDataExists => File.Exists(current.Production.DatabasePath);

    public void SwitchToOther()
    {
        store.SavePending();
        restartIn(current.Other);
    }

    /// <summary>Replaces all test data (leads, templates, email history) with a copy of production.</summary>
    public void CopyProductionData()
    {
        if (!current.IsTest)
        {
            throw new InvalidOperationException("Production data can only be copied into the test environment.");
        }
        store.SavePending();
        database.ReplaceWith(current.Production.DatabasePath);
        store.Reload();
    }
}
