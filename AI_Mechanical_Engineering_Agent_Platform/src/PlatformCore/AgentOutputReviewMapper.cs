using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public static class AgentOutputReviewMapper
{
    public static ReviewReport ToReviewReport(string reviewerId, AgentOutput output)
    {
        var hasFatalError = output.Status == AgentOutputStatus.Failed;
        var requiresHumanApproval = output.Status == AgentOutputStatus.NeedsHumanApproval;
        var collaborationFailures = output.InternalCollaborationReport?.AgentOutputs
            .Where(agentOutput => string.Equals(agentOutput.Status, AgentOutputStatus.Failed.ToString(), StringComparison.OrdinalIgnoreCase))
            .Select(agentOutput => agentOutput.AgentId)
            .ToArray() ?? Array.Empty<string>();
        var drawingReview = output.ReviewReport ??
            output.InternalCollaborationReport?.AgentOutputs.LastOrDefault(agentOutput => agentOutput.ReviewReport is not null)?.ReviewReport;
        var drawingReviewFailed = drawingReview is { IsPassed: false };
        var allIssues = output.Issues
            .Concat(output.InternalCollaborationReport?.Issues ?? Array.Empty<string>())
            .Concat(collaborationFailures.Select(agentId => $"Internal agent failed: {agentId}"))
            .Concat(drawingReviewFailed ? drawingReview!.Issues : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var isPassed =
            output.Status == AgentOutputStatus.Completed &&
            allIssues.Length == 0 &&
            collaborationFailures.Length == 0 &&
            drawingReview is not { IsPassed: false };
        var score = isPassed
            ? Math.Min(1.0, drawingReview?.Score ?? 1.0)
            : hasFatalError || collaborationFailures.Length > 0 ? 0.0 : 0.4;

        return new ReviewReport(
            $"gateway-review-{Guid.NewGuid():N}",
            reviewerId,
            isPassed,
            score,
            allIssues,
            requiresHumanApproval,
            hasFatalError || collaborationFailures.Length > 0);
    }
}
