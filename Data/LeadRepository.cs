using Dapper;
using LeadManager.Models;
using Microsoft.Data.Sqlite;

namespace LeadManager.Data;

/// <summary>Reads and writes the Leads table in a local SQLite file.</summary>
public sealed class LeadRepository
{
    private const int SchemaVersion = 1;

    private const string CreateSchemaSql = """
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

    // Dates go through date() so the file holds plain yyyy-MM-dd text rather than midnight timestamps.
    private const string InsertSql = """
        INSERT INTO Leads (
            LeadCode, FitPriority, ContactName, Practice, Email, InboxType, Location, Subjects, FitEvidence,
            OutreachAngle, SourceUrls, SourceVerification, Caveats, ResearchDate, EmailDeliverability, Interest,
            Status, ContactedOn, FollowUpOn, Notes, ImportedFrom, CreatedAt, UpdatedAt)
        VALUES (
            NULLIF(TRIM(@LeadCode), ''), @FitPriority, @ContactName, @Practice, @Email, @InboxType, @Location, @Subjects, @FitEvidence,
            @OutreachAngle, @SourceUrls, @SourceVerification, @Caveats, date(@ResearchDate), @EmailDeliverability, @Interest,
            @Status, date(@ContactedOn), date(@FollowUpOn), @Notes, @ImportedFrom, @CreatedAt, @UpdatedAt)
        RETURNING Id
        """;

    private const string UpdateSql = """
        UPDATE Leads SET
            LeadCode = NULLIF(TRIM(@LeadCode), ''), FitPriority = @FitPriority, ContactName = @ContactName,
            Practice = @Practice, Email = @Email, InboxType = @InboxType, Location = @Location, Subjects = @Subjects,
            FitEvidence = @FitEvidence, OutreachAngle = @OutreachAngle, SourceUrls = @SourceUrls,
            SourceVerification = @SourceVerification, Caveats = @Caveats, ResearchDate = date(@ResearchDate),
            EmailDeliverability = @EmailDeliverability, Interest = @Interest, Status = @Status,
            ContactedOn = date(@ContactedOn), FollowUpOn = date(@FollowUpOn), Notes = @Notes,
            ImportedFrom = @ImportedFrom, UpdatedAt = @UpdatedAt
        WHERE Id = @Id
        """;

    private readonly string _connectionString;

    public LeadRepository(string databasePath)
    {
        DatabasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        EnsureSchema();
    }

    public string DatabasePath { get; }

    public List<Lead> GetAll()
    {
        using var connection = Open();
        return connection.Query<Lead>("SELECT * FROM Leads").AsList();
    }

    public void Insert(Lead lead)
    {
        using var connection = Open();
        Insert(connection, null, lead);
    }

    public void Update(Lead lead)
    {
        using var connection = Open();
        Update(connection, null, lead);
    }

    public void Delete(long id)
    {
        using var connection = Open();
        connection.Execute("DELETE FROM Leads WHERE Id = @id", new { id });
    }

    /// <summary>Writes an import in one transaction so a bad row can't leave it half-applied.</summary>
    public void SaveImport(IEnumerable<Lead> added, IEnumerable<Lead> updated)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        foreach (var lead in added)
        {
            Insert(connection, transaction, lead);
        }
        foreach (var lead in updated)
        {
            Update(connection, transaction, lead);
        }
        transaction.Commit();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction? transaction, Lead lead)
    {
        lead.CreatedAt = lead.UpdatedAt = Now();
        lead.Id = connection.ExecuteScalar<long>(InsertSql, lead, transaction);
    }

    private static void Update(SqliteConnection connection, SqliteTransaction? transaction, Lead lead)
    {
        lead.UpdatedAt = Now();
        connection.Execute(UpdateSql, lead, transaction);
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        var version = connection.ExecuteScalar<long>("PRAGMA user_version");
        if (version < 1)
        {
            connection.Execute(CreateSchemaSql);
        }
        // Future schema changes go here as `if (version < 2) { ALTER TABLE ... }`.
        connection.Execute($"PRAGMA user_version = {SchemaVersion}");
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static DateTime Now()
    {
        var now = DateTime.Now;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
    }
}
