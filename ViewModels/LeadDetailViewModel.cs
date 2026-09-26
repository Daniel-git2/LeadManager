using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Models;
using LeadManager.Services;

namespace LeadManager.ViewModels;

/// <summary>
/// The lead window. It steps through the list it was opened from (Previous / Next), so you can work
/// down a list of leads without going back to it.
/// </summary>
public sealed partial class LeadDetailViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;

    // A snapshot of the list at the time the window opened, so editing a lead doesn't reshuffle it.
    private readonly List<Lead> _sequence;

    public LeadDetailViewModel(LeadStore store, IDialogService dialogs, Lead lead, IEnumerable<Lead> sequence)
    {
        Store = store;
        _dialogs = dialogs;
        _sequence = sequence.ToList();
        if (!_sequence.Contains(lead))
        {
            _sequence = [lead];
        }
        _lead = lead;
    }

    /// <summary>Asks the window to close, e.g. after the last lead in it was deleted.</summary>
    public event EventHandler? CloseRequested;

    public LeadStore Store { get; }

    public Notice Notice { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Position))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
    private Lead _lead;

    public bool HasSequence => _sequence.Count > 1;

    public string Position => $"{_sequence.IndexOf(Lead) + 1} of {_sequence.Count}";

    partial void OnLeadChanged(Lead value) => Notice.Dismiss();

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous() => Lead = _sequence[_sequence.IndexOf(Lead) - 1];

    private bool CanGoPrevious() => _sequence.IndexOf(Lead) > 0;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next() => Lead = _sequence[_sequence.IndexOf(Lead) + 1];

    private bool CanGoNext()
    {
        var index = _sequence.IndexOf(Lead);
        return index >= 0 && index < _sequence.Count - 1;
    }

    [RelayCommand]
    private void SendEmail() => Notice.Show(LeadActions.SendEmail(Lead));

    [RelayCommand]
    private void CopyEmail() => Notice.Show(LeadActions.CopyEmail(Lead));

    [RelayCommand]
    private void OpenWebsite() => Notice.Show(LeadActions.OpenWebsite(Lead));

    [RelayCommand]
    private void ContactedToday()
    {
        Store.MarkContactedToday(Lead);
        Notice.Show($"Marked as contacted today. Status: {Lead.Status}.");
    }

    [RelayCommand]
    private void FollowUpIn(string? days)
    {
        Lead.FollowUpOn = DateTime.Today.AddDays(int.Parse(days ?? "7"));
        Notice.Show($"Follow up on {Lead.FollowUpOn:dddd, MMM d}.");
    }

    [RelayCommand]
    private void ClearFollowUp() => Lead.FollowUpOn = null;

    [RelayCommand]
    private void Delete()
    {
        var lead = Lead;
        if (!_dialogs.Confirm($"Delete {lead.DisplayName}?\n\nThis removes the lead and its notes for good.", "Delete lead"))
        {
            return;
        }
        var index = _sequence.IndexOf(lead);
        Store.Delete(lead);
        _sequence.Remove(lead);
        if (_sequence.Count == 0)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return;
        }
        Lead = _sequence[Math.Min(index, _sequence.Count - 1)];
        OnPropertyChanged(nameof(HasSequence));
        Notice.Show($"Deleted {lead.DisplayName}.");
    }
}
