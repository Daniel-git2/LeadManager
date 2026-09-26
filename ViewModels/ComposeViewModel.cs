using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LeadManager.Data;
using LeadManager.Models;
using LeadManager.Services;
using LeadManager.Services.Email;
using MimeKit;

namespace LeadManager.ViewModels;

public enum ComposeStage
{
    Writing,
    Sending,
    Finished,
}

/// <summary>One lead the message will go to, and how sending to them went.</summary>
public sealed partial class RecipientRow(Lead lead) : ObservableObject
{
    public const string Waiting = "Waiting";
    public const string Sending = "Sending…";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
    public const string NotSent = "Not sent";

    public Lead Lead { get; } = lead;

    public string Display => $"{Lead.DisplayName} <{Lead.Email.Trim()}>";

    /// <summary>Already got this template (or this subject) before.</summary>
    [ObservableProperty]
    private bool _alreadySent;

    [ObservableProperty]
    private bool _isIncluded = true;

    [ObservableProperty]
    private string _state = Waiting;

    /// <summary>Why it failed, or a warning about a send that worked.</summary>
    [ObservableProperty]
    private string? _detail;
}

public sealed record SkippedLead(Lead Lead, string Reason)
{
    public string Display => $"{Lead.DisplayName}: {Reason}";
}

/// <summary>
/// Writes one message and sends each lead their own personalised copy, pausing between emails.
/// Every attempt is logged, and leads that were emailed get marked as contacted.
/// </summary>
public sealed partial class ComposeViewModel : ObservableObject
{
    private readonly LeadStore _store;
    private readonly Outreach _outreach;
    private readonly IDialogService _dialogs;
    private readonly List<SentEmail> _previousSends;
    private CancellationTokenSource? _sending;
    private int _targetCount;

    public ComposeViewModel(LeadStore store, Outreach outreach, IDialogService dialogs, IReadOnlyList<Lead> leads)
    {
        _store = store;
        _outreach = outreach;
        _dialogs = dialogs;

        var skipped = new List<SkippedLead>();
        foreach (var lead in leads.Distinct())
        {
            if (SkipReason(lead) is { } reason)
            {
                skipped.Add(new SkippedLead(lead, reason));
            }
            else
            {
                Recipients.Add(new RecipientRow(lead));
            }
        }
        Skipped = skipped;
        _previousSends = outreach.Emails.GetSuccessfulSends(Recipients.Select(r => r.Lead.Id));

        Templates = new ObservableCollection<EmailTemplate>(outreach.Emails.GetTemplates());
        _previewRecipient = Recipients.FirstOrDefault();
        // With a single template there's nothing to choose.
        SelectedTemplate = Templates.Count == 1 ? Templates[0] : null;
        RefreshRecipients();
    }

    /// <summary>Asks the window to close.</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<RecipientRow> Recipients { get; } = [];

    public IReadOnlyList<SkippedLead> Skipped { get; }

    public ObservableCollection<EmailTemplate> Templates { get; }

