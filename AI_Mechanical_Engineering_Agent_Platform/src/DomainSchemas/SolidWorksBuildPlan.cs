namespace DomainSchemas;

public sealed record SolidWorksBuildPlan(
    string PlanId,
    string SourceCadModelSpecId,
    string TargetCadSystem,
    string PartType,
    string Unit,
    IReadOnlyList<SolidWorksOperation> Operations,
    IReadOnlyList<SolidWorksArtifact> ExpectedArtifacts,
    IReadOnlyList<string> ValidationRules,
    IReadOnlyList<string> RiskNotes);

public sealed record SolidWorksOperation(
    string OperationId,
    string OperationType,
    string SketchPlane,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> DependsOn,
    string ExpectedResult);

public sealed record SolidWorksWorkerRequest(
    string RequestId,
    SolidWorksBuildPlan BuildPlan,
    string OutputDirectory,
    bool DryRun = true,
    bool AllowRealCadExecution = false);

public sealed record SolidWorksWorkerResult(
    string RequestId,
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string ExecutionMode = "Fake",
    bool RealCadExecuted = false);

public sealed record SolidWorksArtifact(
    string ArtifactId,
    string ArtifactType,
    string FilePath,
    string ExpectedExtension,
    bool Exists,
    long SizeBytes,
    string Description);
