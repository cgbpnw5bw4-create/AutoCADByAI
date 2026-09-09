using DomainSchemas;

namespace SolidWorksWorker.Features.Mirror;

/// <summary>
/// 关于标准基准面的特征镜像。Handler 只做参数校验与计划生成，COM 调用由 Adapter 负责。
/// 当前无 Feature 级真实执行证据，真实执行被证据策略 fail-closed。
/// </summary>
public sealed class MirrorHandler : FeatureHandlerBase
{
    private static readonly string[] SupportedPlanes = ["FrontPlane", "TopPlane", "RightPlane"];

    public override string FeatureType => FeatureTypes.Mirror;

    public override string OperationType => "CreateMirror";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("mirror_plane", "FrontPlane|TopPlane|RightPlane", true, "镜像基准面；当前只接受标准基准面。"),
        new("target_features", "feature_id[;feature_id]", true, "被镜像的特征标识，分号分隔。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "IFeatureManager.InsertMirrorFeature2",
        "本机 SDK 反射（SOLIDWORKS 2023, SolidWorks.Interop.sldworks.dll）",
        [
            "BMirrorBody, BGeometryPattern, BMerge, BKnit, ScopeOptions",
            "共 5 个参数，全部为选项；基准面与被镜像特征只来自打了标记的选择集",
            "创建后回读 IMirrorPatternFeatureData.Plane 与 GetPatternFeatureCount 校验引用"
        ],
        "IFeature object on success; null on failure.",
        [
            "镜像基准面与全部被镜像特征均已被精确选中。",
            "被镜像特征在特征树中位于镜像特征之前。",
            "镜像结果不与原实体自相交。"
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "基准面按标准名解析，不依赖会漂移的自动生成名。",
            "被镜像特征若晚于镜像特征生成，重建时会失败。",
            "镜像实体、面与曲面缝合不在本阶段范围内。"
        ],
        [
            "基准面按标准名在特征树中解析为 RefPlane 对象，不依赖会漂移的自动生成名。",
            "V2.1-A 诊断在 SOLIDWORKS 2023 上实调 InsertMirrorFeature2：结果对象非空、重建通过、体积可测变化。",
            "几何被独立复核：种子 Ø6 通孔在 (x=25, z=-15)，关于 RightPlane 镜像；",
            "只读拓扑探针在产出零件上量到新孔位于 (x=-25, z=-15)——只有 x 变号，z 不变，",
            "正是关于 YZ 面镜像应有的结果；体积减少 282.74 立方毫米，与一个 Ø6 通孔的闭式解一致。",
            "创建后 IFeature.GetTypeName2 返回 MirrorPattern，证明两组选择集标记被按预期解读。"
        ],        EvidenceId: "v2.1-b-20260907-refresh-MirrorHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "standard_plane_feature_mirror;right_plane_verified",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_1_b_refresh/20260907_013503_7710789/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(MirrorHandler)}.");
        }

        var unknownParameters = RejectUnknownParameters(feature, "mirror_plane", "target_features");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!feature.Parameters.TryGetValue("mirror_plane", out var plane) ||
            !SupportedPlanes.Contains(plane, StringComparer.OrdinalIgnoreCase))
        {
            return Invalid(feature, $"mirror_plane must be one of {string.Join(", ", SupportedPlanes)}.");
        }

        if (!feature.Parameters.TryGetValue("target_features", out var targetText) ||
            string.IsNullOrWhiteSpace(targetText))
        {
            return Invalid(feature, "target_features must list at least one feature id.");
        }

        var targets = targetText
            .Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (targets.Length == 0)
        {
            return Invalid(feature, "target_features must list at least one feature id.");
        }

        if (feature.ReferencedFeatures.Count > 0)
        {
            var unbound = targets
                .Where(target => !feature.ReferencedFeatures.Contains(target, StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (unbound.Length > 0)
            {
                return Invalid(
                    feature,
                    $"target_features {string.Join(", ", unbound)} are not present in referenced_features.");
            }
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
            : context.Adapter.ExecuteMirrorAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }

    private static FeatureHandlerValidationResult Invalid(FeatureDefinition feature, string message) =>
        FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId}: {message}");
}
