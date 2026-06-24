namespace Storage;

public interface IEventStore
{
    Task AppendAsync(StoredEvent systemEvent, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredEvent>> ListAsync(CancellationToken cancellationToken = default);
}
