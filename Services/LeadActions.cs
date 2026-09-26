using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using LeadManager.Models;

namespace LeadManager.Services;

/// <summary>Things you can do with a lead outside the app. Each returns a message to show the user.</summary>
public static class LeadActions
{
    public static string CopyEmail(Lead lead)
    {
        var email = lead.Email.Trim();
        if (email.Length == 0)
        {
            return $"{lead.DisplayName} has no email address.";
        }
        try
        {
            Clipboard.SetText(email);
            return $"Copied {email}";
        }
        catch (COMException)
        {
            return "The clipboard is busy. Try again.";
        }
    }

    public static string OpenWebsite(Lead lead)
    {
        var url = lead.SourceUrls
            .Split(['|', ',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        if (url is null)
        {
            return $"{lead.DisplayName} has no website link.";
        }
        return Open(url) ?? $"Opening {new Uri(url).Host}…";
    }

    public static void ShowInExplorer(string path)
    {
        Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    // Returns an error message, or null when it opened.
    private static string? Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Couldn't open {target}: {ex.Message}";
        }
    }
}
