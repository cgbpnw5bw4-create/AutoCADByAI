using System.Reflection;
using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using SkillContracts;
using WorkerContracts;

namespace PlatformCore;

// 默认自检只运行定义、模拟工作流和失败关闭行为；这些字段不代表真机取证。
public static class V21BHoleSelfCheck
{
    public static async Task<HoleSelfCheckResult> RunAsync(string root, PlatformKernel platform, CancellationToken cancellationToken, string? outputRoot = null)
    {
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var issues = new List<string>();
        var observed = new Dictionary<string, List<bool>>(StringComparer.Ordinal);
        void Observe(string key, bool value)
        {
            if (!observed.TryGetValue(key, out var values)) observed[key] = values = [];
            values.Add(value);
        }
        var inputsReadable = true;
        outputRoot ??= Path.Combine(root, "output");
        var worker = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker");
        var assembly = worker?.GetType().Assembly;
        var handlerType = assembly?.GetType("SolidWorksWorker.Features.Hole.HoleHandler");
        var handler = handlerType is null ? null : Activator.CreateInstance(handlerType);
        bool Valid(string method, FeatureDefinition feature)
        {
            var methodInfo = handlerType?.GetMethod(method, [typeof(FeatureDefinition)])
                ?? throw new InvalidOperationException($"缺少孔 Handler 检查方法 {method}。");
            var result = methodInfo.Invoke(handler, [feature]);
            return result?.GetType().GetProperty("IsValid")?.GetValue(result) is true;
        }
        foreach (var name in new[] { "simple", "counterbore", "countersink", "tapped" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var inputPath = Path.Combine(root, "examples", $"hole_{name}_plate.json");
            CADModelSpec spec;
            FeatureDefinition feature;
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(inputPath, cancellationToken));
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("cad_model_spec", out var model) || model.ValueKind != JsonValueKind.Object)
                    throw new JsonException("缺少 cad_model_spec 对象。");
                spec = model.Deserialize<CADModelSpec>() ?? throw new JsonException("cad_model_spec 不能为 null。");
                SolidWorksWorkflowRouter.EnsureModelStructure(spec);
                var holes = spec.Features.Where(HoleGeometryValidation.IsExplicitHole).ToArray();
                if (holes.Length != 1) throw new JsonException("自检样例必须包含一个显式孔特征。");
                feature = holes[0];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or FormatException)
            {
                inputsReadable = false;
                issues.Add($"hole_self_check_input_invalid: {inputPath}: {exception.GetType().Name}: {exception.Message} 恢复该样例后重新运行 self-check。");
                continue;
            }
            try
            {
                var supported = false;
                var result = new HoleValidator().Validate(feature, spec);
                var skill = await new SolidWorksBuildPlanSkill().ExecuteAsync(new("v21-b-selfcheck", "build", spec, new Dictionary<string, string>()));
                if (result.IsValid && skill.Result is SolidWorksBuildPlan plan && worker is not null && new SolidWorksBuildPlanValidator().Validate(plan).IsPassed)
                {
                    var dryResult = await worker.ExecuteAsync(new WorkerInput("v21-b-selfcheck", "build",
                        new SolidWorksWorkerRequest("v21-b-selfcheck", plan, Path.Combine(outputRoot, "solidworks", "self-check", "v2_1_b", name), DryRun: true),
                        new Dictionary<string, string>()));
                    supported = dryResult.Status == WorkerOutputStatus.Completed && Valid("Validate", feature);
                }
                checks[$"{name}_hole_supported"] = supported;
                Observe("unverified_hole_blocks_real_execution", handler is not null &&
                    !Valid("ValidateEvidenceForRealExecution", feature) && !Valid("ValidateParameterProfileForRealExecution", feature));
                var invalid = feature with { Parameters = new Dictionary<string, string>(feature.Parameters) { ["diameter_mm"] = "0" } };
                var unknown = feature with { Parameters = new Dictionary<string, string>(feature.Parameters) { ["hole_type"] = "UnknownHole" } };
                Observe("hole_type_validation_supported", !new HoleValidator().Validate(invalid).IsValid && new HoleValidator().Validate(unknown).FailureStage == PartFamilyFailureStages.UnsupportedHoleType);
                if (!result.IsValid || result.Definition is null)
                {
                    issues.Add($"hole_self_check_fixture_rejected: {inputPath}: {string.Join("; ", result.Issues)} 核对样例参数和 HoleValidator 后重新运行 self-check。");
                    continue;
                }
                var h = result.Definition;
                var box = new GeometryBoundingBox(-50, 0, -30, 50, 20, 30);
                // 独立夹具体积：3²×12；3²×12+(6²-3²)×4；3²×12+锥台额外36；2.5²×12。
                var volume = 120000 - new Dictionary<string, double> { ["simple"] = 108, ["counterbore"] = 216, ["countersink"] = 144, ["tapped"] = 75 }[name] * Math.PI;
                var m = new MeasuredHole(feature.FeatureId, "Hole1", h.HoleType, h.ReferenceFace, new(0, 0), h.DiameterMm, 12, false,
                    h.CounterboreDiameterMm, h.CounterboreDepthMm, h.CountersinkDiameterMm, h.CountersinkAngleDeg,
                    name == "counterbore", name == "countersink", name == "tapped" ? new("hole_wizard_tapped", "ISO_METRIC", "M6", 1, 10, 5, true, false) : null);
                var measured = new MeasuredGeometry(true, box, box, 1, volume, null, volume,
                    FeatureTypes: ["ProfileFeature", "Extrusion", "HoleWzd"],
                    Features: [new("Sketch1", "ProfileFeature", 0), new("Boss1", "Extrusion", 0), new("Hole1", "HoleWzd", 0)], Holes: [m]);
                GeometryValidationReport Validate(MeasuredGeometry values) => new GeometryValidator().Validate(spec, values, ["sketch", "extrude_boss", "hole"]);
                Observe("hole_geometry_validation_supported", Validate(measured).FinalStatus == "Passed" && Validate(measured with { Holes = null }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { DepthMm = 1 }] }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { DiameterMm = 99 }] }).FinalStatus == "Failed");
                if (name == "tapped") Observe("tapped_hole_semantics_separated_from_simple_cut", Validate(measured with { Holes = [m with { Tapped = null }] }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { Tapped = m.Tapped! with { Representation = "geometric_cut" } }] }).FinalStatus == "Failed");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or TargetInvocationException)
            {
                if (exception.GetBaseException() is OperationCanceledException cancelled) throw cancelled;
                issues.Add($"hole_self_check_execution_error: {inputPath}: {exception.GetBaseException().Message} 检查模拟 Worker 与自检夹具后重新运行 self-check。");
            }
        }
        var strategies = assembly?.GetType("SolidWorksWorker.Features.Hole.HoleExecutionStrategies")?.GetProperty("All")?.GetValue(null) as System.Collections.IEnumerable;
        var evidence = strategies?.Cast<object>().Select(s => s.GetType().GetProperty("ApiEvidence")!.GetValue(s)!).ToArray() ?? [];
        checks["hole_api_evidence_required"] = evidence.Length == 4 && evidence.All(e =>
            e.GetType().GetProperty("Status")?.GetValue(e)?.ToString() == "unverified" &&
            e.GetType().GetProperty("AllowsRealExecution")?.GetValue(e) is false);
        foreach (var (key, values) in observed)
        {
            var expectedCount = key == "tapped_hole_semantics_separated_from_simple_cut" ? 1 : 4;
            // 未完成的检查不能伪造 false；已观测到的反例失败则仍然必须报回归。
            if (values.Contains(false) || values.Count == expectedCount) checks[key] = values.All(v => v);
        }
        checks["hole_self_check_inputs_readable"] = inputsReadable;
        try
        {
            checks["v2_1_b_documented"] = File.Exists(Path.Combine(root, "docs", "v2_1_b_hole_features.md")) &&
                File.ReadAllText(Path.Combine(root, "docs", "version_stage_index.md")).Contains("V2.1-B", StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            issues.Add($"hole_self_check_document_unreadable: {Path.Combine(root, "docs", "version_stage_index.md")}: {exception.Message} 恢复阶段说明后重新运行 self-check。");
        }
        var missing = CapabilityNames.Where(name => !checks.ContainsKey(name)).ToArray();
        var groupPassed = missing.Length == 0 && issues.Count == 0 && checks.Values.All(v => v);
        return new HoleSelfCheckResult(checks, issues, missing, groupPassed);
    }

    public static readonly IReadOnlyList<string> CapabilityNames = new[]
    {
        "simple_hole_supported", "counterbore_hole_supported", "countersink_hole_supported", "tapped_hole_supported",
        "hole_api_evidence_required", "hole_type_validation_supported", "hole_geometry_validation_supported",
        "tapped_hole_semantics_separated_from_simple_cut", "unverified_hole_blocks_real_execution", "v2_1_b_documented"
    };
}

public sealed record HoleSelfCheckResult(IReadOnlyDictionary<string, bool> Checks, IReadOnlyList<string> Issues,
    IReadOnlyList<string> UnobservedCapabilities, bool GroupPassed)
{
    public bool? Value(string name) => Checks.TryGetValue(name, out var value) ? value : null;
}
