using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using LeadManager.Models;
using MimeKit;
using MimeKit.Utils;

namespace LeadManager.Services.Email;

/// <summary>An email filled in for one lead.</summary>
/// <param name="Notes">Things worth a look before sending, e.g. a placeholder that fell back to a default.</param>
public sealed record RenderedEmail(string Subject, string TextBody, string HtmlBody, IReadOnlyList<string> Notes);

/// <summary>
/// Turns a template into the email a lead receives. Templates are plain text with {{Placeholders}};
/// links can be pasted as-is or written as [text](https://…).
/// </summary>
public static partial class EmailRenderer
{
    public sealed record Placeholder(string Token, string Label, string Fallback);

    public static IReadOnlyList<Placeholder> Placeholders { get; } =
    [
        new("FirstName", "First name", "there"),
        new("Name", "Full name", "there"),
        new("Practice", "Practice", "your practice"),
        new("Location", "Location", "your area"),
        new("Subjects", "Subjects", "your subjects"),
        new("MyName", "My name", ""),
    ];

    private static readonly HashSet<string> Honorifics = new(StringComparer.OrdinalIgnoreCase) { "mr", "mrs", "ms", "miss", "mx", "dr", "prof" };

    public static RenderedEmail Render(string subject, string body, Lead lead, EmailSettings settings)
    {
        var notes = new List<string>();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["FirstName"] = FirstNameOf(lead.ContactName),
            ["Name"] = Blank(lead.ContactName),
            ["Practice"] = Blank(lead.Practice),
            ["Location"] = Blank(lead.Location),
            ["Subjects"] = Blank(lead.Subjects),
            ["MyName"] = Blank(settings.FromName),
        };

        string Fill(string text) => PlaceholderPattern().Replace(text, match =>
        {
            var token = match.Groups["name"].Value;
            var placeholder = Placeholders.FirstOrDefault(p => p.Token.Equals(token, StringComparison.OrdinalIgnoreCase));
            if (placeholder is null)
            {
                AddNote(notes, $"{{{{{token}}}}} isn't a placeholder this app knows, so it will be sent as written.");
                return match.Value;
            }
            if (values[placeholder.Token] is { } value)
            {
                return value;
            }
            AddNote(notes, placeholder.Fallback.Length > 0
                ? $"No {placeholder.Label.ToLowerInvariant()} for this lead, so {{{{{placeholder.Token}}}}} becomes “{placeholder.Fallback}”."
                : $"{{{{{placeholder.Token}}}}} is empty. Set your name in Email settings.");
            return placeholder.Fallback;
        });

        var filledBody = Fill(body).TrimEnd();
        if (!string.IsNullOrWhiteSpace(settings.Footer))
        {
            filledBody += "\n\n" + settings.Footer.Trim();
        }
        filledBody = filledBody.Replace("\r\n", "\n");

        return new RenderedEmail(Fill(subject).Trim(), ToPlainText(filledBody), ToHtml(filledBody), notes);
    }

    /// <summary>What to call someone in a greeting: "Jessica" from "Jessica L. Reis", but "Mrs. Abbitt" and "Brian and Taylor" whole.</summary>
    public static string? FirstNameOf(string contactName)
    {
        var name = contactName.Trim();
        if (name.Length == 0)
        {
            return null;
        }
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keepWhole = words.Length == 1
            || Honorifics.Contains(words[0].TrimEnd('.'))
            || words.Any(w => w.Equals("and", StringComparison.OrdinalIgnoreCase) || w == "&");
        return keepWhole ? name : words[0];
    }

    public static MimeMessage BuildMessage(RenderedEmail email, Lead lead, EmailSettings settings)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName.Trim(), settings.FromAddress.Trim()));
        message.To.Add(new MailboxAddress(lead.ContactName.Trim(), lead.Email.Trim()));
        message.Subject = email.Subject;
        message.MessageId = MimeUtils.GenerateMessageId(MailboxAddress.Parse(settings.FromAddress.Trim()).Domain);
        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();
        return message;
    }

    /// <summary>True when the address is a single, well-formed email address.</summary>
    public static bool IsSendableAddress(string email) =>
        email.Trim() is { Length: > 0 } trimmed
        && !trimmed.Any(c => char.IsWhiteSpace(c) || c is ',' or ';')
        && MailboxAddress.TryParse(trimmed, out var address)
        && address.Address.Contains('@');

    // [text](url) → "text (url)" for the plain-text part.
    private static string ToPlainText(string body) =>
        MarkdownLink().Replace(body, m => m.Groups["text"].Value == m.Groups["url"].Value
            ? m.Groups["url"].Value
            : $"{m.Groups["text"].Value} ({m.Groups["url"].Value})");

    private static string ToHtml(string body)
    {
        // Pull links out first so escaping and auto-linking don't mangle them.
        var links = new List<string>();
        string Keep(string html)
        {
            links.Add(html);
            return $"\u0001{links.Count - 1}\u0002";
        }

        var text = MarkdownLink().Replace(body, m => Keep(
            $"<a href=\"{WebUtility.HtmlEncode(m.Groups["url"].Value)}\">{WebUtility.HtmlEncode(m.Groups["text"].Value)}</a>"));
        text = BareUrl().Replace(text, m => Keep($"<a href=\"{WebUtility.HtmlEncode(m.Value)}\">{WebUtility.HtmlEncode(m.Value)}</a>"));
        text = WebUtility.HtmlEncode(text);
        text = KeptLink().Replace(text, m => links[int.Parse(m.Groups["index"].Value)]);

        var html = new StringBuilder("<div style=\"font-family: Calibri, Arial, sans-serif; font-size: 11pt; color: #1b1b1a;\">");
        foreach (var paragraph in Regex.Split(text.Trim(), @"\n\s*\n"))
        {
            html.Append("<p style=\"margin: 0 0 12px 0;\">").Append(paragraph.Replace("\n", "<br>")).Append("</p>");
        }
        return html.Append("</div>").ToString();
    }

    private static void AddNote(List<string> notes, string note)
    {
        if (!notes.Contains(note))
        {
            notes.Add(note);
        }
    }

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\{\{\s*(?<name>\w+)\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"\[(?<text>[^\]\n]+)\]\((?<url>https?://[^\s)]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"https?://[^\s<>\u0001\u0002]+[^\s<>.,;:!?)\]\u0001\u0002]")]
    private static partial Regex BareUrl();

    [GeneratedRegex("\u0001(?<index>\\d+)\u0002")]
    private static partial Regex KeptLink();
}
