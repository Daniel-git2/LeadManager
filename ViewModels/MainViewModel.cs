using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CsvHelper;
using LeadManager.Data;
using LeadManager.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace LeadManager.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string AllStatuses = "All statuses";
    private const string AllPriorities = "All priorities";
    private const string Everyone = "Contacted or not";
    private const string OnlyNotContacted = "Not contacted";
    private const string OnlyContacted = "Contacted";
    private const string OnlyFollowUpDue = "Follow-up due";
    private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";

    // Computed or repository-managed properties; changes to these don't need saving.
    private static readonly HashSet<string> UnsavedProperties =
    [
        nameof(Lead.IsContacted), nameof(Lead.IsFollowUpDue), nameof(Lead.PriorityRank),
        nameof(Lead.DisplayName), nameof(Lead.Origin), nameof(Lead.CreatedAt), nameof(Lead.UpdatedAt),
    ];

    private readonly LeadRepository _repository;

    // Edits save shortly after typing pauses rather than on every keystroke.
    private readonly HashSet<Lead> _unsaved = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _syncingContacted;
    private bool _showingSaveError;

    public MainViewModel(LeadRepository repository)
    {
        _repository = repository;
        LeadsView = CollectionViewSource.GetDefaultView(Leads);
        LeadsView.Filter = item => IsShown((Lead)item);
        LeadsView.SortDescriptions.Add(new SortDescription(nameof(Lead.LeadCode), ListSortDirection.Ascending));
        _saveTimer.Tick += (_, _) => SavePending();
        LoadLeads();
    }

    /// <summary>Raised after a new blank lead is created, so the view can put the cursor in it.</summary>
    public event EventHandler? LeadAdded;

    public ObservableCollection<Lead> Leads { get; } = [];

    public ICollectionView LeadsView { get; }

    public string DatabasePath => _repository.DatabasePath;

    public string[] StatusFilterOptions { get; } = [AllStatuses, .. LeadOptions.Statuses];

    public string[] PriorityFilterOptions { get; } = [AllPriorities, .. LeadOptions.Priorities];

    public string[] ContactFilterOptions { get; } = [Everyone, OnlyNotContacted, OnlyContacted, OnlyFollowUpDue];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteLeadCommand), nameof(MarkContactedTodayCommand), nameof(CopyEmailCommand),
        nameof(SendEmailCommand), nameof(OpenWebsiteCommand))]
    private Lead? _selectedLead;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusFilter = AllStatuses;

    [ObservableProperty]
    private string _priorityFilter = AllPriorities;

    [ObservableProperty]
    private string _contactFilter = Everyone;

    [ObservableProperty]
    private string _statusMessage = "";

    public bool HasUnsavedChanges => _unsaved.Count > 0;

    public string Summary
    {
        get
        {
            var total = Leads.Count;
            var shown = LeadsView.Cast<object>().Count();
            var contacted = Leads.Count(l => l.IsContacted);
            var due = Leads.Count(l => l.IsFollowUpDue);
            var showing = shown == total ? $"{total} leads" : $"{shown} of {total} leads shown";
            return $"{showing}   ·   {contacted} contacted   ·   {total - contacted} not contacted   ·   {due} due for follow-up";
        }
    }

    partial void OnSearchTextChanged(string value) => RefreshView();

    partial void OnStatusFilterChanged(string value) => RefreshView();

    partial void OnPriorityFilterChanged(string value) => RefreshView();

    partial void OnContactFilterChanged(string value) => RefreshView();

    public void SavePending()
    {
        _saveTimer.Stop();
        foreach (var lead in _unsaved.ToList())
        {
            try
            {
                _repository.Update(lead);
                _unsaved.Remove(lead);
            }
            catch (SqliteException ex)
            {
                var reason = ex.SqliteErrorCode == 19 && ex.Message.Contains("LeadCode")
                    ? $"Lead ID \"{lead.LeadCode}\" is already used by another lead."
                    : ex.Message;
                StatusMessage = $"Couldn't save {lead.DisplayName}: {reason}";
                _showingSaveError = true;
            }
        }
        if (_unsaved.Count == 0 && _showingSaveError)
        {
            StatusMessage = "";
            _showingSaveError = false;
        }
    }

    [RelayCommand]
    private void AddLead()
    {
        var lead = new Lead();
        _repository.Insert(lead);
        lead.PropertyChanged += OnLeadPropertyChanged;

        SearchText = "";
        StatusFilter = AllStatuses;
        PriorityFilter = AllPriorities;
        ContactFilter = Everyone;

        Leads.Add(lead);
        SelectedLead = lead;
        OnPropertyChanged(nameof(Summary));
        LeadAdded?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteLead()
    {
        if (SelectedLead is not { } lead)
        {
            return;
        }
        var answer = MessageBox.Show($"Delete {lead.DisplayName}? This can't be undone.", "Delete lead",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _unsaved.Remove(lead);
        _repository.Delete(lead.Id);
        lead.PropertyChanged -= OnLeadPropertyChanged;
        Leads.Remove(lead);
        StatusMessage = $"Deleted {lead.DisplayName}.";
        OnPropertyChanged(nameof(Summary));
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MarkContactedToday()
    {
        var lead = SelectedLead!;
        var contactedBefore = lead.ContactedOn is { } previous && previous.Date < DateTime.Today;
        lead.ContactedOn = DateTime.Today;
        // A second contact on a later day is a follow-up.
        if (contactedBefore && lead.Status.Equals(LeadOptions.Contacted, StringComparison.OrdinalIgnoreCase))
        {
            lead.Status = LeadOptions.FollowedUp;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void CopyEmail()
    {
        var email = SelectedLead!.Email.Trim();
        if (email.Length == 0)
        {
            StatusMessage = "This lead has no email address.";
            return;
        }
        try
        {
            Clipboard.SetText(email);
            StatusMessage = $"Copied {email}";
        }
        catch (COMException)
        {
            StatusMessage = "The clipboard is busy. Try again.";
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SendEmail()
    {
        var email = SelectedLead!.Email.Trim();
        if (!email.Contains('@') || email.Any(char.IsWhiteSpace))
        {
            StatusMessage = "This lead doesn't have a usable email address.";
            return;
        }
        OpenExternal($"mailto:{email}");
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenWebsite()
    {
        var url = SelectedLead!.SourceUrls
            .Split(['|', ',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        if (url is null)
        {
            StatusMessage = "This lead has no website link.";
            return;
        }
        OpenExternal(url);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Process.Start("explorer.exe", $"/select,\"{DatabasePath}\"");
    }

    [RelayCommand]
    private void ImportCsv()
    {
        var dialog = new OpenFileDialog { Title = "Import leads from a CSV file", Filter = CsvFilter };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SavePending();
        try
        {
            // Merge against fresh copies so the on-screen leads only change once the import has been saved.
            var result = LeadCsv.Import(dialog.FileName, _repository.GetAll());
            _repository.SaveImport(result.Added, result.Updated);
            LoadLeads(SelectedLead?.Id);

            var message = $"Imported {Path.GetFileName(dialog.FileName)}: {result.Added.Count} new, {result.Updated.Count} updated";
            StatusMessage = result.Skipped > 0 ? $"{message}, {result.Skipped} empty rows skipped." : $"{message}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or CsvHelperException or SqliteException)
        {
            MessageBox.Show($"Couldn't import that file.\n\n{ex.Message}", "Import CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export the leads shown in the list",
            Filter = CsvFilter,
            FileName = $"Leads {DateTime.Today:yyyy-MM-dd}.csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        SavePending();
        var leads = LeadsView.Cast<Lead>().ToList();
        try
        {
            LeadCsv.Export(dialog.FileName, leads);
            StatusMessage = $"Exported {leads.Count} leads to {Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Couldn't write that file.\n\n{ex.Message}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool HasSelection() => SelectedLead is not null;

    private void LoadLeads(long? selectId = null)
    {
        foreach (var lead in Leads)
        {
            lead.PropertyChanged -= OnLeadPropertyChanged;
        }
        Leads.Clear();
        foreach (var lead in _repository.GetAll())
        {
            lead.PropertyChanged += OnLeadPropertyChanged;
            Leads.Add(lead);
        }
        SelectedLead = Leads.FirstOrDefault(l => l.Id == selectId);
        OnPropertyChanged(nameof(Summary));
    }

    private void OnLeadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Lead lead || e.PropertyName is null || UnsavedProperties.Contains(e.PropertyName))
        {
            return;
        }

        if (!_syncingContacted)
        {
            _syncingContacted = true;
            try
            {
                SyncContacted(lead, e.PropertyName);
            }
            finally
            {
                _syncingContacted = false;
            }
        }

        _unsaved.Add(lead);
        _saveTimer.Stop();
        _saveTimer.Start();

        if (e.PropertyName is nameof(Lead.ContactedOn) or nameof(Lead.FollowUpOn))
        {
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Keeps the status and the contacted date telling the same story.</summary>
    private static void SyncContacted(Lead lead, string changedProperty)
    {
        if (changedProperty == nameof(Lead.ContactedOn))
        {
            if (lead.ContactedOn is not null && LeadOptions.IsNotContacted(lead.Status))
            {
                lead.Status = LeadOptions.Contacted;
            }
            else if (lead.ContactedOn is null && lead.Status.Equals(LeadOptions.Contacted, StringComparison.OrdinalIgnoreCase))
            {
                lead.Status = LeadOptions.NotContacted;
            }
        }
        else if (changedProperty == nameof(Lead.Status))
        {
            if (LeadOptions.ImpliesContacted(lead.Status) && lead.ContactedOn is null)
            {
                lead.ContactedOn = DateTime.Today;
            }
            else if (LeadOptions.IsNotContacted(lead.Status))
            {
                lead.ContactedOn = null;
            }
        }
    }

    private bool IsShown(Lead lead)
    {
        if (StatusFilter != AllStatuses && !lead.Status.Equals(StatusFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (PriorityFilter != AllPriorities && !lead.FitPriority.Equals(PriorityFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        var contactMatch = ContactFilter switch
        {
            OnlyNotContacted => !lead.IsContacted,
            OnlyContacted => lead.IsContacted,
            OnlyFollowUpDue => lead.IsFollowUpDue,
            _ => true,
        };
        if (!contactMatch)
        {
            return false;
        }

        // Every word typed has to appear somewhere in the lead.
        var searchable = new[] { lead.LeadCode, lead.ContactName, lead.Practice, lead.Email, lead.Location, lead.Subjects, lead.Notes, lead.Caveats };
        return SearchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => searchable.Any(field => field?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
    }

    private void RefreshView()
    {
        LeadsView.Refresh();
        OnPropertyChanged(nameof(Summary));
    }

    private void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            StatusMessage = $"Couldn't open {target}: {ex.Message}";
        }
    }
}
