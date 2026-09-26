using Dapper;
using LeadManager.Models;
using Microsoft.Data.Sqlite;

namespace LeadManager.Data;

/// <summary>Reads and writes the Leads table.</summary>
public sealed class LeadRepository(AppDatabase database)
{
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

    public string DatabasePath => database.Path;

    public List<Lead> GetAll()
    {
        using var connection = database.Open();
        return connection.Query<Lead>("SELECT * FROM Leads").AsList();
    }

    public void Insert(Lead lead)
    {
        using var connection = database.Open();
        Insert(connection, null, lead);
    }

    public void Update(Lead lead)
    {
        using var connection = database.Open();
        Update(connection, null, lead);
    }

    public void Delete(long id)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM Leads WHERE Id = @id", new { id });
    }

    /// <summary>Writes an import in one transaction so a bad row can't leave it half-applied.</summary>
    public void SaveImport(IEnumerable<Lead> added, IEnumerable<Lead> updated)
    {
        using var connection = database.Open();
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
        lead.CreatedAt = lead.UpdatedAt = AppDatabase.Now();
        lead.Id = connection.ExecuteScalar<long>(InsertSql, lead, transaction);
    }

    private static void Update(SqliteConnection connection, SqliteTransaction? transaction, Lead lead)
    {
        lead.UpdatedAt = AppDatabase.Now();
        connection.Execute(UpdateSql, lead, transaction);
    }
}
