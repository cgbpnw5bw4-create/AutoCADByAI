using DomainSchemas;

namespace SolidWorksWorker.Features.Pattern;

/// <summary>
/// 绕主轴的圆周阵列。Handler 只做参数校验与计划生成，COM 调用由 Adapter 负责。
/// 当前无 Feature 级真实执行证据，真实执行被证据策略 fail-closed。
/// </summary>
public sealed class CircularPatternHandler : FeatureHandlerBase
{
    private static readonly string[] SupportedAxes = ["x", "y", "z"];

    public override string FeatureType => FeatureTypes.CircularPattern;

    public override string OperationType => "CreateCircularPattern";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("axis_selection", "EdgeSelectionCriteria JSON", true, "轴边判据；必须唯一命中一条圆边，其法向即阵列轴。"),
        new("axis", "x|y|z", true, "阵列轴；当前只接受主轴方向。"),
        new("instance_count", "integer_[2,512]", true, "实例总数，含种子特征。"),
        new("angle_deg", "number_(0,360]", true, "阵列总角度，单位度。"),
        new("seed_feature", "feature_id", true, "被阵列的种子特征标识。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureCircularPattern5",
        "本机 SDK 反射（SOLIDWORKS 2023, SolidWorks.Interop.sldworks.dll）",
        [
            "Number, Spacing(弧度), FlipDirection, DName, GeometryPattern",
            "EqualSpacing, VaryInstance, SyncSubAssemblies, BDir2, BSymmetric",
            "Number2, Spacing2, DName2, EqualSpacing2",
            "共 14 个参数，全部为选项与数值；种子与轴只来自打了标记的选择集",
            "创建后回读 ICircularPatternFeatureData.Axis 与 GetPatternFeatureCount 校验引用"
        ],
        "IFeature object on success; null on failure.",
        [
            "种子特征已存在且被精确选中。",
            "旋转轴引用为已解析的轴、边或临时轴。",
            "角度与实例数组合不会产生重叠实例。"
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "轴边由 EdgeSelectionCriteria 判据求解；判据不唯一命中时拒绝执行。",
            "解出的圆边法向必须与声明的主轴平行，否则拒绝执行。",
            "实例落在材料之外时 SolidWorks 会报错或产生空实例。",
            "跳过实例与 VarySketch 不在本阶段范围内。"
        ],
        [
            "轴由 EdgeSelectionCriteria 判据解出中心 Ø10 孔的顶面孔口，其法向与声明的 y 轴平行性校验通过。",
            "V2.1-A 诊断在 SOLIDWORKS 2023 上实调 FeatureCircularPattern5：结果对象非空、重建通过、体积可测变化。",
            "几何被独立复核：种子 Ø6 通孔在 r=20 处，声明 4 个实例等分 360 度；",
            "只读拓扑探针在产出零件上量到孔口圆心 (20,0)、(0,-20)、(-20,0)、(0,20)，恰好每 90 度一个，",
            "体积减少 848.23 立方毫米，与 3 个 Ø6 通孔的闭式解一致。",
            "创建后 IFeature.GetTypeName2 返回 CirPattern，证明选择集标记被按预期解读。"
        ],        EvidenceId: "v2.1-b-20260907-refresh-CircularPatternHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "principal_axis_circular_pattern;equal_spacing;criteria_resolved_axis",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013451_0073586/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(CircularPatternHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(
            feature,
            "axis",
            "axis_selection",
            "instance_count",
            "angle_deg",
            "seed_feature");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        var seedAndCount = PatternParameterRules.ValidateSeedAndCount(feature, nameof(CircularPatternHandler));
        if (seedAndCount is not null)
        {
            return seedAndCount;
        }

        if (!feature.Parameters.TryGetValue("axis", out var axis) ||
            !SupportedAxes.Contains(axis, StringComparer.OrdinalIgnoreCase))
        {
            return PatternParameterRules.Invalid(
                feature,
                $"axis must be one of {string.Join(", ", SupportedAxes)}.");
        }

        if (!PatternParameterRules.TryPositive(feature, "angle_deg", out var angle) || angle > 360d)
        {
            return PatternParameterRules.Invalid(feature, "angle_deg must be inside the interval (0, 360].");
        }

        return PatternParameterRules.ValidateSingleEdgeCriteria(feature, "axis_selection")
               ?? FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.Adapter is null
            ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteCircularPatternAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }
}
