using DomainSchemas;

namespace SolidWorksWorker;

public sealed record PartFamilyDryRunBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> Artifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string? FailureStage = null);

public sealed record PartFamilyBuildContext(
    object Application,
    SolidWorksWorkerRequest Request,
    SolidWorksRuntimeOptions Options,
    string? SolidWorksVersion,
    bool RealCadConnected = true);

public sealed record PartFamilyBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> Artifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string ExecutionMode,
    bool RealCadExecuted,
    string? FailureStage = null);

public interface IPartFamilyBuilder
{
    string PartType { get; }

    string FailureStage { get; }

    bool SupportsRealExecution { get; }

    string ApiEvidence { get; }

    string RealExecutionMode { get; }

    Task<PartFamilyDryRunBuildResult> BuildDryRunAsync(
        SolidWorksWorkerRequest request,
        string artifactsDirectory,
        CancellationToken cancellationToken = default);

    Task<PartFamilyBuildResult> BuildAsync(
        PartFamilyBuildContext context,
        CancellationToken cancellationToken = default);
}

public sealed class PartFamilyBuilderRegistry
{
    private readonly Dictionary<string, IPartFamilyBuilder> _builders = new(StringComparer.OrdinalIgnoreCase);

    public PartFamilyBuilderRegistry(IEnumerable<IPartFamilyBuilder>? builders = null)
    {
        foreach (var builder in builders ?? Array.Empty<IPartFamilyBuilder>())
        {
            Register(builder);
        }
    }

    /// <summary>
    /// 统一建模内核入口。每个已注册零件族都绑定同一个通用 FeatureGraph 执行器，
    /// 不存在零件专用 Builder，也不按 part_type 分支。新增零件族只需在
    /// <see cref="PartTypeRegistry"/> 注册 Definition，无需新增 Builder。
    /// </summary>
    /// <param name="plateBuilder">
    /// 保留参数以兼容既有调用方。统一内核后已不再使用零件专用的 plate builder。
    /// </param>
    public static PartFamilyBuilderRegistry CreateDefault(ISolidWorksPlateBuilder? plateBuilder = null)
    {
        _ = plateBuilder;
        var handlers = Features.FeatureHandlerRegistry.CreateDefault();
        return new(PartTypeRegistry.CreateDefault()
            .GetAll()
            .Select(definition => new Features.SolidWorksFeatureGraphPartFamilyBuilder(
                definition.PartType,
                handlers,
                supportsRealExecution: definition.SupportsRealExecution,
                expectedGeometry: definition.DescribeExpectedGeometry)));
    }

    public void Register(IPartFamilyBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!_builders.TryAdd(builder.PartType, builder))
        {
            throw new InvalidOperationException($"A part family builder is already registered for {builder.PartType}.");
        }
    }

    public bool TryGetBuilder(string? partType, out IPartFamilyBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(partType) && _builders.TryGetValue(partType, out builder!))
        {
            return true;
        }

        builder = null!;
        return false;
    }

    public IReadOnlyList<IPartFamilyBuilder> GetAll() =>
        _builders.Values.OrderBy(builder => builder.PartType, StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed class PlateBasic4HolesPartFamilyBuilder : TextPlaceholderPartFamilyBuilder
{
    private readonly ISolidWorksPlateBuilder _plateBuilder;

    public PlateBasic4HolesPartFamilyBuilder(ISolidWorksPlateBuilder? plateBuilder = null)
    {
        _plateBuilder = plateBuilder ?? new LateBoundSolidWorksPlateBuilder();
    }

    public override string PartType => PlateBasic4HolesDefinition.Type;

    public override string FailureStage => "plate_build_failed";

    public override bool SupportsRealExecution => true;

    public override string ApiEvidence => "real_solidworks_plate_basic_4holes_smoke_passed";

    public override string RealExecutionMode => SolidWorksPartFamilyBuildModes.PlateBasic4Holes;

    public override async Task<PartFamilyBuildResult> BuildAsync(
        PartFamilyBuildContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _plateBuilder.BuildPlateBasicFourHolesAsync(
            context.Application,
            context.Request,
            context.Options,
            context.SolidWorksVersion,
            cancellationToken);
        return new PartFamilyBuildResult(
            result.Status,
            result.GeneratedArtifacts,
            result.Logs,
            result.Issues,
            SolidWorksPlateBuildOutput.ExecutionMode,
            result.RealCadExecuted,
            string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? null : FailureStage);
    }
}

public abstract class TextPlaceholderPartFamilyBuilder : IPartFamilyBuilder
{
    public abstract string PartType { get; }

    public abstract string FailureStage { get; }

    public abstract bool SupportsRealExecution { get; }

    public abstract string ApiEvidence { get; }

    public virtual string RealExecutionMode => "RealBuildPartFamily";

    public virtual Task<PartFamilyBuildResult> BuildAsync(
        PartFamilyBuildContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new PartFamilyBuildResult(
            "Rejected",
            Array.Empty<SolidWorksArtifact>(),
            [$"part_family_builder: {GetType().Name}"],
            [$"{PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient}: {PartType} has no evidence-backed real builder."],
            "RealPreflightOnly",
            RealCadExecuted: false,
            PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient));
    }

    public virtual async Task<PartFamilyDryRunBuildResult> BuildDryRunAsync(
        SolidWorksWorkerRequest request,
        string artifactsDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(request.BuildPlan.PartType, PartType, StringComparison.OrdinalIgnoreCase))
        {
            return new(
                "Failed",
                Array.Empty<SolidWorksArtifact>(),
                Array.Empty<string>(),
                [$"{FailureStage}: builder for {PartType} cannot build {request.BuildPlan.PartType}."],
                FailureStage);
        }

        Directory.CreateDirectory(artifactsDirectory);
        var partPath = Path.Combine(artifactsDirectory, $"fake_{PartType}.SLDPRT.txt");
        var stepPath = Path.Combine(artifactsDirectory, $"fake_{PartType}.STEP.txt");
        await File.WriteAllTextAsync(
            partPath,
            $"Fake SolidWorks part placeholder for {PartType}. No real CAD data is stored here.",
            cancellationToken);
        await File.WriteAllTextAsync(
            stepPath,
            $"Fake STEP placeholder for {PartType}. No real CAD export was executed.",
            cancellationToken);

        return new(
            "Completed",
            [
                Artifact($"generated-{PartType}-part", "Part", partPath, ".SLDPRT", $"Fake {PartType} part placeholder."),
                Artifact($"generated-{PartType}-step", "Step", stepPath, ".STEP", $"Fake {PartType} STEP placeholder.")
            ],
            [
                $"part_family_builder: {GetType().Name}",
                $"part_family_api_evidence: {ApiEvidence}",
                $"part_family_real_execution_supported: {SupportsRealExecution}"
            ],
            Array.Empty<string>());
    }

    protected static SolidWorksArtifact Artifact(
        string artifactId,
        string artifactType,
        string path,
        string expectedExtension,
        string description)
    {
        var fullPath = Path.GetFullPath(path);
        var fileInfo = new FileInfo(fullPath);
        return new SolidWorksArtifact(
            artifactId,
            artifactType,
            fullPath,
            expectedExtension,
            fileInfo.Exists,
            fileInfo.Exists ? fileInfo.Length : 0,
            description);
    }
}
