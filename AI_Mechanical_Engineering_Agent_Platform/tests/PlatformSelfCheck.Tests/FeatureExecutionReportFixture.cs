using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

/// <summary>
/// V2.0-E 起所有零件族统一走通用 FeatureGraph 分支，该分支额外要求
/// feature_execution_report.json，且其中的 handler 版本与 diagnostic 路径
/// 必须与生产证据一致。本助手直接从已注册 Handler 的 ApiEvidence 派生，
/// 因此证据一旦重采，测试 fixture 自动跟随，不会出现手写 JSON 漂移。
/// </summary>
internal static class FeatureExecutionReportFixture
{
    public static IReadOnlyList<object> FeatureResults(params string[] featureTypes)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        var handlers = registry.GetAll();
        var types = featureTypes.Length == 0
            ? new[] { FeatureTypes.ExtrudeBoss }
            : featureTypes;

        return types.Select((featureType, index) =>
        {
            var handler = handlers.Single(item =>
                string.Equals(item.FeatureType, featureType, StringComparison.OrdinalIgnoreCase));
            var evidence = handler.ApiEvidence;
            return (object)new
            {
                feature_id = $"feature-{index}",
                feature_type = featureType,
                handler_name = $"{handler.HandlerId}@{handler.HandlerVersion}",
                handler_version = handler.HandlerVersion,
                api_evidence_status = evidence.Status,
                result_object_validated = true,
                rebuild_passed = true,
                geometry_change_validated = true,
                adapter_id = RealSolidWorksFeatureAdapter.AdapterIdentifier,
                adapter_version = RealSolidWorksFeatureAdapter.CurrentAdapterVersion,
                evidence_id = evidence.EvidenceId,
                evidence_handler_version = evidence.HandlerVersion,
                evidence_parameter_profile = evidence.ParameterProfile,
                evidence_solid_works_version = evidence.SolidWorksVersion,
                evidence_diagnostic_run_path = evidence.DiagnosticRunPath,
                evidence_source_revision = evidence.SourceRevision,
                failure_stage = (string?)null,
                issues = Array.Empty<string>()
            };
        }).ToArray();
    }

    public static string RuntimeVersion(string featureType = FeatureTypes.ExtrudeBoss) =>
        FeatureHandlerRegistry.CreateDefault().GetAll()
            .Single(item => string.Equals(item.FeatureType, featureType, StringComparison.OrdinalIgnoreCase))
            .ApiEvidence.SolidWorksVersion ?? "31.5.0";

    public static string Write(string directory, params string[] featureTypes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "feature_execution_report.json");
        var payload = new
        {
            real_cad_executed = true,
            real_cad_connected = true,
            all_features_executed = true,
            all_result_objects_validated = true,
            all_rebuilds_passed = true,
            all_geometry_changes_validated = true,
            artifacts_validated = true,
            execution_mode = PartFamilyExecutionModes.GenericFeatureGraph,
            execution_strategy = SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
            final_status = "Passed",
            solidworks_version = RuntimeVersion(),
            feature_results = FeatureResults(featureTypes)
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
