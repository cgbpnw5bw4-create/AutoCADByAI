using System.Security.Cryptography;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>轴类、夹套的受限生产证据：绑定诊断、输入、两种 CAD 文件及独立重开实测。</summary>
public static class PartFamilyProductionEvidencePolicy
{
    private const string EvidenceId = "sha256:d4fa0595d847f121e539e0720d8da83b38ae9e75c4e675686898ee52db575303";
    private const string DiagnosticRunPath = "evidence/solidworks/v2_2_d_final/family_acceptance.json";
    private const string SourceRevision = "feature-execution-source-sha256:e484715785d2ba21ea7ae3efbca5238f37a4fbc988cb37b9658603d33e22a048";
    private const string SolidWorksVersion = "31.5.0";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static bool RequiresEvidence(string partType) =>
        string.Equals(partType, "shaft_basic", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(partType, "jacket_basic", StringComparison.OrdinalIgnoreCase);

    public static string ComputePlanFingerprint(SolidWorksBuildPlan plan)
    {
        var shape = new
        {
            plan.PartType, plan.Unit, plan.ExecutionStrategy,
            Dimensions = plan.Dimensions?.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(),
            Operations = plan.Operations.Where(o => o.OperationType is not ("SavePart" or "ExportStep"))
                .Select(o => new { o.OperationId, o.OperationType, o.SketchPlane,
                    Parameters = o.Parameters.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray(), o.DependsOn })
        };
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(shape))).ToLowerInvariant();
    }

    public static bool IsActive(string partType, string? evidenceRoot = null) =>
        Validate(partType, null, evidenceRoot, null).IsValid;

    public static FeatureHandlerValidationResult ValidateForRealExecution(
        SolidWorksBuildPlan plan, string? evidenceRoot = null, string? actualVersion = null) =>
        !RequiresEvidence(plan.PartType) ? FeatureHandlerValidationResult.Passed() : Validate(plan.PartType, plan, evidenceRoot, actualVersion);

    private static FeatureHandlerValidationResult Validate(string partType, SolidWorksBuildPlan? requested, string? evidenceRoot, string? actualVersion)
    {
        try
        {
            Require(RequiresEvidence(partType), "不属于本阶段受限零件族。");
            partType = partType.ToLowerInvariant();
            var path = evidenceRoot is null ? FeatureExecutionEvidencePolicy.ResolveEvidencePath(DiagnosticRunPath)
                : Path.Combine(evidenceRoot, DiagnosticRunPath.Replace('/', Path.DirectorySeparatorChar));
            Require(File.Exists(path), "缺少零件族独立重开验收清单。");
            Require("sha256:" + Hash(path) == EvidenceId, "零件族验收清单身份不匹配。");
            Require(FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision() == SourceRevision, "零件族证据源码已过期。");
            Require(actualVersion is null || actualVersion == SolidWorksVersion, "SolidWorks 运行版本与证据不匹配。");
            using var manifest = JsonDocument.Parse(File.ReadAllText(path));
            var root = manifest.RootElement;
            Require(root.GetProperty("source_revision").GetString() == SourceRevision, "清单源码指纹不匹配。");
            Require(root.GetProperty("solid_works_version").GetString() == SolidWorksVersion, "清单版本不匹配。");
            var cases = root.GetProperty("cases").EnumerateArray().Where(c => c.GetProperty("part_type").GetString() == partType).ToArray();
            Require(cases.Length >= (partType == "shaft_basic" ? 2 : 1), "缺少完整零件族候选覆盖。");
            var matched = requested is null;
            foreach (var candidate in cases)
            {
                var diagnosticPath = ValidateFile(candidate.GetProperty("diagnostic_report"));
                var inputPath = ValidateFile(candidate.GetProperty("input"));
                using var diagnostic = JsonDocument.Parse(File.ReadAllText(diagnosticPath));
                var report = diagnostic.RootElement;
                Require(report.GetProperty("candidate_only").GetBoolean() && !report.GetProperty("main_workflow_accepted").GetBoolean() &&
                    !report.GetProperty("quality_gate_passed").GetBoolean() && report.GetProperty("real_cad_executed").GetBoolean() &&
                    report.GetProperty("solid_works_connected").GetBoolean() && report.GetProperty("final_status").GetString() == "CandidatePassed" &&
                    report.GetProperty("deliverable_status").GetString() == "NotDeliverable" && report.GetProperty("solid_works_version").GetString() == SolidWorksVersion,
                    "候选诊断身份或执行状态无效。");
                var plan = report.GetProperty("build_plan").Deserialize<SolidWorksBuildPlan>(JsonOptions) ?? throw new InvalidDataException("缺少候选建模计划。");
                Require(plan.PartType == partType && plan.Unit == "mm", "候选零件族或单位不匹配。");
                Require(PartTypeRegistry.CreateDefault().TryGetDefinition(partType, out var definition), "未知零件族。");
                Require(Path.GetFullPath(report.GetProperty("input_path").GetString()!) == inputPath, "候选输入身份不匹配。");
                using var input = JsonDocument.Parse(File.ReadAllText(inputPath));
                var specJson = input.RootElement.TryGetProperty("cad_model_spec", out var nested) ? nested : input.RootElement;
                var spec = specJson.Deserialize<CADModelSpec>(JsonOptions) ?? throw new InvalidDataException("候选输入不能解析。");
                var compiled = definition.GenerateBuildPlan("evidence-input-verification", spec);
                Require(compiled.IsSuccess && compiled.BuildPlan is not null && ComputePlanFingerprint(compiled.BuildPlan) == ComputePlanFingerprint(plan),
                    "输入参数与实际候选建模计划不一致。");
                var operations = plan.Operations.Where(o => o.OperationType is not ("SavePart" or "ExportStep")).ToArray();
                var features = report.GetProperty("feature_handler_reports").EnumerateArray().ToArray();
                Require(features.Length == operations.Length && features.Select(f => f.GetProperty("feature_id").GetString())
                    .SequenceEqual(operations.Select(o => o.Parameters.GetValueOrDefault("feature_id", o.Parameters.GetValueOrDefault("sketch_id", o.OperationId)))),
                    "候选缺少完整、顺序对应的特征执行记录。");
                foreach (var feature in features)
                    Require(feature.GetProperty("evidence_source_revision").GetString() == SourceRevision &&
                        feature.GetProperty("evidence_solid_works_version").GetString() == SolidWorksVersion &&
                        feature.GetProperty("api_evidence_status").GetString() == "diagnostic_candidate" &&
                        !string.IsNullOrWhiteSpace(feature.GetProperty("adapter_id").GetString()) &&
                        feature.GetProperty("result_object_validated").GetBoolean() && feature.GetProperty("rebuild_passed").GetBoolean() &&
                        feature.GetProperty("geometry_change_validated").GetBoolean() && feature.GetProperty("issues").GetArrayLength() == 0 &&
                        feature.GetProperty("failure_stage").ValueKind == JsonValueKind.Null, "候选节点缺少当前源码、API 执行或成功读回证据。");
                var expected = definition.DescribeExpectedGeometry(plan);
                Require(expected?.AxialSections is { Count: > 0 } && definition.ReviewBuildPlan(plan).Count == 0, "候选计划缺少可验收截面或评审失败。");
                foreach (var format in new[] { "native", "step" })
                {
                    var artifact = ValidateFile(candidate.GetProperty(format));
                    Require(Path.GetExtension(artifact).Equals(format == "native" ? ".SLDPRT" : ".step", StringComparison.OrdinalIgnoreCase), "产物格式身份错误。");
                    Require(format != "step" || CadArtifactContentValidator.TryValidateStepFile(artifact, out _), "产物不是真实 STEP 交换文件。");
                    Require(Path.GetFullPath(report.GetProperty(format == "native" ? "model" : "step").GetProperty("file_path").GetString()!) == artifact, "诊断产物路径不匹配。");
                    Require(report.GetProperty(format == "native" ? "model" : "step").GetProperty("size_bytes").GetInt64() == new FileInfo(artifact).Length,
                        "诊断记录与实际产物长度不匹配。");
                    var probePath = ValidateFile(candidate.GetProperty(format + "_probe"));
                    using var probe = JsonDocument.Parse(File.ReadAllText(probePath));
                    var result = probe.RootElement;
                    Require(result.GetProperty("read_success").GetBoolean(), "CAD 独立重开失败。");
                    var reopened = Path.GetFullPath(result.GetProperty("input").GetString()!);
                    Require(File.Exists(reopened) && Hash(reopened) == Hash(artifact) && new FileInfo(reopened).Length == new FileInfo(artifact).Length,
                        "独立重开输入与产物字节不一致。");
                    var geometry = result.GetProperty("geometry").Deserialize<MeasuredGeometry>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    Require(geometry is not null && geometry.SolidWorksVersion == SolidWorksVersion, "独立重开缺少匹配的实际运行版本。");
                    var geometryCheck = PartGeometryValidator.Validate(expected!, geometry);
                    Require(geometryCheck.IsValid, "独立重开几何不符合参数截面、体积或单实体要求：" + string.Join("; ", geometryCheck.Issues));
                }
                if (requested is not null && ComputePlanFingerprint(plan) == ComputePlanFingerprint(requested)) matched = true;
            }
            Require(matched, "当前参数及建模操作尚无独立 CAD 证据覆盖。");
            return FeatureHandlerValidationResult.Passed();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return FeatureHandlerValidationResult.Failed(PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient, ex.Message);
        }
    }

    private static string ValidateFile(JsonElement file)
    {
        var path = FeatureExecutionEvidencePolicy.ResolveEvidencePath(file.GetProperty("path").GetString()!);
        Require(File.Exists(path) && new FileInfo(path).Length == file.GetProperty("size_bytes").GetInt64() &&
            Hash(path) == file.GetProperty("sha256").GetString(), $"证据物理文件与指纹不一致：{path}");
        return path;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
