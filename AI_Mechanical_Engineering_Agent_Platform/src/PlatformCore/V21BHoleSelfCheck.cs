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
    public static async Task<IReadOnlyDictionary<string, bool>> RunAsync(string root, PlatformKernel platform, CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, bool>(StringComparer.Ordinal);
        var worker = platform.WorkerRegistry.GetByName("FakeSolidWorksWorker");
        var assembly = worker?.GetType().Assembly;
        var handlerType = assembly?.GetType("SolidWorksWorker.Features.Hole.HoleHandler");
        var handler = handlerType is null ? null : Activator.CreateInstance(handlerType);
        bool Valid(string method, FeatureDefinition feature)
        {
            var result = handlerType?.GetMethod(method, [typeof(FeatureDefinition)])?.Invoke(handler, [feature]);
            return result?.GetType().GetProperty("IsValid")?.GetValue(result) is true;
        }
        var allBlocked = true; var typeValidation = true; var geometry = true; var semantics = true;
        foreach (var name in new[] { "simple", "counterbore", "countersink", "tapped" })
        {
            var supported = false;
            try
            {
                using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "examples", $"hole_{name}_plate.json"), cancellationToken));
                var spec = doc.RootElement.GetProperty("cad_model_spec").Deserialize<CADModelSpec>()!;
                var feature = spec.Features.Single(HoleGeometryValidation.IsExplicitHole);
                var result = new HoleValidator().Validate(feature, spec);
                var skill = await new SolidWorksBuildPlanSkill().ExecuteAsync(new("v21-b-selfcheck", "build", spec, new Dictionary<string, string>()));
                if (result.IsValid && skill.Result is SolidWorksBuildPlan plan && worker is not null && new SolidWorksBuildPlanValidator().Validate(plan).IsPassed)
                {
                    var dryResult = await worker.ExecuteAsync(new WorkerInput("v21-b-selfcheck", "build",
                        new SolidWorksWorkerRequest("v21-b-selfcheck", plan, Path.Combine(root, "output", "solidworks", "self-check", "v2_1_b", name), DryRun: true),
                        new Dictionary<string, string>()));
                    supported = dryResult.Status == WorkerOutputStatus.Completed && Valid("Validate", feature);
                }
                allBlocked &= !Valid("ValidateEvidenceForRealExecution", feature) && !Valid("ValidateParameterProfileForRealExecution", feature);
                var invalid = feature with { Parameters = new Dictionary<string, string>(feature.Parameters) { ["diameter_mm"] = "0" } };
                var unknown = feature with { Parameters = new Dictionary<string, string>(feature.Parameters) { ["hole_type"] = "UnknownHole" } };
                typeValidation &= !new HoleValidator().Validate(invalid).IsValid && new HoleValidator().Validate(unknown).FailureStage == PartFamilyFailureStages.UnsupportedHoleType;
                var h = result.Definition!;
                var box = new GeometryBoundingBox(-50, 0, -30, 50, 20, 30);
                var volume = 120000 - new Dictionary<string, double> { ["simple"] = 108, ["counterbore"] = 216, ["countersink"] = 144, ["tapped"] = 75 }[name] * Math.PI;
                var m = new MeasuredHole(feature.FeatureId, "Hole1", h.HoleType, h.ReferenceFace, new(0, 0), h.DiameterMm, 12, false,
                    h.CounterboreDiameterMm, h.CounterboreDepthMm, h.CountersinkDiameterMm, h.CountersinkAngleDeg,
                    name == "counterbore", name == "countersink", name == "tapped" ? new("hole_wizard_tapped", "ISO_METRIC", "M6", 1, 10, 5, true, false) : null);
                var measured = new MeasuredGeometry(true, box, box, 1, volume, null, volume,
                    FeatureTypes: ["ProfileFeature", "Extrusion", "HoleWzd"],
                    Features: [new("Sketch1", "ProfileFeature", 0), new("Boss1", "Extrusion", 0), new("Hole1", "HoleWzd", 0)], Holes: [m]);
                GeometryValidationReport Validate(MeasuredGeometry values) => new GeometryValidator().Validate(spec, values, ["sketch", "extrude_boss", "hole"]);
                geometry &= Validate(measured).FinalStatus == "Passed" && Validate(measured with { Holes = null }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { DepthMm = 1 }] }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { DiameterMm = 99 }] }).FinalStatus == "Failed";
                if (name == "tapped") semantics &= Validate(measured with { Holes = [m with { Tapped = null }] }).FinalStatus == "Failed" &&
                    Validate(measured with { Holes = [m with { Tapped = m.Tapped! with { Representation = "geometric_cut" } }] }).FinalStatus == "Failed";
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or TargetInvocationException)
            { supported = false; allBlocked = false; typeValidation = false; geometry = false; semantics = false; }
            checks[$"{name}_hole_supported"] = supported;
        }
        var strategies = assembly?.GetType("SolidWorksWorker.Features.Hole.HoleExecutionStrategies")?.GetProperty("All")?.GetValue(null) as System.Collections.IEnumerable;
        var evidence = strategies?.Cast<object>().Select(s => s.GetType().GetProperty("ApiEvidence")!.GetValue(s)!).ToArray() ?? [];
        checks["hole_api_evidence_required"] = evidence.Length == 4 && evidence.All(e =>
            e.GetType().GetProperty("Status")?.GetValue(e)?.ToString() == "unverified" &&
            e.GetType().GetProperty("AllowsRealExecution")?.GetValue(e) is false);
        checks["hole_type_validation_supported"] = typeValidation;
        checks["hole_geometry_validation_supported"] = geometry;
        checks["tapped_hole_semantics_separated_from_simple_cut"] = semantics;
        checks["unverified_hole_blocks_real_execution"] = allBlocked;
        checks["hole_feature_regression_tests_passed"] = checks.Values.All(v => v);
        checks["v2_1_b_documented"] = File.Exists(Path.Combine(root, "docs", "v2_1_b_hole_features.md")) &&
            File.ReadAllText(Path.Combine(root, "docs", "version_stage_index.md")).Contains("V2.1-B", StringComparison.Ordinal);
        return checks;
    }
}
