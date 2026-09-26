using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Models;
using LeadManager.Services;

namespace LeadManager.ViewModels;

/// <summary>The Leads page: the searchable, filterable grid.</summary>
public sealed partial class LeadListViewModel : ObservableObject
{
    public const string AllStatuses = "All statuses";
    public const string AllPriorities = "All priorities";

    // Quick filters shown as chips above the grid.
    public const string ShowAll = "All";
    public const string ShowNotContacted = "NotContacted";
    public const string ShowContacted = "Contacted";
    public const string ShowEngaged = "Engaged";
    public const string ShowFollowUpDue = "FollowUpDue";

    private readonly LeadStore _store;
    private readonly IDialogService _dialogs;
    private readonly Notice _notice;
    private readonly Outreach _outreach;

    public LeadListViewModel(LeadStore store, IDialogService dialogs, Notice notice, Outreach outreach)
    {
        _store = store;
        _dialogs = dialogs;
        _notice = notice;
        _outreach = outreach;
        var view = new ListCollectionView(store.Leads) { Filter = item => IsShown((Lead)item) };
        view.SortDescriptions.Add(new SortDescription(nameof(Lead.LeadCode), ListSortDirection.Ascending));
        LeadsView = view;
        store.Changed += (_, _) => OnPropertyChanged(string.Empty);
    }

    public ICollectionView LeadsView { get; }

    public string[] StatusFilterOptions { get; } = [AllStatuses, .. LeadOptions.Statuses];

    public string[] PriorityFilterOptions { get; } = [AllPriorities, .. LeadOptions.Priorities];

    [ObservableProperty]
    private Lead? _selectedLead;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusFilter = AllStatuses;

    [ObservableProperty]
    private string _priorityFilter = AllPriorities;

    [ObservableProperty]
    private string _quickFilter = ShowAll;

    public int AllCount => _store.Leads.Count;

    public int NotContactedCount => _store.Leads.Count(l => !l.IsContacted);

    public int ContactedCount => _store.Leads.Count(l => l.IsContacted);

    public int EngagedCount => _store.Leads.Count(l => LeadOptions.IsEngaged(l.Status));

    public int FollowUpDueCount => _store.Leads.Count(l => l.IsFollowUpDue);

    public int ShownCount => LeadsView.Cast<object>().Count();

    public bool HasLeads => _store.Leads.Count > 0;

    public bool HasNoMatches => HasLeads && ShownCount == 0;

    public bool HasActiveFilters =>
        SearchText.Length > 0 || StatusFilter != AllStatuses || PriorityFilter != AllPriorities || QuickFilter != ShowAll;

    public string ShownSummary => HasActiveFilters ? $"Showing {ShownCount} of {AllCount}" : $"{AllCount} leads";

    /// <summary>The rows ticked in the grid (kept in step by the view, since DataGrid.SelectedItems can't be bound).</summary>
    public IReadOnlyList<Lead> SelectedLeads { get; private set; } = [];

    public int SelectedCount => SelectedLeads.Count;

    public bool HasSelection => SelectedLeads.Count > 0;

    public string SelectionSummary => SelectedCount == 1 ? "1 selected" : $"{SelectedCount} selected";

    public IReadOnlyList<string> Statuses => LeadOptions.Statuses;

