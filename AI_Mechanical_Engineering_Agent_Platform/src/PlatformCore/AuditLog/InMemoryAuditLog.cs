namespace PlatformCore;

public sealed class InMemoryAuditLog
{
    private readonly List<AuditLogEntry> _entries = new();

    public void Record(string category, string actor, string action, string message)
    {
        _entries.Add(new AuditLogEntry(category, actor, action, message, DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<AuditLogEntry> GetEntries() => _entries.ToArray();
}
