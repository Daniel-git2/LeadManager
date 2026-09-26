using CommunityToolkit.Mvvm.ComponentModel;

namespace LeadManager.Models;

/// <summary>
/// One row of the Leads table. Settable properties are the table's columns (names match so Dapper can map
/// them); get-only properties are computed for display.
/// </summary>
public partial class Lead : ObservableObject
{
    public long Id { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string? _leadCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PriorityRank))]
    private string _fitPriority = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(SecondaryName), nameof(Subtitle))]
    private string _contactName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(SecondaryName), nameof(Subtitle))]
    private string _practice = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _email = "";

    [ObservableProperty]
    private string _inboxType = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    private string _location = "";

    [ObservableProperty]
    private string _subjects = "";

    [ObservableProperty]
    private string _fitEvidence = "";

    [ObservableProperty]
    private string _outreachAngle = "";

    [ObservableProperty]
    private string _sourceUrls = "";

    [ObservableProperty]
    private string _sourceVerification = "";

    [ObservableProperty]
    private string _caveats = "";

    [ObservableProperty]
    private DateTime? _researchDate;

    [ObservableProperty]
    private string _emailDeliverability = LeadOptions.NotTested;

    [ObservableProperty]
    private string _interest = LeadOptions.UnknownInterest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTone))]
    private string _status = LeadOptions.NotContacted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContacted))]
    private DateTime? _contactedOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFollowUpDue))]
    private DateTime? _followUpOn;

    [ObservableProperty]
    private string _notes = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Origin))]
    private string _importedFrom = "";

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private DateTime _updatedAt;

    /// <summary>Checkbox-friendly view of <see cref="ContactedOn"/>; ticking it stamps today's date.</summary>
    public bool IsContacted
    {
        get => ContactedOn is not null;
        set => ContactedOn = value ? ContactedOn ?? DateTime.Today : null;
    }

    public bool IsFollowUpDue => FollowUpOn is { } date && date.Date <= DateTime.Today;

    /// <summary>Sort key so High sorts above Medium above Low.</summary>
    public int PriorityRank
    {
        get
        {
            var index = Array.FindIndex(LeadOptions.Priorities, p => p.Equals(FitPriority, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? LeadOptions.Priorities.Length : index;
        }
    }

    public string StatusTone => LeadOptions.ToneOf(Status);

    public string DisplayName =>
        new[] { ContactName, Practice, Email }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s)) ?? "New lead";

    /// <summary>The practice, shown under the name when the name is what <see cref="DisplayName"/> uses.</summary>
    public string SecondaryName => string.IsNullOrWhiteSpace(ContactName) ? "" : Practice;

    /// <summary>"Titus Tutors · Northwestern CT · ST-002" for headers.</summary>
    public string Subtitle => string.Join("  ·  ", new[] { SecondaryName, Location, LeadCode ?? "" }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string Origin => ImportedFrom.Length > 0 ? $"Imported from {ImportedFrom}" : "Added by hand";
}
