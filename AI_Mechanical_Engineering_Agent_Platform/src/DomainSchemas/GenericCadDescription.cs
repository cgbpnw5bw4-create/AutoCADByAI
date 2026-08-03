using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainSchemas;

public static class SketchEntityTypes
{
    public const string Line = "line";
    public const string Rectangle = "rectangle";
    public const string Circle = "circle";
    public const string Arc = "arc";
    public const string Slot = "slot";
    public const string Point = "point";
    public const string CenterLine = "centerline";
    public const string ConstructionCenterLine = "construction_centerline";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [Line, Rectangle, Circle, Arc, Slot, Point, CenterLine, ConstructionCenterLine],
        StringComparer.OrdinalIgnoreCase);
}

public static class SketchConstraintTypes
{
    public const string Coincident = "coincident";
    public const string Horizontal = "horizontal";
    public const string Vertical = "vertical";
    public const string Parallel = "parallel";
    public const string Perpendicular = "perpendicular";
    public const string Tangent = "tangent";
    public const string Concentric = "concentric";
    public const string Equal = "equal";
    public const string Distance = "distance";
    public const string Diameter = "diameter";
    public const string Radius = "radius";
    public const string Midpoint = "midpoint";
    public const string Symmetric = "symmetric";
    public const string Fixed = "fixed";
    public const string Dimensional = "dimensional";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [
            Coincident,
            Horizontal,
            Vertical,
            Parallel,
            Perpendicular,
            Tangent,
            Concentric,
            Equal,
            Distance,
            Diameter,
            Radius,
            Midpoint,
            Symmetric,
            Fixed,
            Dimensional
        ],
        StringComparer.OrdinalIgnoreCase);
}

public static class FeatureTypes
{
    public const string ExtrudeBoss = "extrude_boss";
    public const string ExtrudeCut = "extrude_cut";
    public const string RevolveBoss = "revolve_boss";
    public const string RevolveCut = "revolve_cut";
    public const string Fillet = "fillet";
    public const string Chamfer = "chamfer";
    public const string Hole = "hole";
    public const string LinearPattern = "linear_pattern";
    public const string CircularPattern = "circular_pattern";
    public const string Mirror = "mirror";

    public static IReadOnlySet<string> Supported { get; } = new HashSet<string>(
        [
            ExtrudeBoss,
            ExtrudeCut,
            RevolveBoss,
            RevolveCut,
            Fillet,
            Chamfer,
            Hole,
            LinearPattern,
            CircularPattern,
            Mirror
        ],
        StringComparer.OrdinalIgnoreCase);
}

public static class CadDescriptionFailureStages
{
    public const string InvalidModelSpec = PartFamilyFailureStages.InvalidCadModelSpec;
    public const string DuplicateSketchId = PartFamilyFailureStages.InvalidCadModelSpec;
    public const string InvalidSketchEntity = PartFamilyFailureStages.UnsupportedSketchEntity;
    public const string InvalidSketchConstraint = PartFamilyFailureStages.UnsupportedConstraint;
    public const string FeatureIdMissing = "feature_id_missing";
    public const string DuplicateFeatureId = "duplicate_feature_id";
    public const string FeatureDependencyMissing = PartFamilyFailureStages.FeatureDependencyMissing;
    public const string FeatureDependencyCycle = PartFamilyFailureStages.FeatureDependencyCycle;
    public const string FeatureExecutionOrderInvalid = PartFamilyFailureStages.InvalidFeatureOrder;
    public const string SketchDependencyMissing = PartFamilyFailureStages.SketchReferenceMissing;
    public const string UnsupportedFeatureType = PartFamilyFailureStages.UnsupportedFeatureType;
    public const string BuildPlanCompilationFailed = PartFamilyFailureStages.BuildPlanCompileFailed;

    // Stable aliases for callers that use the shorter graph terminology.
    public const string MissingDependency = FeatureDependencyMissing;
    public const string CycleDetected = FeatureDependencyCycle;
    public const string InvalidExecutionOrder = FeatureExecutionOrderInvalid;
}

