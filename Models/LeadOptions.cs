namespace LeadManager.Models;

/// <summary>
/// Pick-lists shown in the app. The database stores these as free text, so imported values
/// that aren't in a list are kept as-is.
/// </summary>
public static class LeadOptions
{
    public const string NotContacted = "Not contacted";
    public const string Contacted = "Contacted";
    public const string FollowedUp = "Followed up";
    public const string UnknownInterest = "Unknown";
    public const string NotTested = "Not tested";

    public static string[] Statuses { get; } =
    [
        NotContacted, Contacted, FollowedUp, "Replied", "Meeting booked", "Pilot", "Customer",
        "Not interested", "Bounced", "Do not contact",
    ];

    public static string[] Priorities { get; } = ["High", "Medium", "Low"];

    public static string[] InterestLevels { get; } = [UnknownInterest, "Interested", "Maybe", "Not interested"];

    public static string[] DeliverabilityOptions { get; } = [NotTested, "Valid", "Risky", "Bounced"];

    public static string[] InboxTypes { get; } = ["Published tutor contact", "Practice/shared inquiry inbox"];

    // Statuses that can only happen after you've reached out, so they imply a contacted date.
    private static readonly HashSet<string> ContactedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        Contacted, FollowedUp, "Replied", "Meeting booked", "Pilot", "Customer", "Not interested", "Bounced",
    };

    // Statuses that mean the lead has replied or gone further.
    private static readonly HashSet<string> EngagedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Replied", "Meeting booked", "Pilot", "Customer",
    };

    // Statuses where there's nothing left to chase.
    private static readonly HashSet<string> ClosedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Customer", "Not interested", "Bounced", "Do not contact",
    };

    public static bool ImpliesContacted(string status) => ContactedStatuses.Contains(status.Trim());

    public static bool IsEngaged(string status) => EngagedStatuses.Contains(status.Trim());

    public static bool IsClosed(string status) => ClosedStatuses.Contains(status.Trim());

    /// <summary>Groups statuses into the few tones the status badges are colored by.</summary>
    public static string ToneOf(string status) => status.Trim().ToLowerInvariant() switch
    {
        "contacted" or "followed up" => "Active",
        "replied" or "meeting booked" or "pilot" => "Engaged",
        "customer" => "Won",
        "not interested" or "do not contact" => "Closed",
        "bounced" => "Problem",
        _ => "Neutral",
    };

    public static bool IsNotContacted(string status) => status.Trim().Equals(NotContacted, StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStatus(string value) => MatchOption(value, Statuses);

    public static string NormalizePriority(string value) => MatchOption(value, Priorities);

    public static string NormalizeDeliverability(string value) => MatchOption(value, DeliverabilityOptions);

    public static string NormalizeInterest(string value) =>
        value.Contains("unconfirmed", StringComparison.OrdinalIgnoreCase) ? UnknownInterest : MatchOption(value, InterestLevels);

    private static string MatchOption(string value, string[] options)
    {
        var trimmed = value.Trim();
        return options.FirstOrDefault(o => o.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }
}
