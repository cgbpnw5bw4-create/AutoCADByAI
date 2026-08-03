using DomainSchemas;

namespace SolidWorksWorker.Features;

public static class FeatureHandlerTypes
{
    public const string Sketch = "sketch";
}

public static class FeatureApiEvidenceStatuses
{
    public const string Verified = "verified";
    public const string Unverified = "unverified";
}

public sealed record FeatureApiEvidence(
    string ApiName,
    string InterfaceSource,
    IReadOnlyList<string> Parameters,
    string ReturnValue,
    IReadOnlyList<string> Preconditions,
    string Status,
    IReadOnlyList<string> KnownFailureModes,
    IReadOnlyList<string> ProjectEvidence,
    string? EvidenceId = null,
    string? HandlerVersion = null,
    string? ParameterProfile = null,
    string? SolidWorksVersion = null,
    string? DiagnosticRunPath = null,
    string? SourceRevision = null)
{
    public bool AllowsRealExecution =>
        string.Equals(Status, FeatureApiEvidenceStatuses.Verified, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(EvidenceId) &&
        !string.IsNullOrWhiteSpace(HandlerVersion) &&
        !string.IsNullOrWhiteSpace(ParameterProfile) &&
        !string.IsNullOrWhiteSpace(SolidWorksVersion) &&
        !string.IsNullOrWhiteSpace(DiagnosticRunPath) &&
        !string.IsNullOrWhiteSpace(SourceRevision);
}

public sealed record FeatureHandlerParameterDefinition(
    string Name,
    string ValueKind,
    bool Required,
    string Description);

public sealed record FeatureHandlerValidationResult(
    bool IsValid,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public static FeatureHandlerValidationResult Passed() =>
        new(true, null, Array.Empty<string>());

    public static FeatureHandlerValidationResult Failed(string stage, params string[] issues) =>
        new(false, stage, issues);
}

public sealed record FeatureHandlerBuildPlanContext(
    string OperationId,
    string SketchPlane,
    IReadOnlyList<string> Dependencies);

public sealed record FeatureHandlerBuildPlanResult(
    SolidWorksOperation? Operation,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess => Operation is not null && string.IsNullOrWhiteSpace(FailureStage);
}

public sealed class FeatureHandlerExecutionState
{
    private readonly Dictionary<string, object> _objects = new(StringComparer.OrdinalIgnoreCase);

    public void Set(string key, object value) => _objects[key] = value;

    public bool TryGet(string key, out object value) => _objects.TryGetValue(key, out value!);
}

public sealed record FeatureHandlerExecutionContext(
    ISolidWorksFeatureAdapter Adapter,
    FeatureDefinition Feature,
    SolidWorksOperation Operation,
    FeatureHandlerExecutionState State);

public sealed record FeatureHandlerExecutionResult(
    bool IsSuccess,
    string? FailureStage,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    object? CreatedObject = null)
{
    public static FeatureHandlerExecutionResult Passed(
        IReadOnlyList<string>? logs = null,
        object? createdObject = null) =>
        new(true, null, logs ?? Array.Empty<string>(), Array.Empty<string>(), createdObject);

    public static FeatureHandlerExecutionResult Failed(string stage, params string[] issues) =>
        new(false, stage, Array.Empty<string>(), issues);
}

public sealed record FeatureHandlerReport(
    string FeatureId,
    string FeatureType,
    string HandlerName,
    string ApiEvidenceStatus,
    string? FailureStage,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string? AdapterId = null,
    string? AdapterVersion = null,
    bool ResultObjectValidated = false,
    bool RebuildPassed = false,
    string? EvidenceId = null,
    string? EvidenceHandlerVersion = null,
    string? EvidenceParameterProfile = null,
    string? EvidenceSolidWorksVersion = null,
    string? EvidenceDiagnosticRunPath = null,
    string? EvidenceSourceRevision = null,
    bool GeometryChangeValidated = false,
    double? VolumeBeforeCubicMeters = null,
    double? VolumeAfterCubicMeters = null);

public sealed record FeatureAdapterArtifact(
    string OperationId,
    string FeatureId,
    string FeatureType,
    string AdapterId,
    string AdapterVersion,
    bool ResultObjectValidated,
    bool RebuildPassed,
    bool GeometryChangeValidated = false,
    double? VolumeBeforeCubicMeters = null,
    double? VolumeAfterCubicMeters = null);

public interface ISolidWorksFeatureAdapter : IDisposable
{
    string AdapterId { get; }

    string AdapterVersion { get; }

    Task<FeatureHandlerExecutionResult> ExecuteSketchAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default);

    Task<FeatureHandlerExecutionResult> ExecuteExtrudeBossAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default);

    Task<FeatureHandlerExecutionResult> ExecuteExtrudeCutAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default);

    Task<FeatureHandlerExecutionResult> ExecuteHoleAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default);
}

public interface IFeatureHandler
{
    string FeatureType { get; }

    string HandlerId { get; }

    string HandlerVersion { get; }

    string FailureStage { get; }

    IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; }

    FeatureApiEvidence ApiEvidence { get; }

    bool CanHandle(FeatureDefinition feature);

    FeatureHandlerValidationResult Validate(FeatureDefinition feature);

    FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(
        FeatureDefinition feature);

    FeatureHandlerValidationResult ValidateEvidenceForRealExecution(FeatureDefinition feature);

    FeatureHandlerValidationResult ValidateRuntimeForRealExecution(
        string? actualSolidWorksVersion);

    FeatureHandlerBuildPlanResult BuildPlan(
        FeatureDefinition feature,
        FeatureHandlerBuildPlanContext context);

    Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default);

    FeatureHandlerReport GenerateReport(
        FeatureDefinition feature,
        FeatureHandlerExecutionResult result);
}