public sealed record SketchDefinition
{
    [JsonConstructor]
    public SketchDefinition(
        string sketchId,
        string referencePlane,
        IReadOnlyList<SketchEntity>? entities = null,
        IReadOnlyList<SketchConstraint>? constraints = null,
        IReadOnlyDictionary<string, string>? dimensions = null,
        int? executionOrder = null)
    {
        SketchId = sketchId ?? string.Empty;
        ReferencePlane = referencePlane ?? string.Empty;
        Entities = entities?.ToArray() ?? Array.Empty<SketchEntity>();
        Constraints = constraints?.ToArray() ?? Array.Empty<SketchConstraint>();
        Dimensions = dimensions is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(dimensions, StringComparer.OrdinalIgnoreCase);
        ExecutionOrder = executionOrder;
    }

    [JsonPropertyName("sketch_id")]
    public string SketchId { get; init; }

    [JsonPropertyName("reference_plane")]
    public string ReferencePlane { get; init; }

    [JsonIgnore]
    public string Plane => ReferencePlane;

    [JsonPropertyName("entities")]
    public IReadOnlyList<SketchEntity> Entities { get; init; }

    [JsonPropertyName("constraints")]
    public IReadOnlyList<SketchConstraint> Constraints { get; init; }

    [JsonPropertyName("dimensions")]
    public IReadOnlyDictionary<string, string> Dimensions { get; init; }

    [JsonPropertyName("execution_order")]
    public int? ExecutionOrder { get; init; }
}

public sealed record SketchEntity
{
    [JsonConstructor]
    public SketchEntity(
        string entityId,
        string entityType,
        IReadOnlyDictionary<string, string>? parameters = null,
        bool construction = false,
        int? executionOrder = null)
    {
        EntityId = entityId ?? string.Empty;
        EntityType = entityType ?? string.Empty;
        Parameters = Copy(parameters);
        Construction = construction;
        ExecutionOrder = executionOrder;
    }

    [JsonPropertyName("entity_id")]
    public string EntityId { get; init; }

