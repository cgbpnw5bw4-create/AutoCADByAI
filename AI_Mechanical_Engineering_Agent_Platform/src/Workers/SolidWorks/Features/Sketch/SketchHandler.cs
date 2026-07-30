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
        FeatureApiEvidenceStatuses.Unverified,
        [
            "TopFace and arbitrary reference resolution are not implemented.",
            "Arc, slot and constraint application lack handler-level evidence.",
            "A non-null entity alone does not prove a valid closed profile."
        ],
        [
            "V1.9 part-family diagnostics exercised selected subsets only.",
            "No handler-specific diagnostic is bound to the current implementation."
        ]);

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

        if (!feature.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: sketch {feature.FeatureId} has no serialized entities schema.");
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
            if (entities.Length == 0 ||
                entities.Any(entity => !SketchEntityTypes.Supported.Contains(entity.EntityType)))
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

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceBlocked(context.Feature));
    }
}
