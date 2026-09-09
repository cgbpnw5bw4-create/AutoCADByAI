using System.Globalization;
using System.Text.Json;
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
            new RevolveBossHandler(),
            new Fillet.FilletHandler(),
            new Chamfer.ChamferHandler(),
            new Pattern.LinearPatternHandler(),
            new Pattern.CircularPatternHandler(),
            new Mirror.MirrorHandler()
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
        var adaptedByOperationId = new Dictionary<string, FeatureDefinition>(
            StringComparer.OrdinalIgnoreCase);
        string? firstFailureStage = null;

        var holeValidation = HolePlanValidation.Validate(plan);
        if (holeValidation is not null)
            return new(false, holeValidation.FailureStage, holeValidation.Issues, []);

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
            adaptedByOperationId[operation.OperationId] = adaptation.Feature;
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

            var evidenceValidation =
                resolution.Handler.ValidateEvidenceForRealExecution(adaptation.Feature);
            if (!evidenceValidation.IsValid)
            {
                firstFailureStage ??= evidenceValidation.FailureStage;
                issues.AddRange(evidenceValidation.Issues);
            }
        }

        ValidateGraphEvidenceProfiles(
            plan,
            adaptedByOperationId,
            issues,
            ref firstFailureStage);

        return new(
            issues.Count == 0,
            firstFailureStage,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            features);
    }

    public FeatureHandlerGraphPreflightResult ValidateRuntimeForRealExecution(
        SolidWorksBuildPlan plan,
        string? actualSolidWorksVersion)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var features = new List<FeatureDefinition>();
        var issues = new List<string>();
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

            var validation = resolution.Handler!.ValidateRuntimeForRealExecution(
                actualSolidWorksVersion);
            if (!validation.IsValid)
            {
                firstFailureStage ??= validation.FailureStage;
                issues.AddRange(validation.Issues);
            }
        }

        return new(
            issues.Count == 0,
            firstFailureStage,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            features);
    }

    private static void ValidateGraphEvidenceProfiles(
        SolidWorksBuildPlan plan,
        IReadOnlyDictionary<string, FeatureDefinition> adaptedByOperationId,
        List<string> issues,
        ref string? firstFailureStage)
    {
        foreach (var operation in plan.Operations)
        {
            if (!adaptedByOperationId.TryGetValue(operation.OperationId, out var feature) ||
                !feature.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sketchId = feature.Parameters.GetValueOrDefault("sketch_id");
            var sketchEntry = adaptedByOperationId
                .FirstOrDefault(entry =>
                    entry.Value.FeatureType.Equals(
                        FeatureHandlerTypes.Sketch,
                        StringComparison.OrdinalIgnoreCase) &&
                    entry.Value.FeatureId.Equals(sketchId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(sketchId) ||
                string.IsNullOrWhiteSpace(sketchEntry.Key) ||
                !operation.DependsOn.Contains(
                    sketchEntry.Key,
                    StringComparer.OrdinalIgnoreCase))
            {
                AddGraphIssue(
                    feature,
                    "hole must directly depend on its declared sketch_id.",
                    issues,
                    ref firstFailureStage);
                continue;
            }

            if (!TryReadSingleCircleDiameter(sketchEntry.Value, out var sketchDiameter) ||
                !TryReadHoleDiameter(feature, out var requestedDiameter) ||
                Math.Abs(sketchDiameter - requestedDiameter) > 1e-6)
            {
                AddGraphIssue(
                    feature,
                    "diameter_matches_single_circle requires one dependency circle whose diameter equals the requested hole diameter.",
                    issues,
                    ref firstFailureStage);
            }
        }
    }

    private static void AddGraphIssue(
        FeatureDefinition feature,
        string message,
        List<string> issues,
        ref string? firstFailureStage)
    {
        firstFailureStage ??= PartFamilyFailureStages.FeatureApiUnverified;
        issues.Add(
            $"{PartFamilyFailureStages.FeatureApiUnverified}: " +
            $"{feature.FeatureId}/{feature.FeatureType}: {message}");
    }

    private static bool TryReadSingleCircleDiameter(
        FeatureDefinition sketch,
        out double diameter)
    {
        diameter = 0d;
        if (!sketch.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            return false;
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
            if (entities.Length != 1 ||
                !entities[0].EntityType.Equals(
                    SketchEntityTypes.Circle,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var parameters = entities[0].Parameters;
            if (parameters.TryGetValue("radius_mm", out var radiusText) &&
                double.TryParse(
                    radiusText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var radius) &&
                double.IsFinite(radius) &&
                radius > 0)
            {
                diameter = radius * 2d;
                return true;
            }

            return parameters.TryGetValue("diameter_mm", out var diameterText) &&
                   double.TryParse(
                       diameterText,
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out diameter) &&
                   double.IsFinite(diameter) &&
                   diameter > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadHoleDiameter(
        FeatureDefinition feature,
        out double diameter)
    {
        var text =
            feature.Parameters.GetValueOrDefault("hole_diameter_mm") ??
            feature.Parameters.GetValueOrDefault("diameter_mm");
        return double.TryParse(
                   text,
                   NumberStyles.Float,
                   CultureInfo.InvariantCulture,
                   out diameter) &&
               double.IsFinite(diameter) &&
               diameter > 0;
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
            ["CreateSimpleHole"] = FeatureTypes.Hole,
            ["CreateHole"] = FeatureTypes.Hole,
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
