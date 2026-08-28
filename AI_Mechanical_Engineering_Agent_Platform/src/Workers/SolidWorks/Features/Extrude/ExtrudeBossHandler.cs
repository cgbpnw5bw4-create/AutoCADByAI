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
        FeatureApiEvidenceStatuses.Verified,
        [
            "V2.0-A direction=mid_plane is not proven by the V1.9 blind-extrude call.",
            "Incorrect active sketch or argument count can return null.",
            "Thin, draft and feature-scope profiles are outside this stage."
        ],
        [
            "V1.9 plate/flange family builders used FeatureExtrusion2.",
            "V2.0-C diagnostic verified a 10 mm blind boss from a selected rectangular sketch and measured volume increase from 0 to 5.9999999999999995E-05 cubic metres.",
            "Diagnostic SLDPRT and STEP identity is recorded in the bound diagnostic report; a per-run hash is deliberately not restated here because it cannot be known before the run that this claim is hashed into."
        ],
        EvidenceId: "v2.1-a-20260828-014622-extrude-boss",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "blind;single_end;positive_depth_mm;no_draft;no_thin;merge_result",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/20260828_014622_5303003/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:533b14e951348372edee939de64e411663a94fbd2dc49ac60c18ee63c298cd54");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return Unsupported(feature);
        }

        var unknownParameters = RejectUnknownParameters(feature, "depth_mm", "direction");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!Positive(feature, "depth_mm"))
        {
            return Invalid(feature, "depth_mm must be a finite positive number.");
        }

        if (feature.Parameters.TryGetValue("direction", out var direction) &&
            !direction.Equals("blind", StringComparison.OrdinalIgnoreCase))
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
        return context.Adapter is null
            ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteExtrudeBossAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
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
