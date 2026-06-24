using DomainSchemas;

namespace QualityGate;

public sealed class GateDecisionPolicy
{
    public GateDecision Decide(ReviewReport report)
    {
        if (report.HasFatalError)
        {
            return new GateDecision(
                $"gate-{report.ReviewId}",
                GateDecisionResult.Failed,
                "Review reported a fatal error.",
                "Route to error-diagnosis.");
        }

        if (report.RequiresHumanApproval)
        {
            return new GateDecision(
                $"gate-{report.ReviewId}",
                GateDecisionResult.NeedsHumanApproval,
                "Review requires human approval.",
                "Wait for human approval.");
        }

        if (report.IsPassed && report.Score >= 0.8)
        {
            return new GateDecision(
                $"gate-{report.ReviewId}",
                GateDecisionResult.Passed,
                "Review score and pass flag meet the gate threshold.",
                "Continue workflow.");
        }

        return new GateDecision(
            $"gate-{report.ReviewId}",
            GateDecisionResult.Rejected,
            "Review failed quality threshold.",
            "Return to previous workflow step.");
    }
}