    public IReadOnlyList<EmailRenderer.Placeholder> Placeholders => EmailRenderer.Placeholders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateChanged))]
    [NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand), nameof(DeleteTemplateCommand))]
    private EmailTemplate? _selectedTemplate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateChanged))]
    [NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand), nameof(SendCommand))]
    private string _subject = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateChanged))]
    [NotifyCanExecuteChangedFor(nameof(SaveTemplateCommand), nameof(SendCommand))]
    private string _body = "";

    [ObservableProperty]
    private RecipientRow? _previewRecipient;

    [ObservableProperty]
    private bool _skipAlreadySent = true;

    [ObservableProperty]
    private bool _markContacted = true;

    [ObservableProperty]
    private bool _setFollowUp = true;

    [ObservableProperty]
    private int _followUpDays = 7;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWriting), nameof(IsSending), nameof(IsFinished))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand))]
    private ComposeStage _stage = ComposeStage.Writing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private int _sentCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    private int _failedCount;

    [ObservableProperty]
    private string _progressText = "";

    /// <summary>A problem worth reading, e.g. why sending stopped.</summary>
    [ObservableProperty]
    private string? _problem;

    public bool IsWriting => Stage == ComposeStage.Writing;

    public bool IsSending => Stage == ComposeStage.Sending;

    public bool IsFinished => Stage == ComposeStage.Finished;

    public bool IsTemplateChanged =>
        SelectedTemplate is not null && (Subject != SelectedTemplate.Subject || Body != SelectedTemplate.Body);

    public int SendCount => Recipients.Count(r => r.IsIncluded);

    public int AlreadySentCount => Recipients.Count(r => r.AlreadySent);

    public bool HasAlreadySent => AlreadySentCount > 0;

    public bool HasSkipped => Skipped.Count > 0;

    public string Title => Recipients.Count == 1 ? $"Message {Recipients[0].Lead.DisplayName}" : $"Message {Recipients.Count} leads";

    public string ToSummary
    {
        get
        {
            var names = Recipients.Select(r => r.Lead.DisplayName).ToList();
            return names.Count switch
            {
                0 => "Nobody. None of the selected leads can be emailed.",
                1 => Recipients[0].Display,
                2 => $"{names[0]} and {names[1]}",
                3 => $"{names[0]}, {names[1]} and {names[2]}",
                _ => $"{names[0]}, {names[1]} and {names.Count - 2} more",
            };
        }
    }

    public string AllRecipientsText => string.Join(Environment.NewLine, Recipients.Select(r => r.Display));

    public string SkippedSummary => Skipped.Count == 1 ? "1 lead skipped" : $"{Skipped.Count} leads skipped";

    public string SkippedDetails => string.Join(Environment.NewLine, Skipped.Select(s => s.Display));

    public string AlreadySentText => AlreadySentCount == 1
        ? "Skip 1 lead who already got this email"
        : $"Skip {AlreadySentCount} leads who already got this email";

    public string SendButtonText => (SendCount == 1 ? "Send email" : $"Send {SendCount} emails") + (IsTestEnvironment ? " (test)" : "");

    public bool IsTestEnvironment => _outreach.Environment.IsTest;

    /// <summary>Where emails really go in the test environment; null in production.</summary>
    public string? TestNotice => _outreach.TestModeNotice;

    public string FromText
    {
        get
        {
            var settings = _outreach.CurrentSettings;
            return string.IsNullOrWhiteSpace(settings.FromName) ? settings.FromAddress : $"{settings.FromName} <{settings.FromAddress}>";
        }
    }

    public string PreviewPosition => PreviewRecipient is null ? "" : $"{Recipients.IndexOf(PreviewRecipient) + 1} of {Recipients.Count}";

    public string PreviewTo => PreviewRecipient?.Display ?? "";

    public string PreviewSubject { get; private set; } = "";

    public string PreviewBody { get; private set; } = "";

    public IReadOnlyList<string> PreviewNotes { get; private set; } = [];

    public int ProgressMaximum => Math.Max(1, _targetCount);

    public int ProgressValue => SentCount + FailedCount;

    partial void OnSelectedTemplateChanged(EmailTemplate? value)
    {
        if (value is not null)
        {
            Subject = value.Subject;
            Body = value.Body;
        }
        RefreshRecipients();
    }

    partial void OnSubjectChanged(string value) => RefreshPreview();

    partial void OnBodyChanged(string value) => RefreshPreview();

    partial void OnPreviewRecipientChanged(RecipientRow? value) => RefreshPreview();

    partial void OnSkipAlreadySentChanged(bool value) => RefreshRecipients();

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var targets = Recipients.Where(r => r.IsIncluded).ToList();
        var settings = _outreach.CurrentSettings;
        var pauseNote = targets.Count > 1 && settings.PauseSeconds > 0
            ? $"\n\nThere's a {settings.PauseSeconds}-second pause between emails, so this takes about {Duration(settings.PauseSeconds * (targets.Count - 1))}."
            : "";
        var who = TestNotice ?? $"Each lead gets their own copy from {FromText}.";
        if (!_dialogs.Confirm($"Send {(targets.Count == 1 ? "this email" : $"{targets.Count} emails")} now?\n\n{who}{pauseNote}", "Send email"))
        {
            return;
        }
        // Chosen now rather than when the window opened, in case the settings changed meanwhile.
        var sender = _outreach.SenderFor(_outreach.Settings.Current);

        Stage = ComposeStage.Sending;
        Problem = null;
        foreach (var row in Recipients.Where(r => !r.IsIncluded))
        {
            row.State = RecipientRow.NotSent;
            row.Detail = "Already got this email";
        }
        _targetCount = targets.Count;
        OnPropertyChanged(nameof(ProgressMaximum));

        _sending = new CancellationTokenSource();
        var token = _sending.Token;
        IEmailSession? session = null;
        try
        {
            ProgressText = "Connecting…";
            session = await sender.ConnectAsync(settings, token);
            for (var i = 0; i < targets.Count; i++)
            {
                var row = targets[i];
                if (i > 0 && settings.PauseSeconds > 0)
                {
                    ProgressText = $"Sent {SentCount} of {targets.Count}. Next email in {settings.PauseSeconds} seconds…";
                    await Task.Delay(TimeSpan.FromSeconds(settings.PauseSeconds), token);
                }
                row.State = RecipientRow.Sending;
                ProgressText = $"Sending to {row.Lead.DisplayName} ({i + 1} of {targets.Count})…";

                var email = EmailRenderer.Render(Subject, Body, row.Lead, settings);
                var message = EmailRenderer.BuildMessage(email, row.Lead, settings);
                var outcome = await session.SendAsync(message, token);
                Record(row, email, message, outcome);
                if (outcome.StopBatch)
                {
                    Problem = outcome.Error;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            Problem = "Stopped. Leads that weren't emailed yet are unchanged.";
        }
        catch (EmailException ex)
        {
            Problem = ex.Message;
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }
            _sending.Dispose();
            _sending = null;

            foreach (var row in targets.Where(r => r.State is RecipientRow.Waiting or RecipientRow.Sending))
            {
                row.State = RecipientRow.NotSent;
            }
            _store.SavePending();
            Stage = ComposeStage.Finished;
            ProgressText = (SentCount, FailedCount) switch
            {
                (0, 0) => "Nothing was sent.",
                (_, 0) => SentCount == 1 ? "Sent 1 email." : $"Sent all {SentCount} emails.",
                _ => $"Sent {SentCount}, {FailedCount} failed.",
            };
        }
    }

    private bool CanSend() => IsWriting && SendCount > 0 && Subject.Trim().Length > 0 && Body.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(IsSending))]
    private void Stop() => _sending?.Cancel();

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void PreviousPreview() => MovePreview(-1);

    [RelayCommand]
    private void NextPreview() => MovePreview(1);

    [RelayCommand(CanExecute = nameof(IsTemplateChanged))]
    private void SaveTemplate()
    {
        var template = SelectedTemplate!;
        template.Subject = Subject;
        template.Body = Body;
        _outreach.Emails.UpdateTemplate(template);
        OnPropertyChanged(nameof(IsTemplateChanged));
        SaveTemplateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SaveTemplateAs()
    {
        var suggested = SelectedTemplate?.Name ?? (Subject.Trim().Length > 0 ? Subject.Trim() : "New template");
        if (_dialogs.PromptText("Save as template", "Name this template:", suggested) is not { } name || name.Trim().Length == 0)
        {
            return;
        }
        AddTemplate(new EmailTemplate { Name = _outreach.Emails.UniqueTemplateName(name.Trim()), Subject = Subject, Body = Body });
    }

    [RelayCommand(CanExecute = nameof(HasTemplate))]
    private void DeleteTemplate()
    {
        var template = SelectedTemplate!;
        if (!_dialogs.Confirm($"Delete the template “{template.Name}”?\n\nThe text stays in this message; emails already sent aren't affected.", "Delete template"))
        {
            return;
        }
        _outreach.Emails.DeleteTemplate(template.Id);
        Templates.Remove(template);
        SelectedTemplate = null;
    }

    private bool HasTemplate() => SelectedTemplate is not null;

    [RelayCommand]
    private void ImportTemplate()
    {
        if (_dialogs.PickMsgToOpen() is not { } path)
        {
            return;
        }
        try
        {
            var (name, subject, body) = MsgTemplateReader.Read(path);
            AddTemplate(new EmailTemplate { Name = _outreach.Emails.UniqueTemplateName(name), Subject = subject, Body = body });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _dialogs.ShowError($"Couldn't read {Path.GetFileName(path)} as an Outlook message.\n\n{ex.Message}", "Import template");
        }
    }

    [RelayCommand]
    private void ChangeSettings()
    {
        if (_outreach.OpenSettings())
        {
            OnPropertyChanged(nameof(FromText));
            OnPropertyChanged(nameof(TestNotice));
            RefreshPreview();
        }
    }

    private void AddTemplate(EmailTemplate template)
    {
        _outreach.Emails.InsertTemplate(template);
        var index = Templates.TakeWhile(t => string.Compare(t.Name, template.Name, StringComparison.OrdinalIgnoreCase) < 0).Count();
        Templates.Insert(index, template);
        SelectedTemplate = template;
    }

    private void Record(RecipientRow row, RenderedEmail email, MimeMessage message, SendOutcome outcome)
    {
        _outreach.Emails.LogSent(new SentEmail
        {
            LeadId = row.Lead.Id,
            TemplateId = SelectedTemplate?.Id,
            ToAddress = row.Lead.Email.Trim(),
            Subject = email.Subject,
            Body = email.TextBody,
            Status = outcome.Sent ? SentEmail.SentStatus : SentEmail.FailedStatus,
            Error = outcome.Error ?? outcome.Warning,
            MessageId = outcome.Sent ? message.MessageId : null,
            SentAt = AppDatabase.Now(),
        });

        if (!outcome.Sent)
        {
            FailedCount++;
            row.State = RecipientRow.Failed;
            row.Detail = outcome.Error;
            return;
        }

        SentCount++;
        row.State = RecipientRow.Sent;
        row.Detail = outcome.Warning;
        if (MarkContacted)
        {
            _store.MarkContactedToday(row.Lead);
            _store.AddNote(row.Lead, $"Emailed “{email.Subject}”");
        }
        if (SetFollowUp && FollowUpDays > 0)
        {
            row.Lead.FollowUpOn = DateTime.Today.AddDays(FollowUpDays);
        }
    }

    private void RefreshRecipients()
    {
        var settings = _outreach.CurrentSettings;
        foreach (var row in Recipients)
        {
            // With a template, "already sent" means that template; without one, the same subject line.
            var subject = SelectedTemplate is null ? EmailRenderer.Render(Subject, "", row.Lead, settings).Subject : null;
            row.AlreadySent = _previousSends.Any(s => s.LeadId == row.Lead.Id
                && (SelectedTemplate is not null ? s.TemplateId == SelectedTemplate.Id : subject!.Length > 0 && s.Subject == subject));
            row.IsIncluded = !(SkipAlreadySent && row.AlreadySent);
        }
        OnPropertyChanged(nameof(SendCount));
        OnPropertyChanged(nameof(AlreadySentCount));
        OnPropertyChanged(nameof(HasAlreadySent));
        OnPropertyChanged(nameof(AlreadySentText));
        OnPropertyChanged(nameof(SendButtonText));
        SendCommand.NotifyCanExecuteChanged();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (PreviewRecipient is { } row)
        {
            var email = EmailRenderer.Render(Subject, Body, row.Lead, _outreach.CurrentSettings);
            PreviewSubject = email.Subject;
            PreviewBody = email.TextBody;
            PreviewNotes = email.Notes;
        }
        OnPropertyChanged(nameof(PreviewSubject));
        OnPropertyChanged(nameof(PreviewBody));
        OnPropertyChanged(nameof(PreviewNotes));
        OnPropertyChanged(nameof(PreviewTo));
        OnPropertyChanged(nameof(PreviewPosition));
    }

    private void MovePreview(int step)
    {
        if (Recipients.Count == 0)
        {
            return;
        }
        var index = PreviewRecipient is null ? 0 : Recipients.IndexOf(PreviewRecipient);
        PreviewRecipient = Recipients[(index + step + Recipients.Count) % Recipients.Count];
    }

    private static string? SkipReason(Lead lead)
    {
        if (string.IsNullOrWhiteSpace(lead.Email))
        {
            return "no email address";
        }
        if (!EmailRenderer.IsSendableAddress(lead.Email))
        {
            return "the email address doesn't look valid";
        }
        return lead.Status.Trim().ToLowerInvariant() switch
        {
            "do not contact" => "marked Do not contact",
            "bounced" => "their email bounced before",
            "not interested" => "said they're not interested",
            _ => null,
        };
    }

    private static string Duration(int seconds) => seconds < 90 ? $"{seconds} seconds" : $"{Math.Round(seconds / 60.0)} minutes";
}
