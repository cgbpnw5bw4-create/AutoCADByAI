namespace PlatformCore;

public enum PlatformTaskStatus
{
    Created,
    Running,
    WaitingForReview,
    Rejected,
    Passed,
    Failed,
    WaitingForHumanApproval,
    Cancelled
}

public sealed record PlatformTask
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public PlatformTaskStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
