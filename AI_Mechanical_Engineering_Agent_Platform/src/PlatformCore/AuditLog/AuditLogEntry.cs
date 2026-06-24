namespace PlatformCore;

public sealed record AuditLogEntry(
    string Category,
    string Actor,
    string Action,
    string Message,
    DateTimeOffset CreatedAt);
