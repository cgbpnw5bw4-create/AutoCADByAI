using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Extrude;

public sealed class ExtrudeBossHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.ExtrudeBoss;

    public override string OperationType => "ExtrudeBoss";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("depth_mm", "positive_number", true, "拉伸深度，单位毫米。"),
        new("direction", "blind|mid_plane", false, "拉伸方向语义；当前真实证据不授权 mid_plane。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureExtrusion2",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "Sd, Flip, Dir, T1, T2, D1, D2",
            "draft/thin-feature options",
            "merge/use-feature-scope/auto-select options",
            "start condition and offset"
        ],
        "IFeature object on success; null on failure.",
        [
            "A valid closed sketch is active or selected.",
            "Lengths are converted from millimetres to metres.",
            "End-condition semantics match the evidence profile."
        ],
        FeatureApiEvidenceStatuses.Unverified,
        [
            "V2.0-A direction=mid_plane is not proven by the V1.9 blind-extrude call.",
            "Incorrect active sketch or argument count can return null.",
            "Thin, draft and feature-scope profiles are outside this stage."
        ],
        [
            "V1.9 plate/flange family builders used FeatureExtrusion2.",
            "No handler-specific diagnostic proves the generic parameter mapping."
        ]);

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return Unsupported(feature);
        }

        if (!Positive(feature, "depth_mm"))
        {
            return Invalid(feature, "depth_mm must be a finite positive number.");
        }

        if (feature.Parameters.TryGetValue("direction", out var direction) &&
            !direction.Equals("blind", StringComparison.OrdinalIgnoreCase) &&
            !direction.Equals("mid_plane", StringComparison.OrdinalIgnoreCase))
        {
            return Invalid(feature, $"direction {direction} is not in the declared schema.");
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

    private static bool Positive(FeatureDefinition feature, string name) =>
        feature.Parameters.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) &&
        value > 0;

    private static FeatureHandlerValidationResult Invalid(FeatureDefinition feature, string message) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId}: {message}");

    private static FeatureHandlerValidationResult Unsupported(FeatureDefinition feature) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.UnsupportedFeatureType,
            $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(ExtrudeBossHandler)}.");
}
