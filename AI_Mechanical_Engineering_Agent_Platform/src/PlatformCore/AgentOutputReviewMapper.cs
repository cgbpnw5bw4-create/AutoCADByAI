using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public static class AgentOutputReviewMapper
{
    public static ReviewReport ToReviewReport(string reviewerId, AgentOutput output)
    {
        var hasFatalError = output.Status == AgentOutputStatus.Failed;
        var requiresHumanApproval = output.Status == AgentOutputStatus.NeedsHumanApproval;
        var isPassed = output.Status == AgentOutputStatus.Completed && output.Issues.Count == 0;
        var score = isPassed ? 1.0 : hasFatalError ? 0.0 : 0.4;

        return new ReviewReport(
            $"gateway-review-{Guid.NewGuid():N}",
            reviewerId,
            isPassed,
            score,
            output.Issues,
            requiresHumanApproval,
            hasFatalError);
    }
}
