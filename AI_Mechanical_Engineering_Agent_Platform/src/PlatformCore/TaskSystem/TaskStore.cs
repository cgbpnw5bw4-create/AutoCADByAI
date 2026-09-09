namespace PlatformCore;

public sealed class TaskStore
{
    private readonly Dictionary<string, PlatformTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

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

        lock (_sync) { _tasks[task.Id] = task; }
        return task;
    }

    public PlatformTask? Get(string taskId)
    {
        lock (_sync) { return _tasks.TryGetValue(taskId, out var task) ? task : null; }
    }

    public void UpdateStatus(string taskId, PlatformTaskStatus status)
    {
        lock (_sync)
        {
            var task = Get(taskId) ?? throw new InvalidOperationException($"Task '{taskId}' was not found.");
            _tasks[taskId] = task with { Status = status, UpdatedAt = DateTimeOffset.UtcNow };
        }
    }

    public IReadOnlyList<PlatformTask> GetAll()
    {
        lock (_sync) { return _tasks.Values.ToArray(); }
    }
}
