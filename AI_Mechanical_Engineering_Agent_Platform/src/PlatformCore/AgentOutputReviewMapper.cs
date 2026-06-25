using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public static class AgentOutputReviewMapper
{
    public static ReviewReport ToReviewReport(string reviewerId, AgentOutput output)
    {
        var issueArray = output.Issues.ToArray();
        var hasCriticalIssue = issueArray.Any(issue =>
            issue.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("non_retryable", StringComparison.OrdinalIgnoreCase));
        var hasHumanApprovalIssue = issueArray.Any(issue =>
            issue.Contains("needs_human_approval", StringComparison.OrdinalIgnoreCase));
        var hasFatalError = output.Status == AgentOutputStatus.Failed || hasCriticalIssue;
        var requiresHumanApproval = output.Status == AgentOutputStatus.NeedsHumanApproval || hasHumanApprovalIssue;
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
            drawingReview is not { IsPassed: false } &&
            !requiresHumanApproval &&
            !hasFatalError;
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
