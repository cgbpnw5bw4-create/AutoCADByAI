using DomainSchemas;

namespace WorkerContracts;

public enum WorkerOutputStatus
{
    Completed,
    Rejected,
    Failed
}

public sealed record WorkerOutput(
    WorkerOutputStatus Status,
    IReadOnlyList<ArtifactInfo> GeneratedArtifacts,
    string ExecutionLog,
    IReadOnlyList<string> Issues);
