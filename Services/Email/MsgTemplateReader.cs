using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MsgReader.Outlook;

namespace LeadManager.Services.Email;

/// <summary>Reads an Outlook .msg file into a template's name, subject and plain-text body.</summary>
public static partial class MsgTemplateReader
{
    static MsgTemplateReader()
    {
        // Outlook bodies use Windows code pages (e.g. 1252), which .NET only knows once this provider is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static (string Name, string Subject, string Body) Read(string path)
    {
        using var message = new Storage.Message(path);
        var body = message.BodyText;
        if (string.IsNullOrWhiteSpace(body))
        {
            body = HtmlToText(message.BodyHtml ?? "");
        }

        body = body.Replace("\r\n", "\n").Replace(' ', ' ');
        body = TrailingSpaces().Replace(body, "\n");
        body = ExtraBlankLines().Replace(body, "\n\n");
        // Outlook writes a hyperlink as "text <url>" in the plain-text body; keep it as a link.
        body = OutlookLink().Replace(body, m => $"[{m.Groups["text"].Value}]({m.Groups["url"].Value})");
        body = SpaceBeforePunctuation().Replace(body, "$1");

        return (Path.GetFileNameWithoutExtension(path), (message.Subject ?? "").Trim(), body.Trim());
    }

    private static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, @"<(br|/p|/div)[^>]*>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", "");
        return WebUtility.HtmlDecode(text);
    }

    [GeneratedRegex(@"[ \t]+\n")]
    private static partial Regex TrailingSpaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();

    [GeneratedRegex(@"(?<text>[^\s<>]+) <(?<url>https?://[^>\s]+)>")]
    private static partial Regex OutlookLink();

    // "…(https://x) ." → "…(https://x)." left behind where Outlook put a space after the link.
    [GeneratedRegex(@"(\]\([^)]+\)) (?=[.,;:!?])")]
    private static partial Regex SpaceBeforePunctuation();
}
