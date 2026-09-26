using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using LeadManager.Models;

namespace LeadManager.Data;

public sealed record ImportResult(IReadOnlyList<Lead> Added, IReadOnlyList<Lead> Updated, int Skipped);

/// <summary>Maps lead spreadsheets to and from <see cref="Lead"/> rows.</summary>
public static class LeadCsv
{
    private static readonly Column LeadCodeColumn =
        new("Lead ID", l => l.LeadCode ?? "", (l, v) => l.LeadCode = v, "Lead code", "Code") { FillOnly = true };

    private static readonly Column ContactNameColumn =
        new("Tutor / contact name", l => l.ContactName, (l, v) => l.ContactName = v, "Contact name", "Name", "Contact", "Tutor");

    private static readonly Column PracticeColumn =
        new("Practice", l => l.Practice, (l, v) => l.Practice = v, "Company", "Business", "Organization");

    private static readonly Column EmailColumn =
        new("Public professional email", l => l.Email, (l, v) => l.Email = v, "Email", "Email address", "E-mail");

    // Export writes these in order and under these headers, so an exported file imports straight back.
    // "Fill only" columns hold your own tracking: re-importing a list fills them when blank but never overwrites them.
    private static readonly Column[] Columns =
    [
        LeadCodeColumn,
        new("Fit priority (inferred)", l => l.FitPriority, (l, v) => l.FitPriority = LeadOptions.NormalizePriority(v), "Fit priority", "Priority"),
        ContactNameColumn,
        PracticeColumn,
        EmailColumn,
        new("Inbox type", l => l.InboxType, (l, v) => l.InboxType = v),
        new("Location / service area", l => l.Location, (l, v) => l.Location = v, "Location", "Service area", "City"),
        new("Subjects / student levels", l => l.Subjects, (l, v) => l.Subjects = v, "Subjects", "Student levels"),
        new("Fit evidence", l => l.FitEvidence, (l, v) => l.FitEvidence = v),
        new("Suggested outreach angle", l => l.OutreachAngle, (l, v) => l.OutreachAngle = v, "Outreach angle"),
        new("Source URLs", l => l.SourceUrls, (l, v) => l.SourceUrls = v, "Source URL", "Website", "URL"),
        new("Source verification", l => l.SourceVerification, (l, v) => l.SourceVerification = v),
        new("Caveats", l => l.Caveats, (l, v) => l.Caveats = v),
        new("Research date", l => FormatDate(l.ResearchDate), (l, v) => l.ResearchDate = ParseDate(v)),
        new("Email deliverability", l => l.EmailDeliverability, (l, v) => l.EmailDeliverability = LeadOptions.NormalizeDeliverability(v), "Deliverability")
            { FillOnly = true, Default = LeadOptions.NotTested },
        new("Interest in SkillTrace", l => l.Interest, (l, v) => l.Interest = LeadOptions.NormalizeInterest(v), "Interest")
            { FillOnly = true, Default = LeadOptions.UnknownInterest },
        new("Outreach status", l => l.Status, (l, v) => l.Status = LeadOptions.NormalizeStatus(v), "Status")
            { FillOnly = true, Default = LeadOptions.NotContacted },
        new("Last contacted", l => FormatDate(l.ContactedOn), (l, v) => l.ContactedOn = ParseDate(v), "Contacted on", "Date contacted", "Contacted date")
            { FillOnly = true },
        new("Follow up on", l => FormatDate(l.FollowUpOn), (l, v) => l.FollowUpOn = ParseDate(v), "Follow-up date", "Follow up")
            { FillOnly = true },
        new("Notes", l => l.Notes, (l, v) => l.Notes = v) { FillOnly = true },
    ];

    /// <summary>
    /// Reads a CSV and works out which rows are new and which update an existing lead (matched by
    /// Lead ID, then by email). Nothing is written to the database here.
    /// </summary>
    public static ImportResult Import(string path, IEnumerable<Lead> existingLeads)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { BadDataFound = null, MissingFieldFound = null };
        using var csv = new CsvReader(new StringReader(ReadText(path)), config);
        if (!csv.Read() || !csv.ReadHeader() || csv.HeaderRecord is not { } headers)
        {
            throw new InvalidDataException("The file is empty.");
        }

