using System.Text.Json;
using DomainSchemas;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class V20ERegressionGateTests
{
    [Fact]
    public void CapabilityRegressionGateRejectsProtectedTrueToFalseRegression()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v20e_gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, SelfCheckCapabilityRegressionGate.BaselineRelativePath),
                """
                { "protected_capabilities": { "stable_capability": true } }
                """);

            var result = SelfCheckCapabilityRegressionGate.Evaluate(
                root,
                new Dictionary<string, bool> { ["stable_capability"] = false });

            Assert.True(result.BaselineLoaded);
            Assert.False(result.Passed);
            Assert.Equal(["stable_capability"], result.RegressedFields);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RegressionModelsCompileThroughRegisteredDefinitionsAndReuseTemplates()
    {
        var registry = PartTypeRegistry.CreateDefault();
        var models = PartFamilyRegressionModels.CreateDefault();

        // 回归输入必须覆盖 PartTypeRegistry 中的每一个族，含真实执行被冻结的 jacket。
        Assert.Equal(
            registry.GetAll().Select(definition => definition.PartType).OrderBy(value => value, StringComparer.Ordinal),
            models.Select(model => model.PartType).OrderBy(value => value, StringComparer.Ordinal));
        Assert.All(models, model =>
        {
            var definition = Assert.IsAssignableFrom<IPartFamilyDefinition>(registry.GetDefinition(model.PartType));
            var plan = definition.GenerateBuildPlan($"test-{model.PartType}", model.Spec);
            Assert.True(plan.IsSuccess);
            Assert.NotEmpty(model.Spec.Sketches);
            Assert.NotEmpty(model.Spec.Features);
        });
        Assert.NotNull(typeof(CommonFeatureTemplates).GetMethod(nameof(CommonFeatureTemplates.CreateCircleSketch)));

        // 模板复用必须是可鉴别的事实，不是「模板方法存在」。
        // dimensional + parameter 元数据的组合只可能来自
        // CommonFeatureTemplates.CreateDimensionalConstraint。
        Assert.All(models, model => Assert.Contains(
            model.Spec.Sketches,
            sketch => sketch.Constraints.Any(constraint =>
                string.Equals(
                    constraint.ConstraintType,
                    SketchConstraintTypes.Dimensional,
                    StringComparison.OrdinalIgnoreCase) &&
                constraint.Parameters.ContainsKey("parameter"))));

        // 反例：不经模板构造的约束不携带 parameter 元数据，因此该判据可鉴别，
        // 不是「只要模板类存在就为真」的恒真式。
        var handWritten = new SketchConstraint("bare", SketchConstraintTypes.Dimensional, ["e1"], "10");
        Assert.False(handWritten.Parameters.ContainsKey("parameter"));

        var fromTemplate = CommonFeatureTemplates.CreateDimensionalConstraint("t", "e1", "length_mm", "10");
        Assert.True(fromTemplate.Parameters.ContainsKey("parameter"));
    }

    [Fact]
    public void CapabilityRegressionGateRejectsAWeakenedBaselineInsteadOfIgnoringIt()
    {
        // 削弱基线是绕过本闸门阻力最小的路径：把某个受保护字段改成 false，
        // 回归就变成合法的，而自检依旧全绿。闸必须把这种写法当配置错误拒绝。
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v20e_gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, SelfCheckCapabilityRegressionGate.BaselineRelativePath),
                """
                { "protected_capabilities": { "weakened_capability": false } }
                """);

            var result = SelfCheckCapabilityRegressionGate.Evaluate(
                root,
                new Dictionary<string, bool> { ["weakened_capability"] = false });

            Assert.False(result.Passed);
            Assert.Contains(
                result.ConfigurationErrors,
                issue => issue.Contains("weakened_capability", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CapabilityRegressionGateSeparatesUnobservedFieldsFromRealRegressions()
    {
        // 基线保护了一个自检根本没观测的字段时，闸同样要失败——但要报成配置错误，
        // 而不是能力回归。混为一谈会让人去修没坏的那一头。
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v20e_gate", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, SelfCheckCapabilityRegressionGate.BaselineRelativePath),
                """
                { "protected_capabilities": { "observed_capability": true, "unwired_capability": true } }
                """);

            var result = SelfCheckCapabilityRegressionGate.Evaluate(
                root,
                new Dictionary<string, bool> { ["observed_capability"] = true });

            Assert.False(result.Passed);
            Assert.Empty(result.RegressedFields);
            Assert.Contains(
                result.ConfigurationErrors,
                issue => issue.Contains("unwired_capability", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProductionBaselineIsFullyWiredIntoTheSelfCheckSnapshot()
    {
        // 基线只增不减，但"加进基线"和"接进自检快照"是两件事。
        // 只做前者会让闸变成一条恒假的配置错误，本轮补两条能力时就差点如此。
        var projectRoot = FindProjectRoot();
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(projectRoot, SelfCheckCapabilityRegressionGate.BaselineRelativePath)));
        var protectedNames = document.RootElement
            .GetProperty("protected_capabilities")
            .EnumerateObject()
            .Select(field => field.Name)
            .ToArray();

        var snapshot = File.ReadAllText(Path.Combine(
            projectRoot,
            "src",
            "PlatformCore",
            "PlatformSelfCheckRunner.cs"));

        foreach (var name in protectedNames)
        {
            Assert.Contains($"[\"{name}\"]", snapshot, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProductionBaselineProtectsKnownCapabilitiesAndNeverWeakensThem()
    {
        var baselinePath = Path.Combine(
            FindProjectRoot(),
            SelfCheckCapabilityRegressionGate.BaselineRelativePath);
        Assert.True(File.Exists(baselinePath), baselinePath);

        using var document = JsonDocument.Parse(File.ReadAllText(baselinePath));
        var protectedCapabilities = document.RootElement.GetProperty("protected_capabilities");

        // 基线只增不减：以下能力一旦达成即不得从基线中移除或改为 false。
        string[] required =
        [
            "all_part_families_use_registry",
            "feature_production_evidence_active",
            "flange_artifact_validation_supported",
            "shaft_artifact_validation_supported",
            "plate_part_family_regression_passed",
            "v2_0_d_production_evidence_active",
            "v2_0_e_controlled_plate_evidence_active",
            "v2_0_e_step_content_gate_active",
            "v2_0_e_unified_part_family_builders",
            "v2_0_e_geometry_validation_platform_wide",
            "feature_api_evidence_required",
            "unverified_feature_blocks_execution",
            "complex_feature_registry_supported",
            "edge_selection_model_supported",
            // 这两条在 V2.1-A 注册五个复杂特征时曾静默退化为 false：判据里写死了
            // "注册表恰好 5 个 Handler"。退化没被任何门拦住，正是因为它们当时
            // 不在基线里。补进来的理由就是这次事故本身。
            "feature_handler_no_direct_com_access",
            "unverified_api_blocks_real_execution"
        ];

        foreach (var name in required)
        {
            Assert.True(
                protectedCapabilities.TryGetProperty(name, out var value),
                $"protected capability {name} was removed from the baseline.");
            Assert.True(
                value.ValueKind == JsonValueKind.True,
                $"protected capability {name} must stay true in the baseline.");
        }

        // 任何字段都不得为 false —— 基线里 false 没有合法语义。
        Assert.All(
            protectedCapabilities.EnumerateObject(),
            field => Assert.True(
                field.Value.ValueKind == JsonValueKind.True,
                $"baseline field {field.Name} must be true."));
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("solution root not found");
    }
}
