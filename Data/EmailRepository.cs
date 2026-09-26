using Dapper;
using LeadManager.Models;

namespace LeadManager.Data;

/// <summary>Email templates and the log of emails sent to leads.</summary>
public sealed class EmailRepository(AppDatabase database)
{
    public List<EmailTemplate> GetTemplates()
    {
        using var connection = database.Open();
        return connection.Query<EmailTemplate>("SELECT * FROM EmailTemplates ORDER BY Name").AsList();
    }

    public void InsertTemplate(EmailTemplate template)
    {
        template.CreatedAt = template.UpdatedAt = AppDatabase.Now();
        using var connection = database.Open();
        template.Id = connection.ExecuteScalar<long>("""
            INSERT INTO EmailTemplates (Name, Subject, Body, CreatedAt, UpdatedAt)
            VALUES (@Name, @Subject, @Body, @CreatedAt, @UpdatedAt)
            RETURNING Id
            """, template);
    }

    public void UpdateTemplate(EmailTemplate template)
    {
        template.UpdatedAt = AppDatabase.Now();
        using var connection = database.Open();
        connection.Execute("""
            UPDATE EmailTemplates SET Name = @Name, Subject = @Subject, Body = @Body, UpdatedAt = @UpdatedAt
            WHERE Id = @Id
            """, template);
    }

    public void DeleteTemplate(long id)
    {
        using var connection = database.Open();
        connection.Execute("DELETE FROM EmailTemplates WHERE Id = @id", new { id });
    }

    /// <summary>Returns a name not used by another template: "Name", "Name (2)", "Name (3)"…</summary>
    public string UniqueTemplateName(string name)
    {
        var taken = GetTemplates().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{name} ({n})";
        }
        return candidate;
    }

    public void LogSent(SentEmail email)
    {
        using var connection = database.Open();
        email.Id = connection.ExecuteScalar<long>("""
            INSERT INTO SentEmails (LeadId, TemplateId, ToAddress, Subject, Body, Status, Error, MessageId, SentAt)
            VALUES (@LeadId, @TemplateId, @ToAddress, @Subject, @Body, @Status, @Error, @MessageId, @SentAt)
            RETURNING Id
            """, email);
    }

    public List<SentEmail> GetSentTo(long leadId)
    {
        using var connection = database.Open();
        return connection.Query<SentEmail>(
            "SELECT * FROM SentEmails WHERE LeadId = @leadId ORDER BY SentAt DESC, Id DESC", new { leadId }).AsList();
    }

    /// <summary>Successful sends to any of these leads, for spotting repeats before sending again.</summary>
    public List<SentEmail> GetSuccessfulSends(IEnumerable<long> leadIds)
    {
        using var connection = database.Open();
        return connection.Query<SentEmail>(
            "SELECT * FROM SentEmails WHERE Status = @status AND LeadId IN @leadIds",
            new { status = SentEmail.SentStatus, leadIds = leadIds.ToArray() }).AsList();
    }
}
