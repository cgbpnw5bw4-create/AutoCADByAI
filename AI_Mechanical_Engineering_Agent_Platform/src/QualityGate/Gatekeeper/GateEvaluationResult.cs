using DomainSchemas;

namespace QualityGate;

public sealed record GateEvaluationResult(
    GateDecision Decision,
    RejectReport? RejectReport);
