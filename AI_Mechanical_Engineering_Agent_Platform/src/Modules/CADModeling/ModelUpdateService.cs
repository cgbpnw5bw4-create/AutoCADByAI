using System.Globalization;
using DomainSchemas;

namespace PlatformCore.Modules.CADModeling;

/// <summary>
/// Pure parameter-update boundary. It rewrites only values owned by an existing
/// FeatureGraph, recompiles it, and never opens a CAD document or touches COM.
/// </summary>
public sealed class ModelUpdateService
{
    public ModelUpdatePreparationResult Prepare(
        string taskId,
        CADModelSpec current,
        ModelParameterUpdateRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(request);

        var oldParameters = Copy(current.Parameters);
        var issues = ValidateBaseline(oldParameters, request.OldParameters);
        if (issues.Count > 0)
        {
            return Failed(oldParameters, oldParameters, issues);
        }

        var newParameters = Copy(oldParameters);
        foreach (var item in request.NewParameters ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Value))
            {
                issues.Add("invalid_cad_model_spec: parameter update contains an empty name or value.");
                continue;
            }

            if (!newParameters.ContainsKey(item.Key))
            {
                issues.Add($"invalid_cad_model_spec: parameter update cannot introduce {item.Key}.");
                continue;
            }

            newParameters[item.Key] = item.Value;
        }

        if (issues.Count > 0)
        {
            return Failed(oldParameters, newParameters, issues);
        }

        var changedParameterNames = newParameters
            .Where(item => !oldParameters.TryGetValue(item.Key, out var oldValue) ||
                           !string.Equals(oldValue, item.Value, StringComparison.Ordinal))
            .Select(item => item.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (changedParameterNames.Length == 0)
        {
            issues.Add("invalid_cad_model_spec: parameter update does not change any value.");
            return Failed(oldParameters, newParameters, issues);
        }

        var changedParameters = changedParameterNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rewritten = RewriteGraph(current, newParameters, changedParameters, out var changedSketches, out var directlyChangedFeatures);
        var changedFeatures = ResolveChangedFeatureClosure(rewritten.Features, changedSketches, directlyChangedFeatures);
        var updated = rewritten with
        {
            Parameters = newParameters,
            RequiresFeatureHandlerPipeline = rewritten.Sketches.Count > 0 && rewritten.Features.Count > 0
        };

        var validation = new CADModelSpecValidator().Validate(updated);
        if (!validation.IsValid)
        {
            return new ModelUpdatePreparationResult(
                oldParameters,
                newParameters,
                changedParameterNames,
                changedFeatures,
                updated,
                null,
                FeatureGraphPreserved(current, updated),
                validation.FailureStage,
                validation.Issues);
        }

        var compilation = new BuildPlanCompiler().Compile(taskId, updated);
        return new ModelUpdatePreparationResult(
            oldParameters,
            newParameters,
            changedParameterNames,
            changedFeatures,
            updated,
            compilation.BuildPlan,
            FeatureGraphPreserved(current, updated),
            compilation.FailureStage,
            compilation.Issues);
    }

    public static bool FeatureGraphPreserved(CADModelSpec before, CADModelSpec after)
    {
        if (before.Sketches.Count != after.Sketches.Count || before.Features.Count != after.Features.Count)
        {
            return false;
        }

        var sketchesPreserved = before.Sketches.Zip(after.Sketches).All(pair =>
            string.Equals(pair.First.SketchId, pair.Second.SketchId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pair.First.ReferencePlane, pair.Second.ReferencePlane, StringComparison.OrdinalIgnoreCase) &&
            pair.First.ExecutionOrder == pair.Second.ExecutionOrder &&
            pair.First.Entities.Select(entity => entity.EntityId).SequenceEqual(
                pair.Second.Entities.Select(entity => entity.EntityId),
                StringComparer.OrdinalIgnoreCase));
        var featuresPreserved = before.Features.Zip(after.Features).All(pair =>
            string.Equals(pair.First.FeatureId, pair.Second.FeatureId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(pair.First.FeatureType, pair.Second.FeatureType, StringComparison.OrdinalIgnoreCase) &&
            pair.First.ExecutionOrder == pair.Second.ExecutionOrder &&
            pair.First.DependsOn.SequenceEqual(pair.Second.DependsOn, StringComparer.OrdinalIgnoreCase) &&
            pair.First.ReferencedSketches.SequenceEqual(pair.Second.ReferencedSketches, StringComparer.OrdinalIgnoreCase) &&
            pair.First.ReferencedFeatures.SequenceEqual(pair.Second.ReferencedFeatures, StringComparer.OrdinalIgnoreCase));
        return sketchesPreserved && featuresPreserved;
    }

    private static CADModelSpec RewriteGraph(
        CADModelSpec current,
        IReadOnlyDictionary<string, string> newParameters,
        IReadOnlySet<string> changedParameters,
        out IReadOnlySet<string> changedSketches,
        out IReadOnlySet<string> directlyChangedFeatures)
    {
        var changedSketchIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changedFeatureIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var length = Number(newParameters, "length_mm");
        var width = Number(newParameters, "width_mm");
        var thickness = Number(newParameters, "thickness_mm");
        var diameter = Number(newParameters, "hole_diameter_mm") ?? Number(newParameters, "diameter_mm");
        var margin = 20d;

        var sketches = current.Sketches.Select(sketch =>
        {
            var entities = sketch.Entities.Select((entity, index) =>
            {
                var parameters = Copy(entity.Parameters);
                var entityChanged = BindEntity(
                    sketch.SketchId,
                    entity,
                    index,
                    parameters,
                    changedParameters,
                    length,
                    width,
                    diameter,
                    margin);
                if (entityChanged)
                {
                    changedSketchIds.Add(sketch.SketchId);
                }

                return entity with { Parameters = parameters };
            }).ToArray();
            return sketch with { Entities = entities };
        }).ToArray();

        var features = current.Features.Select(feature =>
        {
            var parameters = Copy(feature.Parameters);
            var featureChanged = BindFeature(
                feature,
                parameters,
                changedParameters,
                thickness,
                diameter);
            if (featureChanged)
            {
                changedFeatureIds.Add(feature.FeatureId);
            }

            return feature with { Parameters = parameters };
        }).ToArray();

        changedSketches = changedSketchIds;
        directlyChangedFeatures = changedFeatureIds;
        return current with { Sketches = sketches, Features = features };
    }

    private static bool BindEntity(
        string sketchId,
        SketchEntity entity,
        int index,
        IDictionary<string, string> values,
        IReadOnlySet<string> changedParameters,
        double? length,
        double? width,
        double? diameter,
        double margin)
    {
        var changed = false;
        if (sketchId.Equals("plate_profile", StringComparison.OrdinalIgnoreCase) &&
            entity.EntityType.Equals(SketchEntityTypes.Rectangle, StringComparison.OrdinalIgnoreCase))
        {
            changed |= SetNumber(values, "length_mm", length, changedParameters.Contains("length_mm"));
            changed |= SetNumber(values, "width_mm", width, changedParameters.Contains("width_mm"));
            changed |= SetNumber(values, "height_mm", width, changedParameters.Contains("width_mm"));
        }

        if (entity.EntityType.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase) &&
            (sketchId.Equals("cut_profile", StringComparison.OrdinalIgnoreCase) ||
             sketchId.Equals("hole_profile", StringComparison.OrdinalIgnoreCase)))
        {
            changed |= SetNumber(values, "radius_mm", diameter / 2d, changedParameters.Contains("hole_diameter_mm") || changedParameters.Contains("diameter_mm"));
            changed |= SetNumber(values, "diameter_mm", diameter, changedParameters.Contains("hole_diameter_mm") || changedParameters.Contains("diameter_mm"));
            var location = ResolvePlateHoleLocation(sketchId, index, length, width, margin);
            changed |= SetNumber(values, "center_x_mm", location.X, changedParameters.Contains("length_mm"));
            changed |= SetNumber(values, "center_y_mm", location.Y, changedParameters.Contains("width_mm"));
        }

        return changed;
    }

    private static bool BindFeature(
        FeatureDefinition feature,
        IDictionary<string, string> values,
        IReadOnlySet<string> changedParameters,
        double? thickness,
        double? diameter)
    {
        var changed = false;
        if (feature.FeatureType.Equals(FeatureTypes.ExtrudeBoss, StringComparison.OrdinalIgnoreCase))
        {
            changed |= SetNumber(values, "depth_mm", thickness, changedParameters.Contains("thickness_mm"));
        }
        else if (feature.FeatureType.Equals(FeatureTypes.ExtrudeCut, StringComparison.OrdinalIgnoreCase) ||
                 feature.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase))
        {
            changed |= SetNumber(values, "depth_mm", thickness is null ? null : thickness.Value * 2d, changedParameters.Contains("thickness_mm"));
            changed |= SetNumber(values, "diameter_mm", diameter, changedParameters.Contains("hole_diameter_mm") || changedParameters.Contains("diameter_mm"));
            changed |= SetNumber(values, "hole_diameter_mm", diameter, changedParameters.Contains("hole_diameter_mm") || changedParameters.Contains("diameter_mm"));
        }

        return changed;
    }

    private static (double? X, double? Y) ResolvePlateHoleLocation(
        string sketchId,
        int entityIndex,
        double? length,
        double? width,
        double margin)
    {
        if (length is null || width is null)
        {
            return (null, null);
        }

        var x = length.Value / 2d - margin;
        var y = width.Value / 2d - margin;
        return sketchId.Equals("hole_profile", StringComparison.OrdinalIgnoreCase)
            ? (x, y)
            : entityIndex switch
            {
                0 => (-x, -y),
                1 => (x, -y),
                _ => (-x, y)
            };
    }

    private static IReadOnlyList<string> ResolveChangedFeatureClosure(
        IReadOnlyList<FeatureDefinition> features,
        IReadOnlySet<string> changedSketches,
        IReadOnlySet<string> directlyChangedFeatures)
    {
        var changed = new HashSet<string>(directlyChangedFeatures, StringComparer.OrdinalIgnoreCase);
        foreach (var feature in features.Where(feature => feature.ReferencedSketches.Any(changedSketches.Contains)))
        {
            changed.Add(feature.FeatureId);
        }

        var expanded = true;
        while (expanded)
        {
            expanded = false;
            foreach (var feature in features.Where(feature => feature.DependsOn.Any(changed.Contains)))
            {
                expanded |= changed.Add(feature.FeatureId);
            }
        }

        return changed.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static List<string> ValidateBaseline(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string>? suppliedOldParameters)
    {
        var issues = new List<string>();
        if (suppliedOldParameters is null)
        {
            return issues;
        }

        foreach (var item in suppliedOldParameters)
        {
            if (!baseline.TryGetValue(item.Key, out var actual) ||
                !string.Equals(actual, item.Value, StringComparison.Ordinal))
            {
                issues.Add($"invalid_cad_model_spec: old_parameters[{item.Key}] does not match the explicit baseline CADModelSpec.");
            }
        }

        return issues;
    }

    private static ModelUpdatePreparationResult Failed(
        IReadOnlyDictionary<string, string> oldParameters,
        IReadOnlyDictionary<string, string> newParameters,
        IReadOnlyList<string> issues) =>
        new(
            oldParameters,
            newParameters,
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            null,
            false,
            PartFamilyFailureStages.InvalidCadModelSpec,
            issues);

    private static bool SetNumber(IDictionary<string, string> values, string name, double? value, bool shouldSet)
    {
        if (!shouldSet || value is null || !values.ContainsKey(name))
        {
            return false;
        }

        var formatted = value.Value.ToString("0.###############", CultureInfo.InvariantCulture);
        if (string.Equals(values[name], formatted, StringComparison.Ordinal))
        {
            return false;
        }

        values[name] = formatted;
        return true;
    }

    private static double? Number(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value)
            ? value
            : null;

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> values) =>
        new(values, StringComparer.OrdinalIgnoreCase);
}

public sealed record ModelParameterUpdateRequest(
    IReadOnlyDictionary<string, string> NewParameters,
    IReadOnlyDictionary<string, string>? OldParameters = null);

public sealed record ModelUpdatePreparationResult(
    IReadOnlyDictionary<string, string> OldParameters,
    IReadOnlyDictionary<string, string> NewParameters,
    IReadOnlyList<string> ChangedParameters,
    IReadOnlyList<string> ChangedFeatures,
    CADModelSpec? UpdatedModelSpec,
    SolidWorksBuildPlan? BuildPlan,
    bool FeatureGraphPreserved,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess =>
        UpdatedModelSpec is not null &&
        BuildPlan is not null &&
        string.IsNullOrWhiteSpace(FailureStage) &&
        Issues.Count == 0;
}