        var mapped = new List<(Column Column, int Index)>();
        for (var i = 0; i < headers.Length; i++)
        {
            var key = NormalizeHeader(headers[i]);
            var column = Columns.FirstOrDefault(c => c.Keys.Contains(key));
            if (column is not null && mapped.All(m => m.Column != column))
            {
                mapped.Add((column, i));
            }
        }
        if (mapped.Count == 0)
        {
            throw new InvalidDataException("None of the column headers look like lead fields. Expected headers such as \"Name\", \"Email\" or \"Practice\".");
        }

        var byCode = new Dictionary<string, Lead>(StringComparer.OrdinalIgnoreCase);
        var byEmail = new Dictionary<string, Lead>(StringComparer.OrdinalIgnoreCase);
        foreach (var lead in existingLeads)
        {
            Index(lead);
        }

        var added = new List<Lead>();
        var updated = new List<Lead>();
        var skipped = 0;
        var fileName = Path.GetFileName(path);

        while (csv.Read())
        {
            var values = new Dictionary<Column, string>();
            foreach (var (column, index) in mapped)
            {
                var value = csv.GetField(index)?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    values[column] = value;
                }
            }

            var code = values.GetValueOrDefault(LeadCodeColumn, "");
            var email = values.GetValueOrDefault(EmailColumn, "");
            if (code.Length == 0 && email.Length == 0 && !values.ContainsKey(ContactNameColumn) && !values.ContainsKey(PracticeColumn))
            {
                skipped++;
                continue;
            }

            var lead = (code.Length > 0 ? byCode.GetValueOrDefault(code) : null)
                ?? (email.Length > 0 ? byEmail.GetValueOrDefault(email) : null);

            if (lead is null)
            {
                lead = new Lead { ImportedFrom = fileName };
                foreach (var (column, value) in values)
                {
                    column.Set(lead, value);
                }
                added.Add(lead);
            }
            else
            {
                var changed = false;
                foreach (var (column, value) in values)
                {
                    var before = column.Get(lead);
                    if (column.FillOnly && !column.IsBlank(before))
                    {
                        continue;
                    }
                    column.Set(lead, value);
                    changed |= column.Get(lead) != before;
                }
                if (changed && !added.Contains(lead) && !updated.Contains(lead))
                {
                    updated.Add(lead);
                }
            }
            Index(lead);
        }

        return new ImportResult(added, updated, skipped);

        void Index(Lead lead)
        {
            if (!string.IsNullOrWhiteSpace(lead.LeadCode))
            {
                byCode.TryAdd(lead.LeadCode.Trim(), lead);
            }
            if (!string.IsNullOrWhiteSpace(lead.Email))
            {
                byEmail.TryAdd(lead.Email.Trim(), lead);
            }
        }
    }

    public static void Export(string path, IEnumerable<Lead> leads)
    {
        // UTF-8 with a BOM so Excel shows characters like en dashes correctly.
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
        foreach (var column in Columns)
        {
            csv.WriteField(column.Header);
        }
        csv.NextRecord();
        foreach (var lead in leads)
        {
            foreach (var column in Columns)
            {
                csv.WriteField(column.Get(lead));
            }
            csv.NextRecord();
        }
    }

    private static string ReadText(string path)
    {
        // ReadWrite sharing so a file that's still open in Excel can be imported.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            // Excel's plain "CSV" export (as opposed to "CSV UTF-8") uses the Windows-1252 code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    private static string NormalizeHeader(string header) =>
        new(header.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string FormatDate(DateTime? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    private static DateTime? ParseDate(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date)
        || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            ? date.Date
            : null;

    private sealed class Column(string header, Func<Lead, string> get, Action<Lead, string> set, params string[] aliases)
    {
        public string Header { get; } = header;

        public Func<Lead, string> Get { get; } = get;

        public Action<Lead, string> Set { get; } = set;

        public HashSet<string> Keys { get; } = [.. aliases.Prepend(header).Select(NormalizeHeader)];

        /// <summary>Only write this field when it's still blank or at its default value.</summary>
        public bool FillOnly { get; init; }

        public string Default { get; init; } = "";

        public bool IsBlank(string value) => value.Length == 0 || value.Equals(Default, StringComparison.OrdinalIgnoreCase);
    }
}
