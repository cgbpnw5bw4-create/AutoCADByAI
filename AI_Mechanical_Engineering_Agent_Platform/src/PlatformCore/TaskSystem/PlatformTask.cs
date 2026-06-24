namespace PlatformCore;

public enum PlatformTaskStatus
{
    Created,
    Running,
    WaitingForReview,
    Rejected,
    Passed,
    Failed
}

public sealed class PlatformTask
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public PlatformTaskStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}