    [JsonPropertyName("entity_type")]
    public string EntityType { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    [JsonPropertyName("construction")]
    public bool Construction { get; init; }

    [JsonPropertyName("execution_order")]
    public int? ExecutionOrder { get; init; }

    public bool IsConstructionCenterLine =>
        EntityType.Equals(SketchEntityTypes.ConstructionCenterLine, StringComparison.OrdinalIgnoreCase) ||
        Construction &&
        (EntityType.Equals(SketchEntityTypes.CenterLine, StringComparison.OrdinalIgnoreCase) ||
         EntityType.Equals(SketchEntityTypes.Line, StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public bool IsConstruction => Construction;

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string>? source) =>
        source is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
}

public sealed record SketchConstraint
{
    [JsonConstructor]
    public SketchConstraint(
        string constraintId,
        string constraintType,
        IReadOnlyList<string>? entityIds = null,
        string? value = null,
        IReadOnlyDictionary<string, string>? parameters = null)
    {
        ConstraintId = constraintId ?? string.Empty;
        ConstraintType = constraintType ?? string.Empty;
        EntityIds = entityIds?.ToArray() ?? Array.Empty<string>();
        Value = value;
        Parameters = parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
    }

    [JsonPropertyName("constraint_id")]
    public string ConstraintId { get; init; }

    [JsonPropertyName("constraint_type")]
    public string ConstraintType { get; init; }

    [JsonPropertyName("entity_ids")]
    public IReadOnlyList<string> EntityIds { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; }
}

public sealed record FeatureDefinition
{
    [JsonConstructor]
    public FeatureDefinition(
        string featureId,
        string featureType,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyList<string>? dependencies = null,
        IReadOnlyList<string>? referencedSketches = null,
        IReadOnlyList<string>? referencedFeatures = null,
        int? executionOrder = null,
        string? targetReference = null)
    {
        FeatureId = featureId ?? string.Empty;
        FeatureType = featureType ?? string.Empty;
        Parameters = parameters is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        Dependencies = dependencies?.ToArray() ?? Array.Empty<string>();
        ReferencedSketches = referencedSketches?.ToArray() ?? Array.Empty<string>();
        ReferencedFeatures = referencedFeatures?.ToArray() ?? Array.Empty<string>();
        ExecutionOrder = executionOrder;
        TargetReference = targetReference;
    }

    public FeatureDefinition(
        string featureId,
        string featureType,
        IReadOnlyDictionary<string, string>? parameters,
        IReadOnlyList<string>? dependsOn,
        string? sketchId,
        int? executionOrder,
        string? targetReference)
        : this(
            featureId,
            featureType,
            parameters,
            dependencies: dependsOn,
            referencedSketches: string.IsNullOrWhiteSpace(sketchId) ? null : [sketchId],
            referencedFeatures: dependsOn,
            executionOrder,
            targetReference)
    {
    }

    [JsonPropertyName("feature_id")]
    public string FeatureId { get; init; }

    [JsonPropertyName("feature_type")]
    public string FeatureType { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; }

    [JsonPropertyName("referenced_sketches")]
    public IReadOnlyList<string> ReferencedSketches { get; init; }

    [JsonPropertyName("referenced_features")]
    public IReadOnlyList<string> ReferencedFeatures { get; init; }

    [JsonIgnore]
    public IReadOnlyList<string> DependsOn =>
        Dependencies.Concat(ReferencedFeatures).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    [JsonIgnore]
    public string? SketchId => ReferencedSketches.FirstOrDefault();

    [JsonPropertyName("execution_order")]
    public int? ExecutionOrder { get; init; }

    [JsonPropertyName("target_reference")]
    public string? TargetReference { get; init; }
}

public sealed record FeatureGraphValidationResult(
    bool IsValid,
    IReadOnlyList<FeatureDefinition> OrderedFeatures,
    string? FailureStage,
    IReadOnlyList<string> Issues);

public sealed class FeatureGraph
{
    private readonly IReadOnlyList<FeatureDefinition> _features;

    public FeatureGraph(IEnumerable<FeatureDefinition>? features)
    {
        _features = features?.ToArray() ?? Array.Empty<FeatureDefinition>();
    }

    public IReadOnlyList<FeatureDefinition> Features => _features;

    public FeatureGraphValidationResult Validate() => ValidateAndSort();

    public FeatureGraphValidationResult TopologicalSort() => ValidateAndSort();

    public bool TryTopologicalSort(
        out IReadOnlyList<FeatureDefinition> orderedFeatures,
        out string? failureStage,
        out IReadOnlyList<string> issues)
    {
        var result = ValidateAndSort();
        orderedFeatures = result.OrderedFeatures;
        failureStage = result.FailureStage;
        issues = result.Issues;
        return result.IsValid;
    }

    public FeatureGraphValidationResult ValidateAndSort()
    {
        var missingId = _features.FirstOrDefault(feature => string.IsNullOrWhiteSpace(feature.FeatureId));
        if (missingId is not null)
        {
            return Failed(
                CadDescriptionFailureStages.FeatureIdMissing,
                "feature_id_missing: every feature must expose a non-empty feature_id.");
        }

        var duplicateId = _features
            .GroupBy(feature => feature.FeatureId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            return Failed(
                CadDescriptionFailureStages.DuplicateFeatureId,
                $"duplicate_feature_id: {duplicateId.Key} is defined more than once.");
        }

        var indexed = _features
            .Select((feature, index) => new IndexedFeature(feature, index))
            .ToDictionary(item => item.Feature.FeatureId, StringComparer.OrdinalIgnoreCase);
        foreach (var item in indexed.Values)
        {
            foreach (var dependency in item.Feature.DependsOn)
            {
                if (!indexed.ContainsKey(dependency))
                {
                    return Failed(
                        CadDescriptionFailureStages.FeatureDependencyMissing,
                        $"feature_dependency_missing: {item.Feature.FeatureId} depends on unknown feature {dependency}.");
                }
            }
        }

        var indegree = indexed.Values.ToDictionary(
            item => item.Feature.FeatureId,
            item => item.Feature.DependsOn.Count,
            StringComparer.OrdinalIgnoreCase);
        var dependents = indexed.Values.ToDictionary(
            item => item.Feature.FeatureId,
            _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var feature in _features)
        {
            foreach (var dependency in feature.DependsOn)
            {
                dependents[dependency].Add(feature.FeatureId);
            }
        }

        var available = indexed.Values
            .Where(item => indegree[item.Feature.FeatureId] == 0)
            .OrderBy(Priority)
            .ThenBy(item => item.SourceIndex)
            .ThenBy(item => item.Feature.FeatureId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var result = new List<FeatureDefinition>(_features.Count);
        while (available.Count > 0)
        {
            var next = available[0];
            available.RemoveAt(0);
            result.Add(next.Feature);
            foreach (var dependentId in dependents[next.Feature.FeatureId])
            {
                indegree[dependentId]--;
                if (indegree[dependentId] == 0)
                {
                    available.Add(indexed[dependentId]);
                }
            }

            available = available
                .OrderBy(Priority)
                .ThenBy(item => item.SourceIndex)
                .ThenBy(item => item.Feature.FeatureId, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (result.Count != _features.Count)
        {
            var cyclicIds = indegree
                .Where(pair => pair.Value > 0)
                .Select(pair => pair.Key)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
            return Failed(
                CadDescriptionFailureStages.FeatureDependencyCycle,
                $"feature_dependency_cycle: cycle detected among {string.Join(", ", cyclicIds)}.");
        }

        var orderedValues = _features
            .Where(feature => feature.ExecutionOrder.HasValue)
            .ToArray();
        var invalidOrder = orderedValues.FirstOrDefault(feature => feature.ExecutionOrder <= 0);
        if (invalidOrder is not null)
        {
            return Failed(
                CadDescriptionFailureStages.FeatureExecutionOrderInvalid,
                $"invalid_feature_order: {invalidOrder.FeatureId} execution_order must be positive.");
        }

        var duplicateOrder = orderedValues
            .GroupBy(feature => feature.ExecutionOrder!.Value)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateOrder is not null)
        {
            return Failed(
                CadDescriptionFailureStages.FeatureExecutionOrderInvalid,
                $"invalid_feature_order: execution_order {duplicateOrder.Key} is assigned more than once.");
        }

        foreach (var feature in orderedValues)
        {
            foreach (var dependencyId in feature.DependsOn)
            {
                var dependency = indexed[dependencyId].Feature;
                if (dependency.ExecutionOrder.HasValue &&
                    dependency.ExecutionOrder.Value >= feature.ExecutionOrder!.Value)
                {
                    return Failed(
                        CadDescriptionFailureStages.FeatureExecutionOrderInvalid,
                        $"invalid_feature_order: {feature.FeatureId} must execute after dependency {dependency.FeatureId}.");
                }
            }
        }

        return new FeatureGraphValidationResult(true, result, null, Array.Empty<string>());
    }

    private static int Priority(IndexedFeature item) =>
        item.Feature.ExecutionOrder ?? int.MaxValue;

    private static FeatureGraphValidationResult Failed(string stage, params string[] issues) =>
        new(false, Array.Empty<FeatureDefinition>(), stage, issues);

    private sealed record IndexedFeature(FeatureDefinition Feature, int SourceIndex);
}

public sealed record BuildPlanCompilationResult(
    SolidWorksBuildPlan? BuildPlan,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess => BuildPlan is not null && string.IsNullOrWhiteSpace(FailureStage);
}

public class BuildPlanCompiler
{
    public static IReadOnlyDictionary<string, string> FeatureOperationMappings { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FeatureTypes.ExtrudeBoss] = "ExtrudeBoss",
            [FeatureTypes.ExtrudeCut] = "CutExtrude",
            [FeatureTypes.RevolveBoss] = "RevolveBoss",
            [FeatureTypes.RevolveCut] = "RevolveCut",
            [FeatureTypes.Fillet] = "AddFillet",
            [FeatureTypes.Chamfer] = "AddChamfer",
            [FeatureTypes.Hole] = "CreateSimpleHole",
            [FeatureTypes.LinearPattern] = "LinearPattern",
            [FeatureTypes.CircularPattern] = "CircularPattern",
            [FeatureTypes.Mirror] = "MirrorFeature"
        };

    public BuildPlanCompilationResult Compile(CADModelSpec spec) =>
        Compile(spec.ModelId, spec);

    public BuildPlanCompilationResult Compile(CADModelSpec spec, string taskId) =>
        Compile(taskId, spec);

    public BuildPlanCompilationResult Compile(string taskId, CADModelSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.ModelId) ||
            string.IsNullOrWhiteSpace(spec.ModelType) ||
            string.IsNullOrWhiteSpace(spec.Unit))
        {
            return Failed(
                CadDescriptionFailureStages.InvalidModelSpec,
                "invalid_cad_model_spec: model_id, model_type and unit are required.");
        }

        if (spec.Features.Count == 0)
        {
            return Failed(
                CadDescriptionFailureStages.InvalidModelSpec,
                "invalid_cad_model_spec: features must contain at least one FeatureDefinition.");
        }

        var sketches = spec.Sketches
            .Select((sketch, index) => new IndexedSketch(sketch, index))
            .ToArray();
        var duplicateSketch = sketches
            .GroupBy(item => item.Sketch.SketchId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateSketch is not null)
        {
            return Failed(
                CadDescriptionFailureStages.DuplicateSketchId,
                $"invalid_cad_model_spec: sketch_id {duplicateSketch.Key} must be non-empty and unique.");
        }

        var invalidSketchOrder = sketches.FirstOrDefault(item =>
            item.Sketch.ExecutionOrder.HasValue && item.Sketch.ExecutionOrder.Value <= 0);
        if (invalidSketchOrder is not null)
        {
            return Failed(
                CadDescriptionFailureStages.FeatureExecutionOrderInvalid,
                $"invalid_feature_order: sketch {invalidSketchOrder.Sketch.SketchId} execution_order must be positive.");
        }

        foreach (var sketch in sketches.Select(item => item.Sketch))
        {
            if (string.IsNullOrWhiteSpace(sketch.ReferencePlane))
            {
                return Failed(
                    CadDescriptionFailureStages.SketchDependencyMissing,
                    $"sketch_reference_missing: sketch {sketch.SketchId} has no reference_plane.");
            }

            var invalidEntity = sketch.Entities.FirstOrDefault(entity =>
                string.IsNullOrWhiteSpace(entity.EntityId) ||
                !SketchEntityTypes.Supported.Contains(entity.EntityType));
            if (invalidEntity is not null)
            {
                return Failed(
                    CadDescriptionFailureStages.InvalidSketchEntity,
                    $"unsupported_sketch_entity: sketch {sketch.SketchId} contains unsupported or unnamed entity {invalidEntity.EntityId}.");
            }

            var entityIds = sketch.Entities
                .Select(entity => entity.EntityId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var invalidConstraint = sketch.Constraints.FirstOrDefault(constraint =>
                string.IsNullOrWhiteSpace(constraint.ConstraintId) ||
                !SketchConstraintTypes.Supported.Contains(constraint.ConstraintType) ||
                constraint.EntityIds.Any(entityId => !entityIds.Contains(entityId)));
            if (invalidConstraint is not null)
            {
                return Failed(
                    CadDescriptionFailureStages.InvalidSketchConstraint,
                    $"unsupported_constraint: sketch {sketch.SketchId} constraint {invalidConstraint.ConstraintId} is unsupported or references an unknown entity.");
            }
        }

        var graphResult = new FeatureGraph(spec.Features).ValidateAndSort();
        if (!graphResult.IsValid)
        {
            return new BuildPlanCompilationResult(null, graphResult.FailureStage, graphResult.Issues);
        }

        var sketchIds = sketches
            .Select(item => item.Sketch.SketchId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var feature in graphResult.OrderedFeatures)
        {
            if (!FeatureOperationMappings.ContainsKey(feature.FeatureType))
            {
                return Failed(
                    CadDescriptionFailureStages.UnsupportedFeatureType,
                    $"unsupported_feature_type: feature {feature.FeatureId} uses unsupported type {feature.FeatureType}.");
            }

            var missingSketch = feature.ReferencedSketches.FirstOrDefault(sketchId =>
                string.IsNullOrWhiteSpace(sketchId) || !sketchIds.Contains(sketchId));
            if (missingSketch is not null)
            {
                return Failed(
                    CadDescriptionFailureStages.SketchDependencyMissing,
                    $"sketch_reference_missing: feature {feature.FeatureId} references unknown sketch {missingSketch}.");
            }
        }

        try
        {
            return CompileValidated(taskId, spec, sketches, graphResult.OrderedFeatures);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Failed(
                CadDescriptionFailureStages.BuildPlanCompilationFailed,
                $"build_plan_compile_failed: {ex.Message}");
        }
    }

    private static BuildPlanCompilationResult CompileValidated(
        string taskId,
        CADModelSpec spec,
        IReadOnlyList<IndexedSketch> sketches,
        IReadOnlyList<FeatureDefinition> orderedFeatures)
    {
        var operations = new List<SolidWorksOperation>();
        var sketchOperations = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var featureOperations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sketchesById = sketches.ToDictionary(
            item => item.Sketch.SketchId,
            item => item.Sketch,
            StringComparer.OrdinalIgnoreCase);
        string NextOperationId() => $"op-{operations.Count + 1:000}";

        IReadOnlyList<string> EmitSketch(
            SketchDefinition sketch,
            IReadOnlyList<string> upstreamDependencies)
        {
            var sketchOperationId = NextOperationId();
            var sketchParameters = new Dictionary<string, string>(
                sketch.Dimensions,
                StringComparer.OrdinalIgnoreCase);
            foreach (var entity in sketch.Entities.Where(entity => !entity.IsConstructionCenterLine))
            {
                foreach (var parameter in entity.Parameters)
                {
                    sketchParameters[parameter.Key] = parameter.Value;
                }
            }

            sketchParameters["sketch_id"] = sketch.SketchId;
            sketchParameters["reference_plane"] = sketch.ReferencePlane;
            sketchParameters["entity_count"] = sketch.Entities.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            sketchParameters["constraint_count"] = sketch.Constraints.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            sketchParameters["dimensions"] = JsonSerializer.Serialize(sketch.Dimensions);
            sketchParameters["entities"] = JsonSerializer.Serialize(sketch.Entities);
            sketchParameters["constraints"] = JsonSerializer.Serialize(sketch.Constraints);
            operations.Add(new SolidWorksOperation(
                sketchOperationId,
                "CreateSketch",
                sketch.ReferencePlane,
                sketchParameters,
                upstreamDependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                $"Sketch {sketch.SketchId} created on {sketch.ReferencePlane}."));

            var completionOperations = new List<string> { sketchOperationId };
            foreach (var centerLine in sketch.Entities
                         .Where(entity => entity.IsConstructionCenterLine)
                         .OrderBy(entity => entity.ExecutionOrder ?? int.MaxValue)
                         .ThenBy(entity => entity.EntityId, StringComparer.OrdinalIgnoreCase))
            {
                var operationId = NextOperationId();
                var parameters = new Dictionary<string, string>(centerLine.Parameters, StringComparer.OrdinalIgnoreCase)
                {
                    ["sketch_id"] = sketch.SketchId,
                    ["entity_id"] = centerLine.EntityId,
                    ["entity_type"] = centerLine.EntityType,
                    ["construction"] = "true"
                };
                operations.Add(new SolidWorksOperation(
                    operationId,
                    "CreateCenterLine",
                    sketch.ReferencePlane,
                    parameters,
                    [sketchOperationId],
                    $"Construction centerline {centerLine.EntityId} created for sketch {sketch.SketchId}."));
                completionOperations.Add(operationId);
            }

            sketchOperations[sketch.SketchId] = completionOperations;
            return completionOperations;
        }

        string? lastFeatureOperation = null;
        foreach (var feature in orderedFeatures)
        {
            var featureDependencyOperations = feature.DependsOn
                .Select(dependency => featureOperations[dependency])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var newlyEmittedSketchOperations = new List<string>();
            foreach (var sketchId in feature.ReferencedSketches
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(id => sketchesById[id].ExecutionOrder ?? int.MaxValue)
                         .ThenBy(id => id, StringComparer.OrdinalIgnoreCase))
            {
                if (!sketchOperations.TryGetValue(sketchId, out var existingOperations))
                {
                    var upstream = featureDependencyOperations.Length > 0
                        ? featureDependencyOperations
                        : string.IsNullOrWhiteSpace(lastFeatureOperation)
                            ? Array.Empty<string>()
                            : [lastFeatureOperation];
                    existingOperations = EmitSketch(sketchesById[sketchId], upstream);
                }

                newlyEmittedSketchOperations.AddRange(existingOperations);
            }

            var operationId = NextOperationId();
            var parameters = new Dictionary<string, string>(feature.Parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["feature_id"] = feature.FeatureId,
                ["feature_type"] = feature.FeatureType
            };
            if (feature.ReferencedSketches.Count > 0)
            {
                parameters["sketch_id"] = feature.ReferencedSketches[0];
                parameters["referenced_sketches"] = JsonSerializer.Serialize(feature.ReferencedSketches);
            }

            if (!string.IsNullOrWhiteSpace(feature.TargetReference))
            {
                parameters["target_reference"] = feature.TargetReference;
            }

            var dependencies = new List<string>(newlyEmittedSketchOperations);
            dependencies.AddRange(featureDependencyOperations);
            operations.Add(new SolidWorksOperation(
                operationId,
                FeatureOperationMappings[feature.FeatureType],
                feature.ReferencedSketches.Count == 0
                    ? feature.TargetReference ?? string.Empty
                    : sketchesById[feature.ReferencedSketches[0]].ReferencePlane,
                parameters,
                dependencies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                $"Feature {feature.FeatureId} compiled from {feature.FeatureType}."));
            featureOperations[feature.FeatureId] = operationId;
            lastFeatureOperation = operationId;
        }

        foreach (var item in sketches
                     .Where(item => !sketchOperations.ContainsKey(item.Sketch.SketchId))
                     .OrderBy(item => item.Sketch.ExecutionOrder ?? int.MaxValue)
                     .ThenBy(item => item.SourceIndex)
                     .ThenBy(item => item.Sketch.SketchId, StringComparer.OrdinalIgnoreCase))
        {
            EmitSketch(
                item.Sketch,
                string.IsNullOrWhiteSpace(lastFeatureOperation)
                    ? Array.Empty<string>()
                    : [lastFeatureOperation]);
        }

        var safeModelId = SafeFileName(spec.ModelId);
        var dependedOnGeometryOperations = operations
            .SelectMany(operation => operation.DependsOn)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var terminalGeometryOperations = operations
            .Select(operation => operation.OperationId)
            .Where(operationId => !dependedOnGeometryOperations.Contains(operationId))
            .ToArray();
        var saveOperationId = NextOperationId();
        operations.Add(new SolidWorksOperation(
            saveOperationId,
            "SavePart",
            string.Empty,
            new Dictionary<string, string>
            {
                ["file_name"] = $"{safeModelId}.SLDPRT"
            },
            terminalGeometryOperations,
            "SolidWorks part output saved."));
        var exportOperationId = NextOperationId();
        operations.Add(new SolidWorksOperation(
            exportOperationId,
            "ExportStep",
            string.Empty,
            new Dictionary<string, string>
            {
                ["file_name"] = $"{safeModelId}.STEP"
            },
            [saveOperationId],
            "STEP output exported."));

        var outputRequirements = spec.OutputRequirements.Count == 0
            ? new[] { "SLDPRT", "STEP", "build_report.json" }
            : spec.OutputRequirements.ToArray();
        var expectedArtifacts = new List<SolidWorksArtifact>
        {
            new(
                "expected-part",
                "Part",
                $"output/solidworks/artifacts/{safeModelId}.SLDPRT",
                ".SLDPRT",
                false,
                0,
                "Compiled SolidWorks part output."),
            new(
                "expected-step",
                "Step",
                $"output/solidworks/artifacts/{safeModelId}.STEP",
                ".STEP",
                false,
                0,
                "Compiled STEP output."),
            new(
                "expected-build-report",
                "BuildReport",
                "output/solidworks/reports/build_report.json",
                ".json",
                false,
                0,
                "Compiled build report.")
        };
        if (outputRequirements.Contains("geometry_validation_report.json", StringComparer.OrdinalIgnoreCase))
        {
            expectedArtifacts.Add(new SolidWorksArtifact(
                "expected-geometry-validation-report",
                "GeometryValidationReport",
                "output/solidworks/reports/geometry_validation_report.json",
                ".json",
                false,
                0,
                "Compiled geometry validation report."));
        }

        if (outputRequirements.Contains("rebuild_report.json", StringComparer.OrdinalIgnoreCase))
        {
            expectedArtifacts.Add(new SolidWorksArtifact(
                "expected-rebuild-report",
                "RebuildReport",
                "output/solidworks/reports/rebuild_report.json",
                ".json",
                false,
                0,
                "Compiled parameter rebuild report."));
        }

        var plan = new SolidWorksBuildPlan(
            $"solidworks-build-plan-{SafeFileName(taskId)}",
            spec.ModelId,
            "SolidWorks",
            spec.ModelType,
            spec.Unit,
            operations,
            expectedArtifacts,
            [
                "feature dependencies must form a directed acyclic graph",
                "execution_order must be positive, unique and dependency-consistent",
                "sketch and feature references must resolve before Worker execution"
            ],
            ["Compilation is descriptive only and does not connect to SolidWorks."],
            Material: spec.Material,
            Features: new Dictionary<string, string>(spec.FeatureOptions, StringComparer.OrdinalIgnoreCase),
            OutputRequirements: outputRequirements,
            DrawingRequirements: new Dictionary<string, string>(spec.DrawingRequirements, StringComparer.OrdinalIgnoreCase),
            ExecutionOptions: new Dictionary<string, string>(spec.ExecutionOptions, StringComparer.OrdinalIgnoreCase),
            Dimensions: new Dictionary<string, string>(spec.Parameters, StringComparer.OrdinalIgnoreCase),
            ExecutionStrategy: spec.RequiresFeatureHandlerPipeline
                ? SolidWorksBuildExecutionStrategies.FeatureHandlerGraph
                : SolidWorksBuildExecutionStrategies.PartFamilyBuilder,
            ReferenceGeometry: new Dictionary<string, string>(
                spec.ReferenceGeometry,
                StringComparer.OrdinalIgnoreCase));
        return new BuildPlanCompilationResult(plan, null, Array.Empty<string>());
    }

    private static BuildPlanCompilationResult Failed(string stage, params string[] issues) =>
        new(null, stage, issues);

    private static string SafeFileName(string value)
    {
        var safe = new string(value
            .Select(character =>
                char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(safe) ? "cad_model" : safe;
    }

    private sealed record IndexedSketch(SketchDefinition Sketch, int SourceIndex);
}

public sealed class GenericBuildPlanCompiler : BuildPlanCompiler;
