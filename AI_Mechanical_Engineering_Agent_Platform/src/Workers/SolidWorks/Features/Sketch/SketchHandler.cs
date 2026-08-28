using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker.Features.Sketch;

public sealed class SketchHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureHandlerTypes.Sketch;

    public override string OperationType => "CreateSketch";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("reference_plane", "reference", true, "精确基准面或平面引用。"),
        new("entities", "SketchEntity[] JSON", true, "草图实体集合。"),
        new("constraints", "SketchConstraint[] JSON", false, "草图约束集合。"),
        new("dimensions", "dictionary JSON", false, "驱动尺寸集合。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "ISketchManager.InsertSketch / CreateLine / CreateCircle / CreateCenterRectangle",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "InsertSketch(UpdateEditRebuild)",
            "CreateLine(X1,Y1,Z1,X2,Y2,Z2)",
            "CreateCircle(Xc,Yc,Zc,Xp,Yp,Zp)",
            "CreateCenterRectangle(Xc,Yc,Zc,Xp,Yp,Zp)"
        ],
        "Sketch entity object or null; InsertSketch has no feature-success contract.",
        [
            "An exact reference plane or face is selected.",
            "Coordinates are expressed in metres.",
            "The requested entity and constraint subset has evidence for the same parameter profile."
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "TopFace and arbitrary reference resolution are not implemented.",
            "Arc, slot and constraint application lack handler-level evidence.",
            "A non-null entity alone does not prove a valid closed profile."
        ],
        [
            "V1.9 part-family diagnostics exercised selected subsets only.",
            "V2.0-C diagnostic verified TopPlane line, center rectangle and circle creation with non-null geometry and successful rebuild.",
            "Diagnostic SLDPRT and STEP identity is recorded in the bound diagnostic report; a per-run hash is deliberately not restated here because it cannot be known before the run that this claim is hashed into."
        ],
        EvidenceId: "v2.1-a-20260828-014622-sketch",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "standard_plane_top;line+center_rectangle+circle;empty_constraints;empty_dimensions;millimetres",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/20260828_014622_5303003/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:533b14e951348372edee939de64e411663a94fbd2dc49ac60c18ee63c298cd54");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(SketchHandler)}.");
        }

        if (string.IsNullOrWhiteSpace(feature.TargetReference))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.SketchReferenceMissing,
                $"{PartFamilyFailureStages.SketchReferenceMissing}: sketch {feature.FeatureId} has no reference plane.");
        }

        var unknownParameters = RejectUnknownParameters(
            feature,
            "sketch_id",
            "reference_plane",
            "entity_count",
            "constraint_count",
            "entities",
            "constraints",
            "dimensions",
            "x1_mm",
            "y1_mm",
            "x2_mm",
            "y2_mm",
            "center_x_mm",
            "center_y_mm",
            "length_mm",
            "width_mm",
            "height_mm",
            "radius_mm",
            "diameter_mm");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!feature.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: sketch {feature.FeatureId} has no serialized entities schema.");
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
            var supported = new HashSet<string>(
                [SketchEntityTypes.Line, SketchEntityTypes.Rectangle, SketchEntityTypes.Circle],
                StringComparer.OrdinalIgnoreCase);
            if (entities.Length == 0 ||
                entities.Any(entity => !supported.Contains(entity.EntityType)))
            {
                return FeatureHandlerValidationResult.Failed(
                    PartFamilyFailureStages.UnsupportedSketchEntity,
                    $"{PartFamilyFailureStages.UnsupportedSketchEntity}: sketch {feature.FeatureId} contains no supported entity.");
            }
        }
        catch (JsonException ex)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: sketch {feature.FeatureId} entities JSON is invalid: {ex.Message}");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(
        FeatureDefinition feature)
    {
        var validation = Validate(feature);
        if (!validation.IsValid)
        {
            return validation;
        }

        if (!string.Equals(
                feature.TargetReference,
                "TopPlane",
                StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceProfileBlocked(
                feature,
                $"reference_plane={feature.TargetReference ?? "missing"} is outside evidence profile {ApiEvidence.ParameterProfile}.");
        }

        if (!IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("constraints")) ||
            !IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("dimensions")))
        {
            return EvidenceProfileBlocked(
                feature,
                $"constraints or dimensions are outside evidence profile {ApiEvidence.ParameterProfile}.");
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(
                feature.Parameters.GetValueOrDefault("entities") ?? "[]") ?? [];
            foreach (var entity in entities)
            {
                var allowed = entity.EntityType.ToLowerInvariant() switch
                {
                    SketchEntityTypes.Line =>
                        new[] { "x1_mm", "y1_mm", "x2_mm", "y2_mm" },
                    SketchEntityTypes.Rectangle =>
                        new[] { "center_x_mm", "center_y_mm", "length_mm", "width_mm", "height_mm" },
                    SketchEntityTypes.Circle =>
                        new[] { "center_x_mm", "center_y_mm", "radius_mm", "diameter_mm" },
                    _ => Array.Empty<string>()
                };
                var unknown = entity.Parameters.Keys
                    .Where(parameter => !allowed.Contains(parameter, StringComparer.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (unknown.Length > 0)
                {
                    return EvidenceProfileBlocked(
                        feature,
                        $"entity {entity.EntityId} contains parameters outside evidence profile: " +
                        $"{string.Join(", ", unknown)}.");
                }
            }
        }
        catch (JsonException ex)
        {
            return EvidenceProfileBlocked(feature, $"entities JSON is invalid: {ex.Message}");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.Adapter is null
            ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteSketchAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }

    private static bool IsEmptyJsonCollection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength() == 0,
                JsonValueKind.Object => !document.RootElement.EnumerateObject().Any(),
                JsonValueKind.Null => true,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
