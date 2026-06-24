using DomainSchemas;

namespace PlatformCore;

public sealed record WorkflowStepResult(
    string StepName,
    string Status,
    string Message,
    GateDecision? GateDecision = null,
    IReadOnlyList<string>? Logs = null);
