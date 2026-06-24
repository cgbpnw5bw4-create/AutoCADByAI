namespace Storage;

public interface ITaskRepository
{
    Task SaveAsync(StoredTask task, CancellationToken cancellationToken = default);

    Task<StoredTask?> GetAsync(string taskId, CancellationToken cancellationToken = default);
}
