using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Hole;

public sealed class HoleHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.Hole;

    public override string OperationType => "CreateSimpleHole";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("hole_diameter_mm", "positive_number", false, "孔直径，单位毫米。"),
        new("diameter_mm", "positive_number", false, "孔直径兼容别名，单位毫米。"),
        new("depth_mm", "positive_number", true, "圆形盲切孔深度，单位毫米。"),
        new("end_condition", "text", false, "终止条件；尚无已授权真实映射。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "ISketchManager.CreateCircle + IFeatureManager.FeatureCut4",
        "Project V1.9 API evidence and SOLIDWORKS API Help",
        ["diameter", "depth", "circle-sketch placement reference"],
        "Circle sketch segment and cut IFeature; null means failure.",
        [
            "A supported simple-hole strategy is selected.",
            "Placement, direction and termination references are resolved.",
            "A dedicated diagnostic passes for the exact blind FeatureCut4 adapter."
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "The circle sketch must contain only the supported simple-hole profile.",
            "Blind depth and placement remain diagnostic candidates.",
            "No wizard standard/type identifiers are used."
        ],
        [
            "V2.0-C diagnostic verified a diameter-matched circular sketch plus 20 mm blind FeatureCut4 after reactivating the dependency sketch.",
            "Solid volume decreased from 5.9214601836602546E-05 to 5.84292036732051E-05 cubic metres; no SimpleHole2 or Hole Wizard API was called."
        ],
        EvidenceId: "v2.1-a-20260828-014622-simple-hole",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "simple_circular_cut_blind;diameter_matches_single_circle;positive_depth_mm;no_wizard",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/20260828_014622_5303003/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:533b14e951348372edee939de64e411663a94fbd2dc49ac60c18ee63c298cd54");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(HoleHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(
            feature,
            "hole_diameter_mm",
            "diameter_mm",
            "depth_mm",
            "strategy",
            "end_condition",
            "through_all");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        var diameterText =
            feature.Parameters.GetValueOrDefault("hole_diameter_mm") ??
            feature.Parameters.GetValueOrDefault("diameter_mm");
        if (!double.TryParse(
                diameterText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var diameter) ||
            !double.IsFinite(diameter) ||
            diameter <= 0)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} requires a finite positive hole diameter.");
        }

        if (!feature.Parameters.TryGetValue("depth_mm", out var depthText) ||
            !double.TryParse(depthText, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth) ||
            !double.IsFinite(depth) ||
            depth <= 0)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} requires a finite positive blind depth_mm.");
        }

        if (feature.Parameters.TryGetValue("strategy", out var strategy) &&
            !strategy.Equals("simple_circular_cut_blind", StringComparison.OrdinalIgnoreCase))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} strategy={strategy} is not authorized.");
        }

        if (feature.Parameters.TryGetValue("end_condition", out var endCondition) &&
            !endCondition.Equals("blind", StringComparison.OrdinalIgnoreCase))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} end_condition={endCondition} is not authorized.");
        }

        if (feature.Parameters.TryGetValue("through_all", out var throughAllText) &&
            (!bool.TryParse(throughAllText, out var throughAll) || throughAll))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} supports blind execution only.");
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
            : context.Adapter.ExecuteHoleAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }
}
