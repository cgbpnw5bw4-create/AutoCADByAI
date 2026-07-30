using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Cut;

public sealed class ExtrudeCutHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.ExtrudeCut;

    public override string OperationType => "CutExtrude";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("through_all", "boolean", false, "是否贯穿；当前真实证据不授权该语义。"),
        new("depth_mm", "positive_number", false, "盲切深度，单位毫米。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureCut4",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "Sd, Flip, Dir, T1, T2, D1, D2",
            "draft/thin-feature options",
            "normal-cut, feature-scope and start-condition options"
        ],
        "IFeature object on success; null on failure.",
        [
            "A valid cutting sketch is active or selected.",
            "Depth and end condition describe the same semantic profile.",
            "The target body and scope are unambiguous."
        ],
        FeatureApiEvidenceStatuses.Unverified,
        [
            "V2.0-A through_all=true is not proven by the V1.9 blind over-depth call.",
            "An invalid selection or end condition can return null.",
            "Normal-cut, thin and multi-body scopes are outside this stage."
        ],
        [
            "V1.9 plate/flange family builders used FeatureCut4.",
            "No handler-specific diagnostic proves a true through-all mapping."
        ]);

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(ExtrudeCutHandler)}.");
        }

        var throughAll =
            feature.Parameters.TryGetValue("through_all", out var text) &&
            bool.TryParse(text, out var parsed) &&
            parsed;
        var positiveDepth =
            feature.Parameters.TryGetValue("depth_mm", out var depthText) &&
            double.TryParse(depthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth) &&
            double.IsFinite(depth) &&
            depth > 0;
        if (!throughAll && !positiveDepth)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} requires through_all=true or positive depth_mm.");
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
