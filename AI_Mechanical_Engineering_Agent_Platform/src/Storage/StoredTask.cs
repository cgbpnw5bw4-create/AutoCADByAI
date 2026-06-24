namespace Storage;

public sealed record StoredTask(
    string Id,
    string Title,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
