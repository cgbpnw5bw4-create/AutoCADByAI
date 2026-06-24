namespace DomainSchemas;

public sealed record ReviewReport(
    string ReviewId,
    string ReviewerId,
    bool IsPassed,
    double Score,
    IReadOnlyList<string> Issues,
    bool RequiresHumanApproval,
    bool HasFatalError);
