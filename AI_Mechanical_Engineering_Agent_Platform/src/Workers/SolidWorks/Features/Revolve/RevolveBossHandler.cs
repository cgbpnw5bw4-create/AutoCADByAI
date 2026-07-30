using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Revolve;

public sealed class RevolveBossHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.RevolveBoss;

    public override string OperationType => "RevolveBoss";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("angle_degrees", "number_(0,360]", true, "旋转角度。"),
        new("profile_selection_mark", "integer", false, "轮廓选择标记；候选范围为 0。"),
        new("axis_selection_mark", "integer", false, "轴选择标记；候选范围为 16。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureRevolve2 with ISelectData.Mark and IEntity.Select4",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "SingleDir, IsSolid, IsThin, IsCut",
            "ReverseDir, BothDirectionUpToSameEntity, Dir1Type, Dir2Type",
            "Dir1Angle, Dir2Angle, offset/reverse/merge/scope options"
        ],
        "IFeature object on success; null on failure.",
        [
            "A closed half-profile and construction axis are resolved.",
            "Profile is selected with mark 0 and axis with mark 16.",
            "Angle is converted to radians and matches the evidence profile."
        ],
        FeatureApiEvidenceStatuses.Unverified,
        [
            "Only the V1.9 shaft-specific 360-degree selection sequence has project evidence.",
            "Generic sketch/result reference resolution is not yet verified.",
            "Open profiles, thin revolve, partial angles and revolve-cut are outside this stage."
        ],
        [
            "V1.9 shaft diagnostic passed the exact 20-argument 360-degree sequence.",
            "No handler-specific diagnostic is bound to the current generic adapter."
        ]);

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(RevolveBossHandler)}.");
        }

        if (!feature.Parameters.TryGetValue("angle_degrees", out var angleText) ||
            !double.TryParse(angleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) ||
            !double.IsFinite(angle) ||
            angle <= 0 ||
            angle > 360)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} angle_degrees must be in (0, 360].");
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
