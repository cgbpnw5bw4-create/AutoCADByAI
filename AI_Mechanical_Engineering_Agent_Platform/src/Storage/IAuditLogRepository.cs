namespace Storage;

public interface IAuditLogRepository
{
    Task AppendAsync(StoredAuditLogEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredAuditLogEntry>> ListAsync(CancellationToken cancellationToken = default);
}
