using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Chamfer;

/// <summary>
/// 距离-角度倒角。Handler 只做参数校验与计划生成，COM 调用由 Adapter 负责。
/// 边选择与圆角共用同一套 EdgeSelectionCriteria 判据：判据不唯一命中时拒绝执行。
/// </summary>
public sealed class ChamferHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.Chamfer;

    public override string OperationType => "CreateChamfer";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("distance_mm", "positive_number", true, "倒角距离，单位毫米。"),
        new("angle_deg", "number_(0,90)", true, "倒角角度，单位度。"),
        new("edge_selection", "EdgeSelectionCriteria JSON", true, "结构化边选择判据；必须唯一命中声明的边数。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.InsertFeatureChamfer",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "Type, Width, Angle, OtherDist",
            "VertexChamDist1/2/3",
            "flip / equal-distance 选项"
        ],
        "IFeature object on success; null on failure.",
        [
            "目标边或面已被精确选中。",
            "距离与角度组合在相邻几何允许范围内。",
            "选择集与证据档案的参数组合一致。"
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "边选择依赖 EdgeSelectionCriteria 判据求解；判据不唯一命中时拒绝执行。",
            "距离或角度超界时返回 null 或生成无效实体。",
            "顶点倒角、等距倒角与切线延伸不在本阶段范围内。"
        ],
        [
            "边选择模型与圆角同源（EdgeSelectionResolver + SolidWorksEdgeEnumerator）。",
            "V2.1-A 诊断在 SOLIDWORKS 2023 上实调 InsertFeatureChamfer：判据唯一命中 y=0 一侧的两条孔口，",
            "结果对象非空、重建通过、体积发生可测变化。",
            "几何被独立复核而不是只看体积变了：只读拓扑探针在产出零件上量到两条 radius=6.00 的",
            "plane+cone 圆边与两条 y=1.00 的 cylinder+cone 圆边，正是 1 mm×45° 倒角应有的锥面环；",
            "体积减少 33.5 立方毫米，与 Pappus 闭式解 33.51 立方毫米在四位有效数字上一致。"
        ],
        EvidenceId: "v2.1-b-20260907-refresh-ChamferHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "distance_angle_edge_chamfer;angle_distance_type;criteria_resolved_selection",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013423_5400787/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(ChamferHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(feature, "distance_mm", "angle_deg", "edge_selection");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!TryFinite(feature, "distance_mm", out var distance) || distance <= 0d)
        {
            return Invalid(feature, "distance_mm must be a finite positive number.");
        }

        if (!TryFinite(feature, "angle_deg", out var angle) || angle <= 0d || angle >= 90d)
        {
            return Invalid(feature, "angle_deg must be inside the open interval (0, 90).");
        }

        feature.Parameters.TryGetValue("edge_selection", out var edges);
        if (!EdgeSelectionCriteriaParser.TryParse(edges, out _, out var edgeIssue))
        {
            return Invalid(feature, $"edge_selection is unusable: {edgeIssue}");
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
            : context.Adapter.ExecuteChamferAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }

    private static bool TryFinite(FeatureDefinition feature, string name, out double value)
    {
        value = 0d;
        return feature.Parameters.TryGetValue(name, out var text) &&
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
               double.IsFinite(value);
    }

    private static FeatureHandlerValidationResult Invalid(FeatureDefinition feature, string message) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId}: {message}");
}
