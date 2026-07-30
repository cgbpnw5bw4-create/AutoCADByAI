using DomainSchemas;
using SolidWorksWorker.Features.Cut;
using SolidWorksWorker.Features.Extrude;
using SolidWorksWorker.Features.Hole;
using SolidWorksWorker.Features.Revolve;
using SolidWorksWorker.Features.Sketch;

namespace SolidWorksWorker.Features;

public sealed record FeatureHandlerResolution(
    IFeatureHandler? Handler,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess => Handler is not null && string.IsNullOrWhiteSpace(FailureStage);
}

public sealed record FeatureHandlerGraphPreflightResult(
    bool IsPassed,
    string? FailureStage,
    IReadOnlyList<string> Issues,
    IReadOnlyList<FeatureDefinition> Features);

public sealed class FeatureHandlerRegistry
{
    private static readonly IReadOnlySet<string> NonFeatureOperations =
        new HashSet<string>(["SavePart", "ExportStep"], StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, IFeatureHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public FeatureHandlerRegistry(IEnumerable<IFeatureHandler>? handlers = null)
    {
        foreach (var handler in handlers ?? Array.Empty<IFeatureHandler>())
        {
            Register(handler);
        }
    }

    public static FeatureHandlerRegistry CreateDefault() =>
        new(
        [
            new SketchHandler(),
            new ExtrudeBossHandler(),
            new ExtrudeCutHandler(),
            new HoleHandler(),
            new RevolveBossHandler()
        ]);

    public void Register(IFeatureHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.FeatureType) ||
            !_handlers.TryAdd(handler.FeatureType, handler))
        {
            throw new InvalidOperationException(
                $"A feature handler is already registered for {handler.FeatureType}.");
        }
    }

    public FeatureHandlerResolution Resolve(FeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (_handlers.TryGetValue(feature.FeatureType, out var handler) &&
            handler.CanHandle(feature))
        {
            return new(handler, null, Array.Empty<string>());
        }

        return new(
            null,
            PartFamilyFailureStages.UnsupportedFeatureType,
            [$"{PartFamilyFailureStages.UnsupportedFeatureType}: no handler is registered for {feature.FeatureType}."]);
    }

    public bool TryGetHandler(string featureType, out IFeatureHandler handler) =>
        _handlers.TryGetValue(featureType, out handler!);

    public IReadOnlyList<IFeatureHandler> GetAll() =>
        _handlers.Values.OrderBy(handler => handler.FeatureType, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Validates the complete graph before any COM connection or Handler execution.
    /// A single unknown, invalid or unverified node rejects the entire graph.
    /// </summary>
    public FeatureHandlerGraphPreflightResult ValidateForRealExecution(SolidWorksBuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var issues = new List<string>();
        var features = new List<FeatureDefinition>();
        string? firstFailureStage = null;

        foreach (var operation in plan.Operations.Where(operation =>
                     !NonFeatureOperations.Contains(operation.OperationType)))
        {
            var adaptation = FeatureHandlerPlanAdapter.Adapt(operation);
            if (adaptation.Feature is null)
            {
                firstFailureStage ??= adaptation.FailureStage;
                issues.AddRange(adaptation.Issues);
                continue;
            }

            features.Add(adaptation.Feature);
            var resolution = Resolve(adaptation.Feature);
            if (!resolution.IsSuccess)
            {
                firstFailureStage ??= resolution.FailureStage;
                issues.AddRange(resolution.Issues);
                continue;
            }

            var validation = resolution.Handler!.Validate(adaptation.Feature);
            if (!validation.IsValid)
            {
                firstFailureStage ??= validation.FailureStage;
                issues.AddRange(validation.Issues);
            }

            if (!resolution.Handler.ApiEvidence.AllowsRealExecution)
            {
                firstFailureStage ??= PartFamilyFailureStages.FeatureApiEvidenceInsufficient;
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiEvidenceInsufficient}: " +
                    $"{adaptation.Feature.FeatureId}/{adaptation.Feature.FeatureType} " +
                    $"has api_evidence_status = {resolution.Handler.ApiEvidence.Status}.");
            }
        }

        return new(
            issues.Count == 0,
            firstFailureStage,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            features);
    }
}

public sealed record FeatureHandlerPlanAdaptation(
    FeatureDefinition? Feature,
    string? FailureStage,
    IReadOnlyList<string> Issues);

public static class FeatureHandlerPlanAdapter
{
    private static readonly IReadOnlyDictionary<string, string> OperationTypeMappings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CreateSketch"] = FeatureHandlerTypes.Sketch,
            ["CreateCenterLine"] = FeatureHandlerTypes.Sketch,
            ["ExtrudeBoss"] = FeatureTypes.ExtrudeBoss,
            ["CutExtrude"] = FeatureTypes.ExtrudeCut,
            ["AddHoleWizardHole"] = FeatureTypes.Hole,
            ["RevolveBoss"] = FeatureTypes.RevolveBoss
        };

    public static FeatureHandlerPlanAdaptation Adapt(SolidWorksOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var featureType = operation.Parameters.GetValueOrDefault("feature_type");
        if (string.IsNullOrWhiteSpace(featureType) &&
            !OperationTypeMappings.TryGetValue(operation.OperationType, out featureType))
        {
            return new(
                null,
                PartFamilyFailureStages.UnsupportedFeatureType,
                [$"{PartFamilyFailureStages.UnsupportedFeatureType}: operation {operation.OperationId}/{operation.OperationType} has no registered feature mapping."]);
        }

        var featureId =
            operation.Parameters.GetValueOrDefault("feature_id") ??
            operation.Parameters.GetValueOrDefault("sketch_id") ??
            operation.OperationId;
        return new(
            new FeatureDefinition(
                featureId,
                featureType!,
                operation.Parameters,
                dependencies: operation.DependsOn,
                referencedSketches: operation.Parameters.TryGetValue("sketch_id", out var sketchId)
                    ? [sketchId]
                    : [],
                referencedFeatures: [],
                targetReference: operation.SketchPlane),
            null,
            Array.Empty<string>());
    }
}
