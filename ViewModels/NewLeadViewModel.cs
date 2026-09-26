using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Models;
using LeadManager.Services;

namespace LeadManager.ViewModels;

/// <summary>The "New lead" form: the essentials, with a warning if the lead is already in the list.</summary>
public sealed partial class NewLeadViewModel : ObservableObject
{
    private readonly LeadStore _store;

    public NewLeadViewModel(LeadStore store)
    {
        _store = store;
        _leadCode = store.SuggestNextLeadCode() ?? "";
    }

    /// <summary>Raised once the lead has been added, so the window can close.</summary>
    public event EventHandler? Completed;

    public Lead? Created { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _contactName = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _practice = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmailWarning))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _email = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeadCodeError))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    private string _leadCode;

    [ObservableProperty]
    private string _location = "";

    [ObservableProperty]
    private string _subjects = "";

    [ObservableProperty]
    private string _fitPriority = "Medium";

    [ObservableProperty]
    private string _website = "";

    [ObservableProperty]
    private string _notes = "";

    public string? EmailWarning
    {
        get
        {
            var email = Email.Trim();
            if (email.Length == 0)
            {
                return null;
            }
            if (!email.Contains('@') || email.Any(char.IsWhiteSpace))
            {
                return "That doesn't look like an email address.";
            }
            return _store.FindByEmail(email) is { } existing
                ? $"Already in your list: {existing.DisplayName}{(existing.LeadCode is { } code ? $" ({code})" : "")}. You can still add it."
                : null;
        }
    }

    public string? LeadCodeError =>
        LeadCode.Trim().Length > 0 && _store.FindByLeadCode(LeadCode) is { } existing
            ? $"{existing.DisplayName} already uses this ID."
            : null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        var lead = new Lead
        {
            LeadCode = LeadCode.Trim(),
            ContactName = ContactName.Trim(),
            Practice = Practice.Trim(),
            Email = Email.Trim(),
            Location = Location.Trim(),
            Subjects = Subjects.Trim(),
            FitPriority = FitPriority.Trim(),
            SourceUrls = Website.Trim(),
            Notes = Notes.Trim(),
        };
        _store.Add(lead);
        Created = lead;
        Completed?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAdd() =>
        LeadCodeError is null
        && new[] { ContactName, Practice, Email }.Any(s => !string.IsNullOrWhiteSpace(s));
}
