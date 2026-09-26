using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LeadManager.Data;
using LeadManager.Models;
using Microsoft.Data.Sqlite;

namespace LeadManager.Services;

/// <summary>
/// The in-memory list of leads every screen shares. Edits to any lead are saved automatically shortly after
/// they happen, and the status and contacted date are kept consistent with each other.
/// </summary>
public sealed partial class LeadStore : ObservableObject
{
    // Settable Lead properties are table columns (get-only ones are computed). These few are managed here.
    private static readonly HashSet<string> EditableColumns = typeof(Lead).GetProperties()
        .Where(p => p.CanWrite && p.Name is not (nameof(Lead.Id) or nameof(Lead.IsContacted) or nameof(Lead.CreatedAt) or nameof(Lead.UpdatedAt)))
        .Select(p => p.Name)
        .ToHashSet();

    // Columns that counts, lists and the dashboard depend on.
    private static readonly HashSet<string> SummaryColumns =
    [
        nameof(Lead.Status), nameof(Lead.ContactedOn), nameof(Lead.FollowUpOn), nameof(Lead.FitPriority),
        nameof(Lead.ContactName), nameof(Lead.Practice), nameof(Lead.Email),
    ];

    private readonly LeadRepository _repository;

    // Edits save shortly after typing pauses rather than on every keystroke.
    private readonly HashSet<Lead> _unsaved = [];
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _syncingContacted;

    public LeadStore(LeadRepository repository)
    {
        _repository = repository;
        _saveTimer.Tick += (_, _) => SavePending();
        Load();
    }

    /// <summary>Raised when leads are added, removed or reloaded, or when an edit changes what summaries show.</summary>
    public event EventHandler? Changed;

    public ObservableCollection<Lead> Leads { get; } = [];

    public string DatabasePath => _repository.DatabasePath;

    public bool HasUnsavedChanges => _unsaved.Count > 0;

    /// <summary>Why the last save failed, or null when everything is saved.</summary>
    [ObservableProperty]
    private string? _saveError;

    public void Add(Lead lead)
    {
        _repository.Insert(lead);
        lead.PropertyChanged += OnLeadPropertyChanged;
        Leads.Add(lead);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Delete(Lead lead)
    {
        _unsaved.Remove(lead);
        _repository.Delete(lead.Id);
        lead.PropertyChanged -= OnLeadPropertyChanged;
        Leads.Remove(lead);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Imports a CSV, saves it in one transaction, then reloads.</summary>
    public ImportResult Import(string path)
    {
        SavePending();
        // Merge against fresh copies so the on-screen leads only change once the import has been saved.
        var result = LeadCsv.Import(path, _repository.GetAll());
        _repository.SaveImport(result.Added, result.Updated);
        Load();
        return result;
    }

    public void Export(string path, IEnumerable<Lead> leads)
    {
        SavePending();
        LeadCsv.Export(path, leads);
    }

    public void MarkContactedToday(Lead lead)
    {
        var contactedBefore = lead.ContactedOn is { } previous && previous.Date < DateTime.Today;
        lead.ContactedOn = DateTime.Today;
        // A second contact on a later day is a follow-up.
        if (contactedBefore && lead.Status.Equals(LeadOptions.Contacted, StringComparison.OrdinalIgnoreCase))
        {
            lead.Status = LeadOptions.FollowedUp;
        }
    }

    /// <summary>Adds a dated line to the end of the lead's notes, like the "Add dated note" button.</summary>
    public void AddNote(Lead lead, string note)
    {
        var existing = lead.Notes.TrimEnd();
        lead.Notes = (existing.Length > 0 ? existing + Environment.NewLine : "") + $"{DateTime.Today:yyyy-MM-dd}: {note}";
    }

    public Lead? FindByEmail(string email) =>
        Leads.FirstOrDefault(l => l.Email.Trim().Equals(email.Trim(), StringComparison.OrdinalIgnoreCase));

    public Lead? FindByLeadCode(string code) =>
        Leads.FirstOrDefault(l => string.Equals(l.LeadCode?.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Continues the most common ID pattern, e.g. ST-100 → ST-101. Null when there's no pattern.</summary>
    public string? SuggestNextLeadCode()
    {
        var numbered = Leads
            .Select(l => Regex.Match(l.LeadCode ?? "", @"^(?<prefix>.*?)(?<number>\d+)$"))
            .Where(m => m.Success && long.TryParse(m.Groups["number"].Value, out _))
            .GroupBy(m => m.Groups["prefix"].Value)
            .MaxBy(g => g.Count());
        if (numbered is null)
        {
            return null;
        }
        var digits = numbered.Max(m => m.Groups["number"].Value.Length);
        var next = numbered.Max(m => long.Parse(m.Groups["number"].Value)) + 1;
        return numbered.Key + next.ToString().PadLeft(digits, '0');
    }

    public void SavePending()
    {
        _saveTimer.Stop();
        string? error = null;
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
                error = $"Couldn't save {lead.DisplayName}: {reason}";
            }
        }
        SaveError = error;
    }

    /// <summary>
    /// Re-reads every lead after the database file was replaced. Unsaved edits are dropped, since they
    /// belong to the old data; call <see cref="SavePending"/> before replacing it.
    /// </summary>
    public void Reload()
    {
        _saveTimer.Stop();
        _unsaved.Clear();
        SaveError = null;
        Load();
    }

    private void Load()
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
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnLeadPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not Lead lead || e.PropertyName is not { } property || !EditableColumns.Contains(property))
        {
            return;
        }

        if (!_syncingContacted)
        {
            _syncingContacted = true;
            try
            {
                SyncContacted(lead, property);
            }
            finally
            {
                _syncingContacted = false;
            }
        }

        _unsaved.Add(lead);
        _saveTimer.Stop();
        _saveTimer.Start();

        if (SummaryColumns.Contains(property))
        {
            Changed?.Invoke(this, EventArgs.Empty);
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
}
