using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// Generic feature-graph execution boundary. RealSolidWorksWorker selects this
/// builder only from the server-owned BuildPlan execution strategy. The complete
/// graph must pass registry, schema and evidence preflight before COM connection.
/// </summary>
public sealed class SolidWorksFeatureGraphPartFamilyBuilder : SolidWorksPartFamilyBuilderBase
{
    private readonly string _partType;
    private readonly FeatureHandlerRegistry _registry;
    private readonly Func<object, ISolidWorksFeatureAdapter> _adapterFactory;
    private readonly bool _supportsRealExecution;
    private readonly Func<SolidWorksBuildPlan, ExpectedPartGeometry?>? _expectedGeometry;

    public SolidWorksFeatureGraphPartFamilyBuilder(
        string partType,
        FeatureHandlerRegistry registry,
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null,
        Func<object, ISolidWorksFeatureAdapter>? adapterFactory = null,
        bool supportsRealExecution = true,
        Func<SolidWorksBuildPlan, ExpectedPartGeometry?>? expectedGeometry = null,
        ISolidWorksGeometryReader? geometryReader = null)
        : base(comFacade, fileVerifier, planeSelector, geometryReader)
    {
        _partType = string.IsNullOrWhiteSpace(partType) ? "generic_cad_model" : partType;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _adapterFactory = adapterFactory ?? (model => new RealSolidWorksFeatureAdapter(model, Com));
        _supportsRealExecution = supportsRealExecution;
        _expectedGeometry = expectedGeometry;
    }

    /// <summary>
    /// 几何期望由零件族 Definition 提供，Worker 不持有任何零件专用几何知识。
    /// </summary>
    protected override ExpectedPartGeometry? DescribeExpectedGeometry(SolidWorksBuildPlan plan) =>
        _expectedGeometry?.Invoke(plan);

    public override string PartType => _partType;

    public override string FailureStage => PartFamilyFailureStages.FeatureApiUnverified;

    public override bool SupportsRealExecution => _supportsRealExecution;

    public override string ApiEvidence => _supportsRealExecution
        ? "feature_handler_registry_preflight_required; handler evidence is evaluated per node"
        : "part_family_production_evidence_pending; generic FeatureGraph execution remains fail-closed";

    public override string RealExecutionMode => PartFamilyExecutionModes.GenericFeatureGraph;

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var state = new FeatureHandlerExecutionState();
        using var adapter = _adapterFactory(model)
            ?? throw Failure(
                PartFamilyFailureStages.FeatureAdapterMissing,
                "Feature adapter factory returned null.");
        foreach (var operation in plan.Operations.Where(operation =>
                     !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
                     !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase)))
        {
            var adaptation = FeatureHandlerPlanAdapter.Adapt(operation);
            if (adaptation.Feature is null)
            {
                throw Failure(
                    adaptation.FailureStage ?? PartFamilyFailureStages.UnsupportedFeatureType,
                    adaptation.Issues.FirstOrDefault() ?? "Feature operation adaptation failed.");
            }

            var resolution = _registry.Resolve(adaptation.Feature);
            if (!resolution.IsSuccess)
            {
                throw Failure(
                    resolution.FailureStage ?? PartFamilyFailureStages.UnsupportedFeatureType,
                    resolution.Issues.FirstOrDefault() ?? "Feature handler resolution failed.");
            }

            var result = resolution.Handler!.ExecuteAsync(
                new FeatureHandlerExecutionContext(
                        adapter,
                        adaptation.Feature,
                        operation,
                        state))
                .GetAwaiter()
                .GetResult();
            logs.AddRange(result.Logs);
            diagnostics.FeatureHandlerReports.Add(
                resolution.Handler.GenerateReport(adaptation.Feature, result));
            if (!result.IsSuccess)
            {
                throw Failure(
                    result.FailureStage ?? PartFamilyFailureStages.FeatureResultInvalid,
                    result.Issues.FirstOrDefault() ?? "Feature handler execution failed.");
            }

            if (result.CreatedObject is not null)
            {
                state.Set(operation.OperationId, result.CreatedObject);
            }

            diagnostics.OperationsExecuted.Add(
                $"feature_handler_success:{adaptation.Feature.FeatureId}:{adaptation.Feature.FeatureType}");
        }
    }
}
