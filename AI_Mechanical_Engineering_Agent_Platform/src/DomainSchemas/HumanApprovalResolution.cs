namespace DomainSchemas;

public sealed record HumanApprovalResolution(
    string ApprovalRequestId,
    string StepId,
    string Decision,
    string SubmittedBy,
    string? Comment,
    DateTimeOffset SubmittedAt,
    ReviewReport? OriginalReviewReport,
    IReadOnlyList<string> OriginalIssues);
