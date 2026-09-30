using System.Globalization;
using System.Text.RegularExpressions;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Validators;
using PlatformCore.Modules.CADModeling.Reviewers;

namespace PlatformCore;

/// <summary>首个闭环仅接纳已有实证的夹套几何档案；其他需求明确拒绝，不推断默认值。</summary>
public sealed class EngineeringPlanValidator
{
    private static readonly IReadOnlyDictionary<string, (string Label, double Value)> Profile =
        new Dictionary<string, (string, double)>
        {
            ["outer_diameter_mm"] = ("外径", 140),
            ["inner_diameter_mm"] = ("内径", 120),
            ["length_mm"] = ("(?:轴向长度|长度|长)", 180)
        };

    public EngineeringPlanValidationResult Validate(EngineeringPlan plan, string requirement, string taskId)
    {
        var issues = new List<string>();
        if (plan.SchemaVersion != "2.3" || string.IsNullOrWhiteSpace(plan.Rationale) ||
            !string.Equals(plan.Requirement, requirement, StringComparison.Ordinal))
            issues.Add("engineering_plan_source_invalid: 版本、依据或原需求绑定不正确。");
        if (plan.Decision != "ready") issues.Add("engineering_plan_not_ready: 模型没有给出可执行计划，必须修订需求。");
        if (plan.Assumptions is null || plan.MissingParameters is null || plan.Risks is null ||
            plan.Assumptions.Count > 0 || plan.MissingParameters.Count > 0 || plan.Risks.Count > 0)
            issues.Add("engineering_plan_uncertain: 假设、缺参或风险必须解决后重新规划。");

        var spec = plan.CadModelSpec;
        if (spec is null) return new(false, null, null, issues.Append("engineering_plan_model_missing: 计划缺少 CADModelSpec。").ToArray());
        if (spec.ModelType != JacketBasicDefinition.Type || spec.Unit != "mm" ||
            !Regex.IsMatch(spec.ModelId, "^[a-zA-Z0-9_-]{1,80}$", RegexOptions.CultureInvariant))
            issues.Add("engineering_plan_profile_unsupported: 本阶段仅支持毫米制 jacket_basic 受证档案。");
        if (spec.Parameters.Count != Profile.Count || spec.Parameters.Keys.Any(key => !Profile.ContainsKey(key)))
            issues.Add("engineering_plan_parameter_invalid: 必须明确且只提供三个夹套参数。");
        if (spec.Material.Length > 0 || spec.Materials.Count > 0 || spec.Sketches.Count > 0 || spec.Features.Count > 0 ||
            spec.ReferenceGeometry.Count > 0 || spec.FeatureOptions.Count > 0 || spec.ExecutionOptions.Count > 0 ||
            spec.DrawingRequirements.Count > 0 || spec.Constraints.Count > 0)
            issues.Add("engineering_plan_scope_unsupported: 此几何样例不接纳材料验收、模型图、附加约束或执行选项。");
        if (spec.OutputRequirements.Count != 2 || !spec.OutputRequirements.Contains("SLDPRT", StringComparer.Ordinal) ||
            !spec.OutputRequirements.Contains("STEP", StringComparer.Ordinal))
            issues.Add("engineering_plan_output_unsupported: 必须要求且只要求 SLDPRT 和 STEP。");

        // 除模型自报缺参外，平台逐项查原文；不能把受证档案尺寸当作用户默认参数。
        var residual = requirement;
        foreach (var (key, profile) in Profile)
        {
            var pattern = $@"{profile.Label}\s*(?:为|是|=|:|：)?\s*[ΦφØ]?\s*([+-]?\d+(?:\.\d+)?)\s*(?:mm|毫米)";
            var matches = Regex.Matches(requirement, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var parsed = spec.Parameters.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var dimension) && double.IsFinite(dimension) ? dimension : double.NaN;
            if (matches.Count != 1 || !double.TryParse(matches[0].Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var stated) || stated != parsed)
                issues.Add($"engineering_plan_requirement_mismatch: {key} 必须在原文中唯一明确、含毫米单位且与计划一致。");
            if (parsed != profile.Value)
                issues.Add($"engineering_plan_evidence_scope_mismatch: {key}={value} 不属于本阶段受证尺寸 {profile.Value}。");
            residual = Regex.Replace(residual, pattern, "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        if (!requirement.Contains("夹套", StringComparison.Ordinal) ||
            !requirement.Contains("SLDPRT", StringComparison.OrdinalIgnoreCase) || !requirement.Contains("STEP", StringComparison.OrdinalIgnoreCase))
            issues.Add("engineering_plan_requirement_incomplete: 原文必须明确夹套和两种输出。");
        residual = Regex.Replace(residual, @"SLDPRT|STEP|请|创建|建模|生成|一个|圆筒|同轴|直筒|夹套|输出|导出|和|与|及|为|是|文件|格式|[\s，,。.;；:：、]", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (residual.Length > 0)
            issues.Add("engineering_plan_requirement_unsupported: 原文存在当前几何档案无法校验的要求，不能静默忽略。");
        var registry = PartTypeRegistry.CreateDefault();
        issues.AddRange(new CADModelSpecValidator(registry).Validate(spec).Issues);
        if (issues.Count > 0) return new(false, null, null, issues);

        var mapped = PartFamilyGenericModelFactory.CreateJacketBasic(spec with
        {
            ModelId = "engineering-jacket-" + Regex.Replace(taskId, "[^a-zA-Z0-9_-]", "_"),
            Parameters = Profile.ToDictionary(item => item.Key,
                item => item.Value.Value.ToString("R", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase),
            Description = requirement,
            OutputRequirements = ["SLDPRT", "STEP", "feature_execution_report.json", "build_report.json",
                "e2e_execution_report.json", "release_manifest.json", "package_quality_report.json"]
        }) with { RequiresFeatureHandlerPipeline = true };
        issues.AddRange(new CADModelSpecValidator(registry).Validate(mapped).Issues);
        var compiled = registry.GetDefinition(JacketBasicDefinition.Type)!.GenerateBuildPlan(taskId, mapped);
        issues.AddRange(compiled.Issues);
        if (compiled.BuildPlan is not null)
        {
            issues.AddRange(new SolidWorksBuildPlanValidator().Validate(compiled.BuildPlan).Issues);
            issues.AddRange(new SolidWorksBuildPlanReviewer().Review(compiled.BuildPlan).Issues);
        }
        if (!compiled.IsSuccess) issues.Add("engineering_plan_mapping_failed: 现有编译器拒绝映射计划。");
        return issues.Count == 0 ? new(true, mapped, compiled.BuildPlan, []) : new(false, null, null, issues);
    }
}
