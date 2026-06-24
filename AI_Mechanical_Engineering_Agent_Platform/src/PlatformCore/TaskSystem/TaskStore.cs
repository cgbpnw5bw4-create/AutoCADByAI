namespace PlatformCore;

public sealed class TaskStore
{
    private readonly Dictionary<string, PlatformTask> _tasks = new(StringComparer.OrdinalIgnoreCase);

    public PlatformTask Create(string title)
    {
        var now = DateTimeOffset.UtcNow;
        var task = new PlatformTask
        {
            Id = $"task-{Guid.NewGuid():N}",
            Title = title,
            Status = PlatformTaskStatus.Created,
            CreatedAt = now,
            UpdatedAt = now
        };

        _tasks[task.Id] = task;
        return task;
    }

    public PlatformTask? Get(string taskId) =>
        _tasks.TryGetValue(taskId, out var task) ? task : null;

    public void UpdateStatus(string taskId, PlatformTaskStatus status)
    {
        var task = Get(taskId) ?? throw new InvalidOperationException($"Task '{taskId}' was not found.");
        task.Status = status;
        task.UpdatedAt = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<PlatformTask> GetAll() => _tasks.Values.ToArray();
}
