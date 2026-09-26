using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsvHelper;
using LeadManager.Services;
using Microsoft.Data.Sqlite;

namespace LeadManager.ViewModels;

/// <summary>The main window: page switching plus the actions available from every page.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly Outreach _outreach;

    public MainViewModel(LeadStore store, IDialogService dialogs, Outreach outreach)
    {
        Store = store;
        _dialogs = dialogs;
        _outreach = outreach;
        LeadList = new LeadListViewModel(store, dialogs, Notice, outreach);
        Dashboard = new DashboardViewModel(store, dialogs, outreach, ShowLeads);
        _currentPage = Dashboard;

        store.PropertyChanged += OnStorePropertyChanged;
        store.Changed += (_, _) => OnPropertyChanged(nameof(LeadCount));
    }

    public LeadStore Store { get; }

    public Notice Notice { get; } = new();

    public DashboardViewModel Dashboard { get; }

    public LeadListViewModel LeadList { get; }

    public int LeadCount => Store.Leads.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardPage), nameof(IsLeadsPage))]
    private ObservableObject _currentPage;

    public bool IsDashboardPage
    {
        get => CurrentPage == Dashboard;
        set
        {
            if (value)
            {
                CurrentPage = Dashboard;
            }
        }
    }

    public bool IsLeadsPage
    {
        get => CurrentPage == LeadList;
        set
        {
            if (value)
            {
                CurrentPage = LeadList;
            }
        }
    }

    partial void OnCurrentPageChanged(ObservableObject value)
    {
        if (value == Dashboard)
        {
            Dashboard.Refresh();
        }
    }

    [RelayCommand]
    private void GoToDashboard() => IsDashboardPage = true;

    [RelayCommand]
    private void GoToLeads() => IsLeadsPage = true;

    [RelayCommand]
    private void NewLead()
    {
        var form = new NewLeadViewModel(Store);
        if (_dialogs.ShowNewLead(form) && form.Created is { } lead)
        {
            IsLeadsPage = true;
            LeadList.Select(lead);
            Notice.Show($"Added {lead.DisplayName}.");
        }
    }

    [RelayCommand]
    private void ImportCsv()
    {
        if (_dialogs.PickCsvToOpen() is { } path)
        {
            ImportFile(path);
        }
    }

    /// <summary>Imports a CSV picked in the file dialog or dropped onto the window.</summary>
    public void ImportFile(string path)
    {
        try
        {
            var result = Store.Import(path);
            var parts = new List<string> { $"{result.Added.Count} new", $"{result.Updated.Count} updated" };
            if (result.Skipped > 0)
            {
                parts.Add($"{result.Skipped} empty rows skipped");
            }
            Notice.Show($"Imported {Path.GetFileName(path)}: {string.Join(", ", parts)}.", seconds: 8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or CsvHelperException or SqliteException)
        {
            _dialogs.ShowError($"Couldn't import {Path.GetFileName(path)}.\n\n{ex.Message}", "Import CSV");
        }
    }

    [RelayCommand]
    private void ExportAll() => Export(Store.Leads.ToList(), $"Leads {DateTime.Today:yyyy-MM-dd}.csv");

    [RelayCommand]
    private void ExportShown() =>
        Export(LeadList.LeadsView.Cast<Models.Lead>().ToList(), $"Leads (filtered) {DateTime.Today:yyyy-MM-dd}.csv");

    [RelayCommand]
    private void ShowDatabaseFile() => LeadActions.ShowInExplorer(Store.DatabasePath);

    [RelayCommand]
    private void OpenEmailSettings()
    {
        if (_outreach.OpenSettings())
        {
            Notice.Show("Email settings saved.");
        }
    }

    private void Export(List<Models.Lead> leads, string suggestedName)
    {
        if (_dialogs.PickCsvToSave(suggestedName) is not { } path)
        {
            return;
        }
        try
        {
            Store.Export(path, leads);
            Notice.Show($"Exported {leads.Count} leads to {Path.GetFileName(path)}.", seconds: 6);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError($"Couldn't write {Path.GetFileName(path)}.\n\n{ex.Message}", "Export CSV");
        }
    }

    private void ShowLeads(string quickFilter, string status)
    {
        LeadList.ShowOnly(quickFilter, status);
        IsLeadsPage = true;
    }

    private void OnStorePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LeadStore.SaveError))
        {
            return;
        }
        if (Store.SaveError is { } error)
        {
            Notice.ShowError(error);
        }
        else if (Notice.IsError)
        {
            Notice.Dismiss();
        }
    }
}
