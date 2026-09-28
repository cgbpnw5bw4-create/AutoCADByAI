using DomainSchemas;

namespace QualityGate;

public sealed record GateEvaluationResult(
    GateDecision Decision,
    RejectReport? RejectReport)
{
    public IReadOnlyList<QualityGateCheckResult> Checks { get; init; } = [];
}
