using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Models;
using LeadManager.Services;

namespace LeadManager.ViewModels;

public sealed record PipelineRow(string Status, int Count, int Total, int Largest)
{
    public string Tooltip => $"{Status}: {Count} {(Count == 1 ? "lead" : "leads")} ({(Total == 0 ? 0 : Count * 100.0 / Total):0}%)";
}

/// <summary>The home page: what needs doing today, and how outreach is going overall.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int ListLength = 5;

    private readonly LeadStore _store;
    private readonly IDialogService _dialogs;
    private readonly Outreach _outreach;
    private readonly Action<string, string> _showLeads;
    private List<Lead> _allFollowUps = [];
    private List<Lead> _allUpNext = [];

    /// <param name="showLeads">Switches to the Leads page with a quick filter and status filter applied.</param>
    public DashboardViewModel(LeadStore store, IDialogService dialogs, Outreach outreach, Action<string, string> showLeads)
    {
        _store = store;
        _dialogs = dialogs;
        _outreach = outreach;
        _showLeads = showLeads;
        store.Changed += (_, _) => Refresh();
        Refresh();
    }

    public string Greeting => DateTime.Now.Hour switch
    {
        < 12 => "Good morning",
        < 17 => "Good afternoon",
        _ => "Good evening",
    };

    public string TodayText { get; private set; } = "";

    public bool HasLeads { get; private set; }

    public int TotalCount { get; private set; }

    public int NotContactedCount { get; private set; }

    public int ContactedCount { get; private set; }

    public int EngagedCount { get; private set; }

    public int FollowUpsDueCount { get; private set; }

    public bool HasFollowUps => FollowUpsDueCount > 0;

    public bool HasUpNext => UpNext.Count > 0;

    public int ContactedThisWeekCount { get; private set; }

    public string ContactedCaption { get; private set; } = "";

    public string EngagedCaption { get; private set; } = "";

    public IReadOnlyList<Lead> FollowUps { get; private set; } = [];

    public IReadOnlyList<Lead> UpNext { get; private set; } = [];

    public string MoreFollowUpsText { get; private set; } = "";

    public string MoreUpNextText { get; private set; } = "";

    public IReadOnlyList<PipelineRow> Pipeline { get; private set; } = [];

    public void Refresh()
    {
        var leads = _store.Leads;
        var today = DateTime.Today;

        HasLeads = leads.Count > 0;
        TotalCount = leads.Count;
        ContactedCount = leads.Count(l => l.IsContacted);
        NotContactedCount = TotalCount - ContactedCount;
        EngagedCount = leads.Count(l => LeadOptions.IsEngaged(l.Status));
        ContactedThisWeekCount = leads.Count(l => l.ContactedOn is { } d && d.Date > today.AddDays(-7));
        ContactedCaption = $"{Percent(ContactedCount, TotalCount)} of leads · {ContactedThisWeekCount} this week";
        EngagedCaption = ContactedCount == 0 ? "Replied or further" : $"{Percent(EngagedCount, ContactedCount)} of contacted";

        _allFollowUps = [.. leads.Where(l => l.IsFollowUpDue).OrderBy(l => l.FollowUpOn).ThenBy(l => l.PriorityRank)];
        FollowUpsDueCount = _allFollowUps.Count;
        FollowUps = _allFollowUps.Take(ListLength).ToList();
        MoreFollowUpsText = FollowUpsDueCount > ListLength ? $"See all {FollowUpsDueCount}" : "";

        _allUpNext = [.. leads
            .Where(l => !l.IsContacted && !LeadOptions.IsClosed(l.Status))
            .OrderBy(l => l.PriorityRank)
            .ThenBy(l => l.LeadCode ?? "￿", StringComparer.OrdinalIgnoreCase)];
        UpNext = _allUpNext.Take(ListLength).ToList();
        MoreUpNextText = _allUpNext.Count > ListLength ? $"See all {_allUpNext.Count}" : "";

        // Every standard stage, plus any custom statuses that came in from a CSV.
        var counts = leads.GroupBy(l => l.Status.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var statuses = LeadOptions.Statuses.Concat(counts.Keys.Where(k => !LeadOptions.Statuses.Contains(k, StringComparer.OrdinalIgnoreCase)));
        var largest = counts.Count == 0 ? 0 : counts.Values.Max();
        Pipeline = statuses.Select(s => new PipelineRow(s, counts.GetValueOrDefault(s), TotalCount, largest)).ToList();

        TodayText = FollowUpsDueCount switch
        {
            0 => $"{today:dddd, MMMM d} · no follow-ups due",
            1 => $"{today:dddd, MMMM d} · 1 follow-up due",
            _ => $"{today:dddd, MMMM d} · {FollowUpsDueCount} follow-ups due",
        };

        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void ViewFollowUp(Lead lead) => View(lead, _allFollowUps);

    [RelayCommand]
    private void ViewUpNext(Lead lead) => View(lead, _allUpNext);

    [RelayCommand]
    private void ShowLeads(string quickFilter) => _showLeads(quickFilter, LeadListViewModel.AllStatuses);

    [RelayCommand]
    private void ShowStatus(string status) => _showLeads(LeadListViewModel.ShowAll, status);

    private void View(Lead lead, IEnumerable<Lead> sequence)
    {
        _dialogs.ShowLead(new LeadDetailViewModel(_store, _dialogs, _outreach, lead, sequence));
        _store.SavePending();
        Refresh();
    }

    private static string Percent(int part, int whole) => whole == 0 ? "0%" : $"{part * 100.0 / whole:0}%";
}
