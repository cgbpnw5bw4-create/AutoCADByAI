using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// V2.0-D generic FeatureGraph execution pipeline. This is an orchestration
/// boundary only: every node still resolves through FeatureHandlerRegistry and
/// calls an IFeatureHandler, which keeps COM isolated in the existing adapter.
/// </summary>
public sealed class FeatureExecutionPipeline
{
    private readonly FeatureHandlerRegistry _registry;
    private readonly Func<object, ISolidWorksFeatureAdapter> _adapterFactory;

    public FeatureExecutionPipeline(
        FeatureHandlerRegistry registry,
        Func<object, ISolidWorksFeatureAdapter>? adapterFactory = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _adapterFactory = adapterFactory ?? throw new ArgumentNullException(nameof(adapterFactory));
    }

    public FeatureExecutionPipelineResult Execute(
        object model,
        SolidWorksBuildPlan plan,
        SolidWorksPartFamilyBuildDiagnostics diagnostics,
        List<string> logs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(logs);

        var state = new FeatureHandlerExecutionState();
        var reports = new List<FeatureHandlerReport>();
        using var adapter = _adapterFactory(model) ?? throw new InvalidOperationException(
            $"{PartFamilyFailureStages.FeatureAdapterMissing}: Feature adapter factory returned null.");

        foreach (var operation in plan.Operations.Where(operation =>
                     !operation.OperationType.Equals("SavePart", StringComparison.OrdinalIgnoreCase) &&
                     !operation.OperationType.Equals("ExportStep", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var adaptation = FeatureHandlerPlanAdapter.Adapt(operation);
            if (adaptation.Feature is null)
            {
                return Failed(
                    adaptation.FailureStage ?? PartFamilyFailureStages.UnsupportedFeatureType,
                    adaptation.Issues,
                    reports);
            }

            var resolution = _registry.Resolve(adaptation.Feature);
            if (!resolution.IsSuccess)
            {
                return Failed(
                    resolution.FailureStage ?? PartFamilyFailureStages.UnsupportedFeatureType,
                    resolution.Issues,
                    reports);
            }

            var result = resolution.Handler!.ExecuteAsync(
                    new FeatureHandlerExecutionContext(adapter, adaptation.Feature, operation, state),
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            logs.AddRange(result.Logs);
            var report = resolution.Handler.GenerateReport(adaptation.Feature, result);
            reports.Add(report);
            diagnostics.FeatureHandlerReports.Add(report);
            if (!result.IsSuccess)
            {
                return Failed(
                    result.FailureStage ?? PartFamilyFailureStages.FeatureResultInvalid,
                    result.Issues,
                    reports);
            }

            if (result.CreatedObject is not null)
            {
                state.Set(operation.OperationId, result.CreatedObject);
            }

            diagnostics.OperationsExecuted.Add(
                $"feature_execution_pipeline_success:{adaptation.Feature.FeatureId}:{adaptation.Feature.FeatureType}");
        }

        return new FeatureExecutionPipelineResult(true, null, Array.Empty<string>(), reports);
    }

    private static FeatureExecutionPipelineResult Failed(
        string failureStage,
        IReadOnlyList<string> issues,
        IReadOnlyList<FeatureHandlerReport> reports) =>
        new(false, failureStage, issues, reports);
}

public sealed record FeatureExecutionPipelineResult(
    bool IsSuccess,
    string? FailureStage,
    IReadOnlyList<string> Issues,
    IReadOnlyList<FeatureHandlerReport> Reports);
