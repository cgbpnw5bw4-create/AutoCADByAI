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
        FeatureApiEvidenceStatuses.Verified,
        [
            "V2.0-A through_all=true is not proven by the V1.9 blind over-depth call.",
            "An invalid selection or end condition can return null.",
            "Normal-cut, thin and multi-body scopes are outside this stage."
        ],
        [
            "V1.9 plate/flange family builders used FeatureCut4.",
            "V2.0-C diagnostic verified a 20 mm blind FeatureCut4 call after reactivating the dependency sketch.",
            "Solid volume decreased from 5.9999999999999995E-05 to 5.9214601836602546E-05 cubic metres; four-view review shows the cut."
        ],
        EvidenceId: "v2.1-b-20260907-refresh-ExtrudeCutHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "blind;single_end;positive_depth_mm;through_all_false;no_thin;single_body_scope",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013221_3210868/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(ExtrudeCutHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(feature, "through_all", "depth_mm");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        var hasThroughAll = feature.Parameters.TryGetValue("through_all", out var text);
        var parsed = false;
        var validThroughAll =
            !hasThroughAll ||
            bool.TryParse(text, out parsed);
        var throughAll =
            hasThroughAll &&
            validThroughAll &&
            parsed;
        var positiveDepth =
            feature.Parameters.TryGetValue("depth_mm", out var depthText) &&
            double.TryParse(depthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth) &&
            double.IsFinite(depth) &&
            depth > 0;
        if (!validThroughAll || throughAll || !positiveDepth)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} supports blind positive depth_mm only.");
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
            : context.Adapter.ExecuteExtrudeCutAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }
}
