namespace PlatformCore;

public sealed record SystemEvent(
    string EventType,
    string Message,
    IReadOnlyDictionary<string, string> Data,
    DateTimeOffset CreatedAt);
