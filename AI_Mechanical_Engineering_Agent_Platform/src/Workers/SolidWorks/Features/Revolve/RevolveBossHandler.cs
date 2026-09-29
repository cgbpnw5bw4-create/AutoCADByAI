using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Revolve;

public sealed class RevolveBossHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.RevolveBoss;

    public override string OperationType => "RevolveBoss";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("angle_degrees", "360", true, "仅完整实体旋转。"),
        new("profile_selection_mark", "0", true, "轮廓选择标记。"),
        new("axis_selection_mark", "16", true, "唯一构造轴选择标记。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureRevolve2 with IFeature.Select2 and ISketchSegment.Select4",
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
        FeatureApiEvidenceStatuses.Verified,
        [
            "Only the bound RightPlane closed half-section and single construction axis are authorized.",
            "Open profiles, thin revolve, partial angles and revolve-cut are outside this stage."
        ],
        [
            "V2.2-D uses an exact 20-argument call with selection identity, angle, boss/thin and single-solid readback.",
            "Native and STEP reopening are required by the independent part-family evidence gate."
        ],
        EvidenceId: "v2.2-d-20260928-RevolveBossHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "right_plane;closed_half_section;single_construction_axis_x;profile_mark_0;axis_mark_16;solid_boss_360;no_thin;no_cut",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_2_d_final/20260928_014622_7587652/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e484715785d2ba21ea7ae3efbca5238f37a4fbc988cb37b9658603d33e22a048");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(RevolveBossHandler)}.");
        }

        var unknown = RejectUnknownParameters(feature, "angle_degrees", "profile_selection_mark", "axis_selection_mark", "feature_api");
        if (!unknown.IsValid) return unknown;
        if ((!string.IsNullOrWhiteSpace(feature.TargetReference) && !feature.TargetReference.Equals("RightPlane", StringComparison.OrdinalIgnoreCase)) ||
            (feature.Parameters.TryGetValue("feature_api", out var api) && api != "FeatureRevolve2") ||
            feature.Parameters.GetValueOrDefault("profile_selection_mark") != "0" ||
            feature.Parameters.GetValueOrDefault("axis_selection_mark") != "16" ||
            feature.ReferencedSketches.Count != 1 ||
            !feature.Parameters.TryGetValue("angle_degrees", out var angleText) ||
            !double.TryParse(angleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var angle) ||
            !double.IsFinite(angle) ||
            angle != 360d)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} 只支持唯一草图、360°实体凸台、轮廓 mark 0 和轴 mark 16。");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = Validate(context.Feature);
        if (!validation.IsValid)
            return Task.FromResult(FeatureHandlerExecutionResult.Failed(validation.FailureStage!, validation.Issues.ToArray()));
        return context.Adapter is null ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteRevolveBossAsync(context.Feature, context.Operation, context.State, cancellationToken);
    }
}
