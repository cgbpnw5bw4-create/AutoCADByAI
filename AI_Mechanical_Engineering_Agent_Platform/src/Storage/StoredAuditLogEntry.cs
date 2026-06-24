namespace Storage;

public sealed record StoredAuditLogEntry(
    string Category,
    string Actor,
    string Action,
    string Message,
    DateTimeOffset CreatedAt);
