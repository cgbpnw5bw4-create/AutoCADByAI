namespace PlatformCore;

public sealed class InMemoryEventBus
{
    private readonly List<SystemEvent> _events = new();

    public void Publish(string eventType, string message, IReadOnlyDictionary<string, string>? data = null)
    {
        _events.Add(new SystemEvent(
            eventType,
            message,
            data ?? new Dictionary<string, string>(),
            DateTimeOffset.UtcNow));
    }

    public IReadOnlyList<SystemEvent> GetEvents() => _events.ToArray();
}
