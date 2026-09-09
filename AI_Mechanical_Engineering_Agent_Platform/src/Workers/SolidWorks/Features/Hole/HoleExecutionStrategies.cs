using DomainSchemas;

namespace SolidWorksWorker.Features.Hole;

// 保留统一 Adapter 入口；策略只声明类型、失败和证据，不承载 COM。
public sealed record HoleExecutionStrategy(string HoleType, string ExecutionFailureStage, string UnverifiedStage, FeatureApiEvidence ApiEvidence);

public static class HoleExecutionStrategies
{
    private static readonly IReadOnlyDictionary<string, HoleExecutionStrategy> Strategies =
        new Dictionary<string, HoleExecutionStrategy>(StringComparer.OrdinalIgnoreCase)
        {
            [HoleTypes.Simple] = Create(HoleTypes.Simple, PartFamilyFailureStages.SimpleHoleExecutionFailed),
            [HoleTypes.Counterbore] = Create(HoleTypes.Counterbore, PartFamilyFailureStages.CounterboreExecutionFailed),
            [HoleTypes.Countersink] = Create(HoleTypes.Countersink, PartFamilyFailureStages.CountersinkExecutionFailed),
            [HoleTypes.Tapped] = Create(HoleTypes.Tapped, PartFamilyFailureStages.TappedHoleExecutionFailed)
        };
    public static IReadOnlyCollection<HoleExecutionStrategy> All => Strategies.Values.ToArray();
    public static HoleExecutionStrategy Get(string type) => Strategies[type];
    private static HoleExecutionStrategy Create(string type, string failure) => new(type, failure,
        type == HoleTypes.Tapped ? PartFamilyFailureStages.TappedHoleApiUnverified : PartFamilyFailureStages.FeatureApiUnverified,
        new FeatureApiEvidence(
            "IFeatureManager.CreateDefinition + IWizardHoleFeatureData2.InitializeHole + IFeatureManager.CreateFeature",
            "https://help.solidworks.com/2023/english/api/sldworksapiprogguide/Overview/Hole_Wizard_Features_and_WizardHoleFeatureData2_Objects.htm",
            ["毫米长度转换为米；角度转换为弧度", "规格映射标准/紧固件枚举后读回确认；螺距不是 HoleWizard5.Length", "前置选择为已解析参考面与显式孔位"],
            "IFeature 必须存在，重建通过且独立几何/线程元数据读回匹配。",
            ["当前运行时与源码绑定诊断通过", "已解析选择状态和标准规格", "TappedHole 要求 Hole Wizard 攻丝类型，不允许普通 Cut 或仅 Cosmetic Thread 冒充"],
            FeatureApiEvidenceStatuses.Unverified,
            ["选择失败", "枚举或规格不存在", "重建失败", "几何或攻丝元数据不匹配", "缺少宏录制及当前 profile 真实证据"],
            ["官方签名和本地 DLL 反射仅证明 API 存在，不证明真实执行。历史圆形盲切证据不能授权新 profile。"]));
}
