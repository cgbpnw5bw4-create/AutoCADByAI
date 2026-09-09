namespace PlatformCore;

public sealed class InMemoryAuditLog
{
    private readonly List<AuditLogEntry> _entries = new();
    private readonly object _sync = new();

    public void Record(string category, string actor, string action, string message)
    {
        lock (_sync) { _entries.Add(new AuditLogEntry(category, actor, action, message, DateTimeOffset.UtcNow)); }
    }

    public IReadOnlyList<AuditLogEntry> GetEntries()
    {
        lock (_sync) { return _entries.ToArray(); }
    }
}
