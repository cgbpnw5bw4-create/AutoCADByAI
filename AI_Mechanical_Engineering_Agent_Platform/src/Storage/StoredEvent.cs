namespace Storage;

public sealed record StoredEvent(
    string EventType,
    string Message,
    IReadOnlyDictionary<string, string> Data,
    DateTimeOffset CreatedAt);
