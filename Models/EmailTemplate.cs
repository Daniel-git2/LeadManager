namespace LeadManager.Models;

/// <summary>A reusable email. Subject and body may contain placeholders like {{FirstName}}.</summary>
public sealed class EmailTemplate
{
    public long Id { get; set; }

    public string Name { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Body { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>One email the app sent (or tried to send) to a lead.</summary>
public sealed class SentEmail
{
    public const string SentStatus = "Sent";
    public const string FailedStatus = "Failed";

    public long Id { get; set; }

    public long? LeadId { get; set; }

    public long? TemplateId { get; set; }

    public string ToAddress { get; set; } = "";

    public string Subject { get; set; } = "";

    public string Body { get; set; } = "";

    public string Status { get; set; } = SentStatus;

    public string? Error { get; set; }

    public string? MessageId { get; set; }

    public DateTime SentAt { get; set; }

    public bool WasSent => Status == SentStatus;
}
