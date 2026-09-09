using DomainSchemas;

namespace SolidWorksWorker.Features.Pattern;

/// <summary>
/// 单方向线性阵列。Handler 只做参数校验与计划生成，COM 调用由 Adapter 负责。
/// <para>
/// 方向由 <c>direction_selection</c> 判据解出一条直线边，其单位方向即阵列方向；
/// <c>direction</c> 声明期望落在哪根主轴上，执行时与解出的边做平行性交叉校验。
/// 两者都要，是因为判据可能命中"一条合法但不是我想要的边"——只有声明的意图
/// 与解出的实体互相印证，才谈得上选对了。
/// </para>
/// </summary>
public sealed class LinearPatternHandler : FeatureHandlerBase
{
    private static readonly string[] SupportedDirections = ["x", "y", "z"];

    public override string FeatureType => FeatureTypes.LinearPattern;

    public override string OperationType => "CreateLinearPattern";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("direction", "x|y|z", true, "期望的阵列方向主轴；与解出的边做平行性交叉校验。"),
        new("direction_selection", "EdgeSelectionCriteria JSON", true, "方向边判据；必须唯一命中一条直线边。"),
        new("instance_count", "integer_[2,512]", true, "实例总数，含种子特征。"),
        new("spacing_mm", "positive_number", true, "相邻实例间距，单位毫米。"),
        new("seed_feature", "feature_id", true, "被阵列的种子特征标识。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.FeatureLinearPattern4",
        "本机 SDK 反射（SOLIDWORKS 2023, SolidWorks.Interop.sldworks.dll）",
        [
            "Num1, Spacing1(米), Num2, Spacing2, FlipDir1/2, DName1/2",
            "GeometryPattern, VaryInstance, HasOffset1/2, CtrlByNum1/2",
            "FromCentroid1/2, RevOffset1/2, Offset1/2",
            "共 20 个参数，全部为选项与数值；种子与方向只来自打了标记的选择集",
            "创建后回读 ILinearPatternFeatureData.D1Axis 与 GetPatternFeatureCount 校验引用"
        ],
        "IFeature object on success; null on failure.",
        [
            "种子特征已存在且被精确选中。",
            "方向引用为已解析的边、轴或基准。",
            "实例数与间距不会产生自相交或越界实体。"
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "方向边由 EdgeSelectionCriteria 判据求解；判据不唯一命中时拒绝执行。",
            "解出的边方向必须与声明的主轴平行，否则拒绝执行。",
            "实例落在材料之外时 SolidWorks 会报错或产生空实例。",
            "双方向阵列、跳过实例与 VarySketch 不在本阶段范围内。"
        ],
        [
            "方向由 EdgeSelectionCriteria 判据解出一条 100 mm 直线棱，其单位方向与声明的 x 轴平行性校验通过。",
            "V2.1-A 诊断在 SOLIDWORKS 2023 上实调 FeatureLinearPattern4：结果对象非空、重建通过、体积可测变化。",
            "几何被独立复核：种子 Ø6 通孔在 x=-30，声明间距 20 mm、共 4 个实例；",
            "只读拓扑探针在产出零件上量到孔口圆心 x = -30 / -10 / 10 / 30（z 均为 0，半径均 3.00），",
            "体积减少 848.23 立方毫米，与 3 个 Ø6 通孔的闭式解 3×π×3²×10 = 848.23 完全一致。",
            "创建后 IFeature.GetTypeName2 返回 LPattern，证明选择集标记被按预期解读。"
        ],        EvidenceId: "v2.1-b-20260907-refresh-LinearPatternHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "single_direction_linear_pattern;criteria_resolved_direction;principal_axis_cross_checked",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013438_4901315/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(LinearPatternHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(
            feature,
            "direction",
            "direction_selection",
            "instance_count",
            "spacing_mm",
            "seed_feature");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        var seedAndCount = PatternParameterRules.ValidateSeedAndCount(feature, nameof(LinearPatternHandler));
        if (seedAndCount is not null)
        {
            return seedAndCount;
        }

        if (!feature.Parameters.TryGetValue("direction", out var direction) ||
            !SupportedDirections.Contains(direction, StringComparer.OrdinalIgnoreCase))
        {
            return PatternParameterRules.Invalid(
                feature,
                $"direction must be one of {string.Join(", ", SupportedDirections)}.");
        }

        if (!PatternParameterRules.TryPositive(feature, "spacing_mm", out _))
        {
            return PatternParameterRules.Invalid(feature, "spacing_mm must be a finite positive number.");
        }

        return PatternParameterRules.ValidateSingleEdgeCriteria(feature, "direction_selection")
               ?? FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.Adapter is null
            ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteLinearPatternAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }
}
