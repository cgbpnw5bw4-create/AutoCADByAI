using DomainSchemas;

namespace QualityGate;

public sealed class RejectReportBuilder
{
    public RejectReport Build(ReviewReport report, GateDecision decision)
    {
        var reasons = report.Issues.Count > 0
            ? report.Issues
            : new[] { decision.Reason };

        return new RejectReport(
            $"reject-{report.ReviewId}",
            report.ReviewId,
            reasons,
            $"Gate rejected review '{report.ReviewId}': {decision.Reason}",
            DateTimeOffset.UtcNow);
    }
}
