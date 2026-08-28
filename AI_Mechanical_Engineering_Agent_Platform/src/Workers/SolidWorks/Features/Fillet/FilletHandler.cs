using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Fillet;

/// <summary>
/// 恒定半径圆角。Handler 只做参数校验与计划生成，COM 调用由 Adapter 负责。
/// 边选择由 EdgeSelectionCriteria 判据求解，判据不唯一命中时拒绝执行。
/// </summary>
public sealed class FilletHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.Fillet;

    public override string OperationType => "CreateFillet";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("radius_mm", "positive_number", true, "圆角半径，单位毫米。"),
        new("edge_selection", "EdgeSelectionCriteria JSON", true, "结构化边选择判据；必须唯一命中声明的边数。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureFillet3",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "Options(Int32), R1/R2(Double, 米), Rho(Double)",
            "Ftyp(Int32), OverflowType(Int32), ConicRhoType(Int32)",
            "Radii / Dist2Arr / RhoArr / SetBackDistances(Object 数组)",
            "PointRadiusArray / PointDist2Array / PointRhoArray(Object 数组)",
            "共 14 个参数，全部为选项与数值；目标边只来自当前选择集"
        ],
        "IFeature object on success; null on failure.",
        [
            "目标边或面已被精确选中。",
            "半径小于相邻几何允许的最大值。",
            "选择集与证据档案的参数组合一致。"
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "边选择依赖 EdgeSelectionCriteria 判据求解；判据不唯一命中时拒绝执行。",
            "半径过大时 SolidWorks 返回 null 或产生自相交实体。",
            "变半径、setback 与面圆角均不在本阶段范围内。"
        ],
        [
            "边选择模型已建立并经真机验证（EdgeSelectionResolver + SolidWorksEdgeEnumerator）。",
            "V2.1-A 诊断在 SOLIDWORKS 2023 上实调 FeatureFillet3：判据唯一命中两条顶面孔口，",
            "结果对象非空、重建通过、体积发生可测变化。"
        ],
        EvidenceId: "v2.1-a-20260828-014622-fillet",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "constant_radius_edge_fillet;uniform_radius;simple_type;criteria_resolved_selection",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/20260828_014622_5303003/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:533b14e951348372edee939de64e411663a94fbd2dc49ac60c18ee63c298cd54");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(FilletHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(feature, "radius_mm", "edge_selection");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!feature.Parameters.TryGetValue("radius_mm", out var radiusText) ||
            !double.TryParse(radiusText, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius) ||
            !double.IsFinite(radius) ||
            radius <= 0d)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} radius_mm must be a finite positive number.");
        }

        feature.Parameters.TryGetValue("edge_selection", out var edges);
        if (!EdgeSelectionCriteriaParser.TryParse(edges, out _, out var edgeIssue))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} edge_selection is unusable: {edgeIssue}");
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
            : context.Adapter.ExecuteFilletAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }
}