public abstract class FeatureHandlerBase : IFeatureHandler
{
    public abstract string FeatureType { get; }

    public abstract string OperationType { get; }

    public virtual string HandlerId => $"solidworks.{FeatureType}";

    public virtual string HandlerVersion => "2.0-c.2";

    public virtual string FailureStage => PartFamilyFailureStages.InvalidFeatureParameter;

    public abstract IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; }

    public abstract FeatureApiEvidence ApiEvidence { get; }

    public bool CanHandle(FeatureDefinition feature) =>
        feature is not null &&
        feature.FeatureType.Equals(FeatureType, StringComparison.OrdinalIgnoreCase);

    public abstract FeatureHandlerValidationResult Validate(FeatureDefinition feature);

    public virtual FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(
        FeatureDefinition feature) =>
        Validate(feature);

    public virtual FeatureHandlerValidationResult ValidateEvidenceForRealExecution(
        FeatureDefinition feature)
    {
        var validation = ValidateParameterProfileForRealExecution(feature);
        if (!validation.IsValid)
        {
            return validation;
        }

        return FeatureExecutionEvidencePolicy.ValidateEvidence(this, feature);
    }

    public virtual FeatureHandlerValidationResult ValidateRuntimeForRealExecution(
        string? actualSolidWorksVersion) =>
        FeatureExecutionEvidencePolicy.ValidateRuntimeVersion(this, actualSolidWorksVersion);

    public virtual FeatureHandlerBuildPlanResult BuildPlan(
        FeatureDefinition feature,
        FeatureHandlerBuildPlanContext context)
    {
        var validation = Validate(feature);
        if (!validation.IsValid)
        {
            return new(null, validation.FailureStage, validation.Issues);
        }

        var parameters = new Dictionary<string, string>(feature.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            ["feature_id"] = feature.FeatureId,
            ["feature_type"] = feature.FeatureType
        };
        return new(
            new SolidWorksOperation(
                context.OperationId,
                OperationType,
                context.SketchPlane,
                parameters,
                context.Dependencies,
                $"Feature {feature.FeatureId} planned by {GetType().Name}."),
            null,
            Array.Empty<string>());
    }

    public abstract Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default);

    public FeatureHandlerReport GenerateReport(
        FeatureDefinition feature,
        FeatureHandlerExecutionResult result)
    {
        var artifact = result.CreatedObject as FeatureAdapterArtifact;
        return new(
            feature.FeatureId,
            feature.FeatureType,
            $"{HandlerId}@{HandlerVersion}",
            ApiEvidence.Status,
            result.FailureStage,
            result.Logs,
            result.Issues,
            artifact?.AdapterId,
            artifact?.AdapterVersion,
            artifact?.ResultObjectValidated ?? false,
            artifact?.RebuildPassed ?? false,
            ApiEvidence.EvidenceId,
            ApiEvidence.HandlerVersion,
            ApiEvidence.ParameterProfile,
            ApiEvidence.SolidWorksVersion,
            ApiEvidence.DiagnosticRunPath,
            ApiEvidence.SourceRevision,
            artifact?.GeometryChangeValidated ?? false,
            artifact?.VolumeBeforeCubicMeters,
            artifact?.VolumeAfterCubicMeters);
    }

    protected FeatureHandlerExecutionResult EvidenceBlocked(FeatureDefinition feature) =>
        FeatureHandlerExecutionResult.Failed(
            PartFamilyFailureStages.FeatureApiUnverified,
            $"{PartFamilyFailureStages.FeatureApiUnverified}: {feature.FeatureId}/{FeatureType} has api_evidence_status = {ApiEvidence.Status}.");

    protected static FeatureHandlerValidationResult EvidenceProfileBlocked(
        FeatureDefinition feature,
        string message) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.FeatureApiUnverified,
            $"{PartFamilyFailureStages.FeatureApiUnverified}: " +
            $"{feature.FeatureId}/{feature.FeatureType}: {message}");

    protected static FeatureHandlerExecutionResult AdapterMissing(FeatureDefinition feature) =>
        FeatureHandlerExecutionResult.Failed(
            PartFamilyFailureStages.FeatureAdapterMissing,
            $"{PartFamilyFailureStages.FeatureAdapterMissing}: no SolidWorks feature adapter is available for {feature.FeatureId}/{feature.FeatureType}.");

    protected static FeatureHandlerValidationResult RejectUnknownParameters(
        FeatureDefinition feature,
        params string[] allowedParameters)
    {
        var allowed = new HashSet<string>(
            allowedParameters.Concat(
            [
                "feature_id",
                "feature_type",
                "sketch_id",
                "referenced_sketches",
                "target_reference"
            ]),
            StringComparer.OrdinalIgnoreCase);
        var unknown = feature.Parameters.Keys
            .Where(parameter => !allowed.Contains(parameter))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return unknown.Length == 0
            ? FeatureHandlerValidationResult.Passed()
            : FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} contains " +
                $"parameters outside the authorized profile: {string.Join(", ", unknown)}.");
    }
}