    public void UpdateSelection(IReadOnlyList<Lead> selected)
    {
        SelectedLeads = selected;
        OnPropertyChanged(nameof(SelectedLeads));
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));
    }

    partial void OnSearchTextChanged(string value) => Refresh();

    partial void OnStatusFilterChanged(string value) => Refresh();

    partial void OnPriorityFilterChanged(string value) => Refresh();

    partial void OnQuickFilterChanged(string value) => Refresh();

    /// <summary>Resets the filters to show one slice, e.g. when a dashboard tile is clicked.</summary>
    public void ShowOnly(string quickFilter = ShowAll, string status = AllStatuses)
    {
        SearchText = "";
        PriorityFilter = AllPriorities;
        StatusFilter = status;
        QuickFilter = quickFilter;
    }

    public void Select(Lead lead)
    {
        if (!LeadsView.Cast<Lead>().Contains(lead))
        {
            ShowOnly();
        }
        SelectedLead = lead;
    }

    [RelayCommand]
    private void ClearFilters() => ShowOnly();

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void ViewLead(Lead? lead)
    {
        lead ??= SelectedLead;
        if (lead is null)
        {
            return;
        }
        var detail = new LeadDetailViewModel(_store, _dialogs, _outreach, lead, LeadsView.Cast<Lead>());
        _dialogs.ShowLead(detail);

        // Re-apply the filters now that the edits are done, and land on the last lead viewed.
        Refresh();
        SelectedLead = _store.Leads.Contains(detail.Lead) ? detail.Lead : null;
    }

    /// <summary>Emails the selected leads, or just <paramref name="lead"/> when it isn't part of the selection.</summary>
    [RelayCommand]
    private void Message(Lead? lead)
    {
        var targets = TargetsFor(lead);
        var sent = _outreach.Compose(targets);
        if (sent > 0)
        {
            _notice.Show(sent == 1 ? "Email sent." : $"Sent {sent} emails.", seconds: 6);
        }
    }

    [RelayCommand]
    private void CopyEmail(Lead? lead)
    {
        if ((lead ?? SelectedLead) is { } target)
        {
            _notice.Show(LeadActions.CopyEmail(target));
        }
    }

    [RelayCommand]
    private void OpenWebsite(Lead? lead)
    {
        if ((lead ?? SelectedLead) is { } target)
        {
            _notice.Show(LeadActions.OpenWebsite(target));
        }
    }

    [RelayCommand]
    private void MarkContactedToday(Lead? lead)
    {
        var targets = TargetsFor(lead);
        foreach (var target in targets)
        {
            _store.MarkContactedToday(target);
        }
        if (targets.Count > 0)
        {
            _notice.Show(targets.Count == 1 ? $"{targets[0].DisplayName} marked as contacted today." : $"{targets.Count} leads marked as contacted today.");
        }
    }

    [RelayCommand]
    private void SetStatus(string status)
    {
        foreach (var target in SelectedLeads)
        {
            target.Status = status;
        }
        _notice.Show($"{Leads(SelectedCount)} set to {status}.");
    }

    /// <summary>Sets a follow-up this many days out for the selected leads; "0" clears it.</summary>
    [RelayCommand]
    private void SetFollowUp(string days)
    {
        var count = int.Parse(days);
        foreach (var target in SelectedLeads)
        {
            target.FollowUpOn = count == 0 ? null : DateTime.Today.AddDays(count);
        }
        _notice.Show(count == 0
            ? $"Cleared the follow-up for {Leads(SelectedCount)}."
            : $"{Leads(SelectedCount)} to follow up on {DateTime.Today.AddDays(count):dddd, MMM d}.");
    }

    [RelayCommand]
    private void DeleteLead(Lead? lead)
    {
        var targets = TargetsFor(lead);
        var question = targets.Count == 1
            ? $"Delete {targets[0].DisplayName}?\n\nThis removes the lead and its notes for good."
            : $"Delete {targets.Count} leads?\n\nThis removes them and their notes for good.";
        if (targets.Count == 0 || !_dialogs.Confirm(question, targets.Count == 1 ? "Delete lead" : "Delete leads"))
        {
            return;
        }
        foreach (var target in targets)
        {
            _store.Delete(target);
        }
        _notice.Show(targets.Count == 1 ? $"Deleted {targets[0].DisplayName}." : $"Deleted {targets.Count} leads.");
    }

    // A row's menu acts on the whole selection when that row is part of it, otherwise on the row alone.
    private IReadOnlyList<Lead> TargetsFor(Lead? lead)
    {
        if (lead is null)
        {
            return SelectedLeads.Count > 0 ? SelectedLeads.ToList() : SelectedLead is { } single ? [single] : [];
        }
        return SelectedLeads.Count > 1 && SelectedLeads.Contains(lead) ? SelectedLeads.ToList() : [lead];
    }

    private static string Leads(int count) => count == 1 ? "1 lead" : $"{count} leads";

    private void Refresh()
    {
        LeadsView.Refresh();
        OnPropertyChanged(string.Empty);
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
        var quickMatch = QuickFilter switch
        {
            ShowNotContacted => !lead.IsContacted,
            ShowContacted => lead.IsContacted,
            ShowEngaged => LeadOptions.IsEngaged(lead.Status),
            ShowFollowUpDue => lead.IsFollowUpDue,
            _ => true,
        };
        if (!quickMatch)
        {
            return false;
        }

        // Every word typed has to appear somewhere in the lead.
        var searchable = new[] { lead.LeadCode, lead.ContactName, lead.Practice, lead.Email, lead.Location, lead.Subjects, lead.Notes, lead.Caveats };
        return SearchText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(term => searchable.Any(field => field?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
    }
}
