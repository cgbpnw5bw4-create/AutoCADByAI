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

    public SolidWorksFeatureGraphPartFamilyBuilder(
        string partType,
        FeatureHandlerRegistry registry,
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPartFamilyPlaneSelector? planeSelector = null)
        : base(comFacade, fileVerifier, planeSelector)
    {
        _partType = string.IsNullOrWhiteSpace(partType) ? "generic_cad_model" : partType;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public override string PartType => _partType;

    public override string FailureStage => PartFamilyFailureStages.FeatureApiEvidenceInsufficient;

    public override bool SupportsRealExecution => true;

    public override string ApiEvidence =>
        "feature_handler_registry_preflight_required; handler evidence is evaluated per node";

    public override string RealExecutionMode => "RealBuildGenericFeatureGraph";

    protected override void BuildFeatures(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs)
    {
        var state = new FeatureHandlerExecutionState();
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
                        model,
                        Com,
                        adaptation.Feature,
                        operation,
                        state))
                .GetAwaiter()
                .GetResult();
            logs.AddRange(result.Logs);
            if (!result.IsSuccess)
            {
                throw Failure(
                    result.FailureStage ?? PartFamilyFailureStages.FeatureApiEvidenceInsufficient,
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
