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
        new("depth_mm", "positive_number", false, "盲孔条件必填；通孔可省略，单位毫米。"),
        new("end_condition", "text", false, "终止条件；尚无已授权真实映射。")
        ,new("hole_type", "text", false, "SimpleHole、CounterboreHole、CountersinkHole、TappedHole。")
        ,new("through_all", "boolean", false, "通孔标志；否则为盲孔。")
        ,new("position", "json", false, "参考面局部毫米坐标对象或数组。")
        ,new("reference_face", "text", false, "直接依赖拉伸特征的 start_face/end_face。")
        ,new("quantity", "integer", false, "必须等于显式孔位数量。")
        ,new("pattern_reference", "text", false, "可选已验证放置草图点集引用。")
        ,new("counterbore_diameter_mm", "positive_number", false, "沉孔直径，毫米。")
        ,new("counterbore_depth_mm", "positive_number", false, "沉孔深度，毫米。")
        ,new("countersink_diameter_mm", "positive_number", false, "沉头直径，毫米。")
        ,new("countersink_angle_deg", "positive_number", false, "沉头全角，度。")
        ,new("thread_standard", "text", false, "ISO_METRIC。")
        ,new("thread_size", "text", false, "有限粗牙规格。")
        ,new("thread_pitch", "positive_number", false, "螺距，毫米。")
        ,new("thread_depth_mm", "positive_number", false, "螺纹有效深度，毫米。")
        ,new("tap_drill_diameter_mm", "positive_number", false, "攻丝底孔径，毫米。")
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
        EvidenceId: "v2.1-b-20260907-refresh-HoleHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "simple_circular_cut_blind;diameter_matches_single_circle;positive_depth_mm;no_wizard",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013221_3210868/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(HoleHandler)}.");
        }

        if (feature.Parameters.ContainsKey("hole_type"))
        {
            var result = new HoleValidator().Validate(feature);
            return new(result.IsValid, result.FailureStage, result.Issues);
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

    public override FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(FeatureDefinition feature)
    {
        var result = Validate(feature);
        if (!result.IsValid || !feature.Parameters.ContainsKey("hole_type")) return result;
        var hole = new HoleValidator().Validate(feature).Definition!;
        var stage = HoleExecutionStrategies.Get(hole.HoleType).UnverifiedStage;
        return FeatureHandlerValidationResult.Failed(stage, $"{stage}: {hole.HoleType} 的显式面引用/终止/放置 profile 尚未完成真实 API 取证。");
    }

    public override FeatureHandlerBuildPlanResult BuildPlan(FeatureDefinition feature, FeatureHandlerBuildPlanContext context)
    {
        var result = base.BuildPlan(feature, context);
        if (!result.IsSuccess || !feature.Parameters.ContainsKey("hole_type")) return result;
        var parameters = new Dictionary<string, string>(result.Operation!.Parameters, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in new HoleValidator().Validate(feature).Definition!.ToParameters()) parameters[pair.Key] = pair.Value;
        return result with { Operation = result.Operation with { OperationType = "CreateHole", Parameters = parameters } };
    }

    public override FeatureHandlerReport GenerateReport(FeatureDefinition feature, FeatureHandlerExecutionResult result)
    {
        var report = base.GenerateReport(feature, result);
        if (!feature.Parameters.ContainsKey("hole_type"))
            return report with { HoleType = HoleTypes.Simple, HoleParameters = feature.Parameters };
        var validation = new HoleValidator().Validate(feature);
        return report with
        {
            HoleType = validation.Definition?.HoleType ?? feature.Parameters.GetValueOrDefault("hole_type"),
            HoleParameters = validation.Definition?.ToParameters() ?? feature.Parameters,
            ApiEvidenceStatus = FeatureApiEvidenceStatuses.Unverified,
            EvidenceId = null, EvidenceHandlerVersion = null, EvidenceParameterProfile = null,
            EvidenceSolidWorksVersion = null, EvidenceDiagnosticRunPath = null, EvidenceSourceRevision = null
        };
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
