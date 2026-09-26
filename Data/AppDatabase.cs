using Dapper;
using Microsoft.Data.Sqlite;

namespace LeadManager.Data;

/// <summary>The SQLite file and its schema. Opening it upgrades an older file in place.</summary>
public sealed class AppDatabase
{
    private const int SchemaVersion = 2;

    private const string LeadsTableSql = """
        CREATE TABLE IF NOT EXISTS Leads (
            Id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            LeadCode            TEXT COLLATE NOCASE UNIQUE,   -- "Lead ID" from the CSV, e.g. ST-001
            FitPriority         TEXT NOT NULL DEFAULT '',
            ContactName         TEXT NOT NULL DEFAULT '',
            Practice            TEXT NOT NULL DEFAULT '',
            Email               TEXT NOT NULL DEFAULT '',
            InboxType           TEXT NOT NULL DEFAULT '',
            Location            TEXT NOT NULL DEFAULT '',
            Subjects            TEXT NOT NULL DEFAULT '',
            FitEvidence         TEXT NOT NULL DEFAULT '',
            OutreachAngle       TEXT NOT NULL DEFAULT '',
            SourceUrls          TEXT NOT NULL DEFAULT '',
            SourceVerification  TEXT NOT NULL DEFAULT '',
            Caveats             TEXT NOT NULL DEFAULT '',
            ResearchDate        TEXT,                         -- yyyy-MM-dd
            EmailDeliverability TEXT NOT NULL DEFAULT 'Not tested',
            Interest            TEXT NOT NULL DEFAULT 'Unknown',
            Status              TEXT NOT NULL DEFAULT 'Not contacted',
            ContactedOn         TEXT,                         -- yyyy-MM-dd; NULL = never contacted
            FollowUpOn          TEXT,                         -- yyyy-MM-dd
            Notes               TEXT NOT NULL DEFAULT '',
            ImportedFrom        TEXT NOT NULL DEFAULT '',     -- CSV file the lead first came from
            CreatedAt           TEXT NOT NULL,
            UpdatedAt           TEXT NOT NULL
        );
        """;

    private const string EmailTablesSql = """
        CREATE TABLE IF NOT EXISTS EmailTemplates (
            Id        INTEGER PRIMARY KEY AUTOINCREMENT,
            Name      TEXT NOT NULL COLLATE NOCASE UNIQUE,
            Subject   TEXT NOT NULL DEFAULT '',
            Body      TEXT NOT NULL DEFAULT '',
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        -- One row per email the app tried to send, kept even if the lead is deleted later.
        CREATE TABLE IF NOT EXISTS SentEmails (
            Id         INTEGER PRIMARY KEY AUTOINCREMENT,
            LeadId     INTEGER,             -- Leads.Id
            TemplateId INTEGER,             -- EmailTemplates.Id the message started from, if any
            ToAddress  TEXT NOT NULL,
            Subject    TEXT NOT NULL,
            Body       TEXT NOT NULL,       -- the plain-text version as sent
            Status     TEXT NOT NULL,       -- 'Sent' or 'Failed'
            Error      TEXT,
            MessageId  TEXT,
            SentAt     TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_SentEmails_LeadId ON SentEmails (LeadId);
        """;

    private readonly string _connectionString;

    public AppDatabase(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        Migrate();
    }

    public string Path { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// Overwrites this database with a copy of another one (e.g. production into test). The source is opened
    /// read-only, so it's never changed.
    /// </summary>
    public void ReplaceWith(string sourcePath)
    {
        var source = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString();
        using (var from = new SqliteConnection(source))
        using (var to = Open())
        {
            from.Open();
            from.BackupDatabase(to);
        }
        // The copy may come from an older version of the app.
        Migrate();
    }

    /// <summary>Timestamps are stored to the second so the file reads cleanly in other tools.</summary>
    public static DateTime Now()
    {
        var now = DateTime.Now;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }

    private void Migrate()
    {
        using var connection = Open();
        var version = connection.ExecuteScalar<long>("PRAGMA user_version");
        using var transaction = connection.BeginTransaction();
        if (version < 1)
        {
            connection.Execute(LeadsTableSql, transaction: transaction);
        }
        if (version < 2)
        {
            connection.Execute(EmailTablesSql, transaction: transaction);
        }
        // Future changes go here as `if (version < 3) { ALTER TABLE ... }`.
        connection.Execute($"PRAGMA user_version = {SchemaVersion}", transaction: transaction);
        transaction.Commit();
    }
}
