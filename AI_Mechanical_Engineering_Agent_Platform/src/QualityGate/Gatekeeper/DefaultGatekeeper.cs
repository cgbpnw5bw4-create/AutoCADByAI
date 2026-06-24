using DomainSchemas;

namespace QualityGate;

public sealed class DefaultGatekeeper : IGatekeeper
{
    private readonly GateDecisionPolicy _policy;
    private readonly RejectReportBuilder _rejectReportBuilder;

    public DefaultGatekeeper(GateDecisionPolicy policy, RejectReportBuilder rejectReportBuilder)
    {
        _policy = policy;
        _rejectReportBuilder = rejectReportBuilder;
    }

    public GateEvaluationResult Evaluate(ReviewReport reviewReport)
    {
        var decision = _policy.Decide(reviewReport);
        if (decision.Result != GateDecisionResult.Rejected)
        {
            return new GateEvaluationResult(decision, null);
        }

        var rejectReport = _rejectReportBuilder.Build(reviewReport, decision);
        return new GateEvaluationResult(decision with { RejectReport = rejectReport }, rejectReport);
    }
}
