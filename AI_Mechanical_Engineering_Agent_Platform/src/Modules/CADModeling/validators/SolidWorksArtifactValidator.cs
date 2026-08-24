using DomainSchemas;
using QualityGate;
using System.Text.Json;

namespace PlatformCore.Modules.CADModeling.Validators;

public sealed class SolidWorksArtifactValidator : IValidator
{
    private readonly string? _configuredOutputRoot;
    private readonly PartTypeRegistry _partTypeRegistry;

    public SolidWorksArtifactValidator(
        string? configuredOutputRoot = null,
        PartTypeRegistry? partTypeRegistry = null)
    {
        _configuredOutputRoot = string.IsNullOrWhiteSpace(configuredOutputRoot)
            ? null
            : NormalizeDirectory(configuredOutputRoot);
        _partTypeRegistry = partTypeRegistry ?? PartTypeRegistry.CreateDefault();
    }

    public string Name => "solidworks-artifact-validator";

    public ReviewReport Validate(object payload)
    {
        var issues = new List<string>();
        if (payload is not SolidWorksWorkerResult result)
        {
            issues.Add("payload must be SolidWorksWorkerResult.");
            return Report(issues);
        }

        var isFakeMode = string.Equals(result.ExecutionMode, "Fake", StringComparison.OrdinalIgnoreCase);
        var isRealGenericFeatureGraphMode = string.Equals(
            result.ExecutionMode,
            PartFamilyExecutionModes.GenericFeatureGraph,
            StringComparison.OrdinalIgnoreCase);
        var partFamilyDefinition = _partTypeRegistry.GetAll().FirstOrDefault(definition =>
            string.Equals(definition.RealExecutionMode, result.ExecutionMode, StringComparison.OrdinalIgnoreCase));

        // V2.0-E 起所有零件族统一走通用 FeatureGraph 执行模式。此时必须使用通用分支，
        // 因为它额外要求 feature_execution_report.json；退回零件族分支会放松校验。
        var isRealPartFamilyBuildMode = partFamilyDefinition is not null && !isRealGenericFeatureGraphMode;
        var isRealDrawingMode = string.Equals(result.ExecutionMode, "RealDrawingBasicViews", StringComparison.OrdinalIgnoreCase);
        var isRealDrawingDimensionMode = string.Equals(result.ExecutionMode, "RealDrawingDimensions", StringComparison.OrdinalIgnoreCase);
        var isRealDrawingTitleBlockMode = string.Equals(result.ExecutionMode, "RealDrawingTitleBlock", StringComparison.OrdinalIgnoreCase);

        if (!isFakeMode && !isRealPartFamilyBuildMode && !isRealGenericFeatureGraphMode &&
            !isRealDrawingMode && !isRealDrawingDimensionMode && !isRealDrawingTitleBlockMode)
        {
            var familyModes = string.Join(", ", _partTypeRegistry.GetAll().Select(definition => definition.RealExecutionMode));
            issues.Add(
                $"execution_mode must be Fake, {familyModes}, {PartFamilyExecutionModes.GenericFeatureGraph}, " +
                "RealDrawingBasicViews, RealDrawingDimensions, or RealDrawingTitleBlock.");
        }

        if (result.GeneratedArtifacts.Count == 0)
        {
            issues.Add("generated_artifacts must not be empty.");
        }

        if (isFakeMode)
        {
            ValidateFakeMode(result, issues);
        }
        else if (isRealPartFamilyBuildMode)
        {
            ValidateRealPartFamilyBuildMode(result, partFamilyDefinition!, issues);
        }
        else if (isRealGenericFeatureGraphMode)
        {
            ValidateRealGenericFeatureGraphMode(result, issues);
        }
        else if (isRealDrawingMode)
        {
            ValidateRealDrawingMode(result, issues);
        }
        else if (isRealDrawingDimensionMode)
        {
            ValidateRealDrawingDimensionMode(result, issues);
        }
        else if (isRealDrawingTitleBlockMode)
        {
            ValidateRealDrawingTitleBlockMode(result, issues);
        }

        return Report(issues);
    }

    private void ValidateFakeMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must remain false in Fake mode.");
        }

        var hasBuildReport = false;
        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!File.Exists(fullPath))
            {
                issues.Add($"fake artifact does not exist: {artifact.FilePath}.");
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length <= 0)
            {
                issues.Add($"fake artifact is empty: {artifact.FilePath}.");
            }

            if (_configuredOutputRoot is not null)
            {
                if (!IsUnderRoot(fullPath, _configuredOutputRoot))
                {
                    issues.Add($"fake artifact path must be under configured output/solidworks root: {artifact.FilePath}.");
                }
            }
            else if (!IsUnderOutputSolidWorksSegment(fullPath))
            {
                issues.Add($"fake artifact path must be under output/solidworks: {artifact.FilePath}.");
            }

            if (Path.GetFileName(fullPath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase))
            {
                hasBuildReport = true;
            }
        }

        if (!hasBuildReport)
        {
            issues.Add("build_report.json was not generated.");
        }
    }

    private void ValidateRealPartFamilyBuildMode(
        SolidWorksWorkerResult result,
        IPartFamilyDefinition definition,
        List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add($"real_cad_executed must be true for {definition.RealExecutionMode}.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add($"real_cad_connected must be true for {definition.RealExecutionMode}.");
        }

        var part = FindArtifact(result, ".SLDPRT");
        var step = FindArtifact(result, ".STEP");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(part, ".SLDPRT", issues);
        ValidateRealArtifact(step, ".STEP", issues);
        ValidateRealArtifact(report, "build_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealBuildReport(report.FilePath, definition, issues);
        }
    }

    private void ValidateRealDrawingMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingBasicViews.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingBasicViews.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("drawing_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "drawing_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingReport(report.FilePath, issues);
        }
    }

    private void ValidateRealGenericFeatureGraphMode(
        SolidWorksWorkerResult result,
        List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add($"real_cad_executed must be true for {PartFamilyExecutionModes.GenericFeatureGraph}.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add($"real_cad_connected must be true for {PartFamilyExecutionModes.GenericFeatureGraph}.");
        }

        var part = FindArtifact(result, ".SLDPRT");
        var step = FindArtifact(result, ".STEP");
        var buildReport = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase));
        var featureReport = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("feature_execution_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(part, ".SLDPRT", issues);
        ValidateRealArtifact(step, ".STEP", issues);
        ValidateRealArtifact(buildReport, "build_report.json", issues);
        ValidateRealArtifact(featureReport, "feature_execution_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real generic feature artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (buildReport is not null && File.Exists(buildReport.FilePath))
        {
            ValidateGenericBuildReport(buildReport.FilePath, issues);
        }

        if (featureReport is not null && File.Exists(featureReport.FilePath))
        {
            ValidateFeatureExecutionReport(featureReport.FilePath, issues);
        }
    }

    private void ValidateRealDrawingDimensionMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingDimensions.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingDimensions.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("dimension_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "dimension_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing dimension artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingDimensionReport(report.FilePath, issues);
        }
    }

    private void ValidateRealDrawingTitleBlockMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingTitleBlock.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingTitleBlock.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("title_block_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "title_block_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing title block artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingTitleBlockReport(report.FilePath, issues);
        }
    }

    private static SolidWorksArtifact? FindArtifact(SolidWorksWorkerResult result, string extension) =>
        result.GeneratedArtifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.ExpectedExtension, extension, StringComparison.OrdinalIgnoreCase) ||
            artifact.FilePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static void ValidateRealArtifact(SolidWorksArtifact? artifact, string name, List<string> issues)
    {
        if (artifact is null)
        {
            issues.Add($"{name} artifact was not generated.");
            return;
        }

        var fullPath = Path.GetFullPath(artifact.FilePath);
        if (!File.Exists(fullPath))
        {
            issues.Add($"{name} artifact does not exist: {artifact.FilePath}.");
            return;
        }

        if (new FileInfo(fullPath).Length <= 0)
        {
            issues.Add($"{name} artifact is empty: {artifact.FilePath}.");
            return;
        }

        if ((fullPath.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase) ||
             fullPath.EndsWith(".STP", StringComparison.OrdinalIgnoreCase)) &&
            !CadArtifactContentValidator.TryValidateStepFile(fullPath, out var contentIssue))
        {
            issues.Add($"{name} artifact content is not valid STEP: {contentIssue}");
        }
    }

    private static void ValidateRealBuildReport(
        string reportPath,
        IPartFamilyDefinition definition,
        List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("real_cad_executed", out var executed) ||
                executed.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report real_cad_executed must be true.");
            }

            if (!root.TryGetProperty("execution_mode", out var executionMode) ||
                !string.Equals(executionMode.GetString(), definition.RealExecutionMode, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"build_report execution_mode must be {definition.RealExecutionMode}.");
            }

            if (!root.TryGetProperty("part_type", out var partType) ||
                !string.Equals(partType.GetString(), definition.PartType, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"build_report part_type must be {definition.PartType}.");
            }

            if (!root.TryGetProperty("real_cad_connected", out var connected) ||
                connected.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report real_cad_connected must be true.");
            }

            if (!root.TryGetProperty("sldprt_save_success", out var sldprtSaveSuccess) ||
                sldprtSaveSuccess.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report sldprt_save_success must be true.");
            }

            if (!root.TryGetProperty("step_export_success", out var stepExportSuccess) ||
                stepExportSuccess.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report step_export_success must be true.");
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("build_report final_status must be Passed.");
            }

            ValidateGeometryEvidence(root, issues);
        }
        catch (JsonException ex)
        {
            issues.Add($"build_report.json is invalid: {ex.Message}.");
        }
    }

    /// <summary>
    /// 几何证据校验，由 build_report 自描述驱动：报告携带期望体数与容差，
    /// 因此本校验器不需要持有任何零件族专属几何知识。
    /// V2.0-E 之前该检查只挂在 part-family 分支上，统一执行路线后两端同时
    /// 不可达；现在两条分支共用同一份实现。
    /// </summary>
    private static void ValidateGeometryEvidence(JsonElement root, List<string> issues)
    {
        var status = root.TryGetProperty("geometry_validation_status", out var statusProperty) &&
                     statusProperty.ValueKind == JsonValueKind.String
            ? statusProperty.GetString()
            : null;

        // 零件族尚未声明几何期望。这是一个可见缺口（见 docs/cad_capability_matrix.md），
        // 不是默认通过——但也不能在此处凭空要求证据。
        if (string.IsNullOrWhiteSpace(status) ||
            string.Equals(status, "NotDeclared", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        RequireTrue(root, "geometry_validation_attempted", "build_report", issues);
        if (!string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"build_report geometry_validation_status must be Passed, actual={status}.");
        }

        var hasExpectedBodies = root.TryGetProperty("expected_body_count", out var expectedBodies) &&
                                expectedBodies.ValueKind == JsonValueKind.Number &&
                                expectedBodies.TryGetInt32(out var expectedBodyCount);
        var hasMeasuredBodies = root.TryGetProperty("measured_body_count", out var measuredBodies) &&
                                measuredBodies.ValueKind == JsonValueKind.Number &&
                                measuredBodies.TryGetInt32(out var measuredBodyCount);
        if (!hasExpectedBodies || !hasMeasuredBodies)
        {
            issues.Add("build_report must contain expected_body_count and measured_body_count.");
        }
        else if (expectedBodies.GetInt32() != measuredBodies.GetInt32())
        {
            issues.Add(
                $"build_report measured_body_count {measuredBodies.GetInt32()} does not match " +
                $"expected_body_count {expectedBodies.GetInt32()}.");
        }

        if (!TryPositiveFiniteNumber(root, "expected_volume_cubic_mm", out var expectedVolume) ||
            !TryPositiveFiniteNumber(root, "measured_volume_cubic_mm", out var measuredVolume))
        {
            issues.Add("build_report geometry evidence must contain finite positive expected and measured volumes.");
            return;
        }

        var relativeTolerance =
            root.TryGetProperty("geometry_volume_relative_tolerance", out var toleranceProperty) &&
            toleranceProperty.ValueKind == JsonValueKind.Number &&
            toleranceProperty.TryGetDouble(out var declaredTolerance) &&
            double.IsFinite(declaredTolerance) &&
            declaredTolerance > 0d
                ? declaredTolerance
                : JacketGeometryValidator.VolumeRelativeTolerance;
        var tolerance = Math.Max(1d, expectedVolume * relativeTolerance);
        if (Math.Abs(expectedVolume - measuredVolume) > tolerance)
        {
            issues.Add("build_report measured volume is outside the allowed tolerance.");
        }
    }

    private static bool TryPositiveFiniteNumber(JsonElement root, string name, out double value)
    {
        value = 0d;
        return root.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value) &&
               double.IsFinite(value) &&
               value > 0d;
    }

    private static void ValidateGenericBuildReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            RequireTrue(root, "real_cad_executed", "build_report", issues);
            RequireTrue(root, "real_cad_connected", "build_report", issues);
            RequireTrue(root, "sldprt_save_success", "build_report", issues);
            RequireTrue(root, "step_export_success", "build_report", issues);
            RequireString(
                root,
                "execution_mode",
                PartFamilyExecutionModes.GenericFeatureGraph,
                "build_report",
                issues);
            RequireString(
                root,
                "execution_strategy",
                SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                "build_report",
                issues);
            RequireString(root, "final_status", "Passed", "build_report", issues);
            ValidateFeatureResults(root, "feature_handler_reports", "build_report", issues);

            // 统一执行路线下的几何证据校验。声明了几何期望的零件族必须在此
            // 独立复核一次，避免 V2.0-E 迁移时出现的「生产端与消费端同时失联」。
            ValidateGeometryEvidence(root, issues);
        }
        catch (JsonException ex)
        {
            issues.Add($"build_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateFeatureExecutionReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            RequireTrue(root, "real_cad_executed", "feature_execution_report", issues);
            RequireTrue(root, "real_cad_connected", "feature_execution_report", issues);
            RequireTrue(root, "all_features_executed", "feature_execution_report", issues);
            RequireTrue(root, "all_result_objects_validated", "feature_execution_report", issues);
            RequireTrue(root, "all_rebuilds_passed", "feature_execution_report", issues);
            RequireTrue(root, "all_geometry_changes_validated", "feature_execution_report", issues);
            RequireTrue(root, "artifacts_validated", "feature_execution_report", issues);
            RequireString(
                root,
                "execution_mode",
                PartFamilyExecutionModes.GenericFeatureGraph,
                "feature_execution_report",
                issues);
            RequireString(
                root,
                "execution_strategy",
                SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                "feature_execution_report",
                issues);
            RequireString(root, "final_status", "Passed", "feature_execution_report", issues);
            ValidateFeatureResults(root, "feature_results", "feature_execution_report", issues);
        }
        catch (JsonException ex)
        {
            issues.Add($"feature_execution_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateFeatureResults(
        JsonElement root,
        string propertyName,
        string reportName,
        List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var results) ||
            results.ValueKind != JsonValueKind.Array ||
            results.GetArrayLength() == 0)
        {
            issues.Add($"{reportName} {propertyName} must contain at least one feature result.");
            return;
        }

        var runtimeVersion =
            root.TryGetProperty("solidworks_version", out var versionProperty) &&
            versionProperty.ValueKind == JsonValueKind.String
                ? versionProperty.GetString()
                : null;
        if (string.IsNullOrWhiteSpace(runtimeVersion))
        {
            issues.Add($"{reportName} solidworks_version must be present.");
        }

        var index = 0;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("api_evidence_status", out var evidence) ||
                !string.Equals(evidence.GetString(), "verified", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add($"{reportName} feature result {index} must bind verified API evidence.");
            }

            if (!result.TryGetProperty("result_object_validated", out var validated) ||
                validated.ValueKind != JsonValueKind.True)
            {
                issues.Add($"{reportName} feature result {index} did not validate its result object.");
            }

            if (!result.TryGetProperty("rebuild_passed", out var rebuild) ||
                rebuild.ValueKind != JsonValueKind.True)
            {
                issues.Add($"{reportName} feature result {index} did not pass rebuild validation.");
            }

            if (!result.TryGetProperty("geometry_change_validated", out var geometry) ||
                geometry.ValueKind != JsonValueKind.True)
            {
                issues.Add($"{reportName} feature result {index} did not validate its geometry change.");
            }

            var adapterId = ReadString(result, "adapter_id");
            var adapterVersion = ReadString(result, "adapter_version");
            if (!string.Equals(
                    adapterId,
                    "solidworks.real-feature-adapter",
                    StringComparison.Ordinal) ||
                !string.Equals(adapterVersion, "2.0-c.2", StringComparison.Ordinal))
            {
                issues.Add(
                    $"{reportName} feature result {index} must bind " +
                    "solidworks.real-feature-adapter@2.0-c.2.");
            }

            foreach (var evidenceField in new[]
                     {
                         "evidence_id",
                         "evidence_handler_version",
                         "evidence_parameter_profile",
                         "evidence_solid_works_version",
                         "evidence_diagnostic_run_path",
                         "evidence_source_revision"
                     })
            {
                if (!result.TryGetProperty(evidenceField, out var evidenceValue) ||
                    string.IsNullOrWhiteSpace(evidenceValue.GetString()))
                {
                    issues.Add(
                        $"{reportName} feature result {index} must bind {evidenceField}.");
                }
            }

            var featureType = ReadString(result, "feature_type");
            var handlerName = ReadString(result, "handler_name");
            var evidenceHandlerVersion = ReadString(result, "evidence_handler_version");
            var evidenceSolidWorksVersion = ReadString(
                result,
                "evidence_solid_works_version");
            var evidenceDiagnosticRunPath = ReadString(
                result,
                "evidence_diagnostic_run_path");
            var evidenceSourceRevision = ReadString(
                result,
                "evidence_source_revision");
            if (string.IsNullOrWhiteSpace(handlerName) ||
                string.IsNullOrWhiteSpace(evidenceHandlerVersion) ||
                !handlerName.Contains(
                    $"@{evidenceHandlerVersion}",
                    StringComparison.Ordinal))
            {
                issues.Add(
                    $"{reportName} feature result {index} handler version does not match its evidence.");
            }

            if (string.IsNullOrWhiteSpace(runtimeVersion) ||
                !string.Equals(
                    runtimeVersion,
                    evidenceSolidWorksVersion,
                    StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"{reportName} feature result {index} evidence SolidWorks version " +
                    "does not match the connected runtime.");
            }

            ValidateDiagnosticEvidence(
                reportName,
                index,
                featureType,
                handlerName,
                evidenceHandlerVersion,
                evidenceSolidWorksVersion,
                evidenceDiagnosticRunPath,
                evidenceSourceRevision,
                adapterId,
                adapterVersion,
                issues);

            if (result.TryGetProperty("failure_stage", out var failureStage) &&
                failureStage.ValueKind != JsonValueKind.Null &&
                !string.IsNullOrWhiteSpace(failureStage.GetString()))
            {
                issues.Add($"{reportName} feature result {index} contains failure_stage={failureStage.GetString()}.");
            }

            index++;
        }
    }

    private static void ValidateDiagnosticEvidence(
        string reportName,
        int index,
        string? featureType,
        string? handlerName,
        string? handlerVersion,
        string? solidWorksVersion,
        string? diagnosticRunPath,
        string? sourceRevision,
        string? adapterId,
        string? adapterVersion,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(diagnosticRunPath))
        {
            return;
        }

        string path;
        try
        {
            path = Path.IsPathFullyQualified(diagnosticRunPath)
                ? Path.GetFullPath(diagnosticRunPath)
                : Path.GetFullPath(Path.Combine(
                    FindProjectRootForEvidence(),
                    diagnosticRunPath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            issues.Add(
                $"{reportName} feature result {index} has invalid diagnostic path: {ex.Message}.");
            return;
        }

        if (!File.Exists(path))
        {
            issues.Add(
                $"{reportName} feature result {index} diagnostic evidence does not exist: {path}.");
            return;
        }

        try
        {
            using var diagnostic = JsonDocument.Parse(File.ReadAllText(path));
            var root = diagnostic.RootElement;
            if (!IsBoolean(root, "candidate_only", true) ||
                !IsBoolean(root, "main_workflow_accepted", false) ||
                !IsBoolean(root, "quality_gate_passed", false) ||
                !IsBoolean(root, "solid_works_connected", true) ||
                !IsBoolean(root, "real_cad_executed", true) ||
                !StringEquals(root, "final_status", "CandidatePassed") ||
                !StringEquals(root, "deliverable_status", "NotDeliverable") ||
                !StringEquals(root, "solid_works_version", solidWorksVersion))
            {
                issues.Add(
                    $"{reportName} feature result {index} diagnostic is not a valid " +
                    "CandidatePassed/NotDeliverable run for the same SolidWorks version.");
                return;
            }

            if (!root.TryGetProperty("feature_handler_reports", out var featureReports) ||
                featureReports.ValueKind != JsonValueKind.Array)
            {
                issues.Add(
                    $"{reportName} feature result {index} diagnostic feature results are missing.");
                return;
            }

            var matching = featureReports.EnumerateArray().Where(candidate =>
                StringEquals(candidate, "feature_type", featureType) &&
                StringEquals(candidate, "adapter_id", adapterId) &&
                StringEquals(candidate, "adapter_version", adapterVersion) &&
                StringEquals(
                    candidate,
                    "evidence_solid_works_version",
                    solidWorksVersion) &&
                StringEquals(
                    candidate,
                    "evidence_source_revision",
                    sourceRevision) &&
                IsBoolean(candidate, "result_object_validated", true) &&
                IsBoolean(candidate, "rebuild_passed", true) &&
                IsBoolean(candidate, "geometry_change_validated", true) &&
                string.IsNullOrWhiteSpace(ReadString(candidate, "failure_stage")) &&
                (ReadString(candidate, "handler_name")?.Contains(
                    $"@{handlerVersion}",
                    StringComparison.Ordinal) == true));
            if (!matching.Any())
            {
                issues.Add(
                    $"{reportName} feature result {index} diagnostic does not bind the same " +
                    $"feature/handler/adapter/runtime/source revision as {handlerName}.");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"{reportName} feature result {index} diagnostic evidence is invalid: {ex.Message}.");
        }
    }

    private static string FindProjectRootForEvidence()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AI_Mechanical_Engineering_Agent_Platform.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "Project root is unavailable for feature evidence validation.");
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool StringEquals(
        JsonElement root,
        string propertyName,
        string? expected) =>
        !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(
            ReadString(root, propertyName),
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsBoolean(
        JsonElement root,
        string propertyName,
        bool expected) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == (expected ? JsonValueKind.True : JsonValueKind.False);

    private static void RequireTrue(
        JsonElement root,
        string propertyName,
        string reportName,
        List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.True)
        {
            issues.Add($"{reportName} {propertyName} must be true.");
        }
    }

    private static void RequireString(
        JsonElement root,
        string propertyName,
        string expected,
        string reportName,
        List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            !string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"{reportName} {propertyName} must be {expected}.");
        }
    }

    private static void ValidateRealDrawingReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("drawing_created", out var drawingCreated) ||
                drawingCreated.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report drawing_created must be true.");
            }

            if (!root.TryGetProperty("slddrw_exists", out var slddrwExists) ||
                slddrwExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report slddrw_exists must be true.");
            }

            if (!root.TryGetProperty("pdf_exists", out var pdfExists) ||
                pdfExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report pdf_exists must be true.");
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("drawing_report final_status must be Passed.");
            }

            if (!root.TryGetProperty("views_created", out var views) ||
                views.ValueKind != JsonValueKind.Array)
            {
                issues.Add("drawing_report views_created must be present.");
                return;
            }

            var viewNames = views.EnumerateArray()
                .Select(view => view.GetString())
                .Where(view => !string.IsNullOrWhiteSpace(view))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var expectedView in new[] { "Front", "Top", "Right", "Isometric" })
            {
                if (!viewNames.Contains(expectedView))
                {
                    issues.Add($"drawing_report missing view: {expectedView}.");
                }
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"drawing_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateRealDrawingDimensionReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("drawing_opened", out var drawingOpened) ||
                drawingOpened.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report drawing_opened must be true.");
            }

            if (!root.TryGetProperty("slddrw_exists", out var slddrwExists) ||
                slddrwExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report slddrw_exists must be true.");
            }

            if (!root.TryGetProperty("pdf_exists", out var pdfExists) ||
                pdfExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report pdf_exists must be true.");
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("dimension_report final_status must be Passed.");
            }

            foreach (var flagName in new[]
            {
                "length_dimension_added",
                "width_dimension_added",
                "thickness_dimension_added",
                "hole_diameter_dimension_added",
                "hole_position_dimension_added"
            })
            {
                if (!root.TryGetProperty(flagName, out var flag) || flag.ValueKind != JsonValueKind.True)
                {
                    issues.Add($"dimension_report {flagName} must be true.");
                }
            }

            if (!root.TryGetProperty("views_confirmed", out var views) ||
                views.ValueKind != JsonValueKind.Array)
            {
                issues.Add("dimension_report views_confirmed must be present.");
                return;
            }

            var viewNames = views.EnumerateArray()
                .Select(view => view.GetString())
                .Where(view => !string.IsNullOrWhiteSpace(view))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var expectedView in new[] { "Front", "Top", "Right", "Isometric" })
            {
                if (!viewNames.Contains(expectedView))
                {
                    issues.Add($"dimension_report missing confirmed view: {expectedView}.");
                }
            }

            if (!root.TryGetProperty("dimensions", out var dimensions) ||
                dimensions.ValueKind != JsonValueKind.Array ||
                dimensions.GetArrayLength() < 5)
            {
                issues.Add("dimension_report dimensions must include the required basic dimensions.");
                return;
            }

            var failedDimension = dimensions.EnumerateArray().FirstOrDefault(dimension =>
                !dimension.TryGetProperty("status", out var status) ||
                !string.Equals(status.GetString(), "Passed", StringComparison.OrdinalIgnoreCase));
            if (failedDimension.ValueKind != JsonValueKind.Undefined)
            {
                issues.Add("dimension_report dimensions must all have status Passed.");
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"dimension_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateRealDrawingTitleBlockReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            foreach (var flagName in new[]
            {
                "drawing_opened",
                "title_block_template_detected",
                "drawing_properties_read",
                "custom_properties_written",
                "title_block_updated",
                "slddrw_exists",
                "pdf_exists"
            })
            {
                if (!root.TryGetProperty(flagName, out var flag) || flag.ValueKind != JsonValueKind.True)
                {
                    issues.Add($"title_block_report {flagName} must be true.");
                }
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("title_block_report final_status must be Passed.");
            }

            if (!root.TryGetProperty("title_block_population_strategy", out var populationStrategy) ||
                !string.Equals(populationStrategy.GetString(), "custom_properties_only", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("title_block_report title_block_population_strategy must be custom_properties_only.");
            }

            if (!root.TryGetProperty("title_block_fields_verified_in_sheet_format", out var fieldsVerified) ||
                fieldsVerified.ValueKind != JsonValueKind.False)
            {
                issues.Add("title_block_report title_block_fields_verified_in_sheet_format must be false until sheet format note rendering is verified.");
            }

            var expectedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["part_name"] = "plate_basic_4holes",
                ["drawing_number"] = "PLATE-BASIC-4HOLES",
                ["revision"] = "A"
            };

            foreach (var expected in expectedFields)
            {
                if (!root.TryGetProperty(expected.Key, out var field) ||
                    !string.Equals(field.GetString(), expected.Value, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"title_block_report {expected.Key} must be {expected.Value}.");
                }
            }

            foreach (var requiredField in new[] { "material", "scale", "drawing_date" })
            {
                if (!root.TryGetProperty(requiredField, out var field) ||
                    string.IsNullOrWhiteSpace(field.GetString()))
                {
                    issues.Add($"title_block_report {requiredField} must be present.");
                }
            }

            if (!root.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Array ||
                properties.GetArrayLength() < 6)
            {
                issues.Add("title_block_report properties must include the required title block fields.");
                return;
            }

            var failedProperty = properties.EnumerateArray().FirstOrDefault(property =>
                !property.TryGetProperty("status", out var status) ||
                !string.Equals(status.GetString(), "Passed", StringComparison.OrdinalIgnoreCase));
            if (failedProperty.ValueKind != JsonValueKind.Undefined)
            {
                issues.Add("title_block_report properties must all have status Passed.");
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"title_block_report.json is invalid: {ex.Message}.");
        }
    }

    private static ReviewReport Report(IReadOnlyList<string> issues) =>
        new(
            $"solidworks-artifact-validation-{Guid.NewGuid():N}",
            "solidworks-artifact-validator",
            issues.Count == 0,
            issues.Count == 0 ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: issues.Any(issue =>
                issue.Contains("real_cad_executed", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("execution_mode", StringComparison.OrdinalIgnoreCase)));

    private static bool IsUnderOutputSolidWorksSegment(string fullPath)
    {
        var directory = new FileInfo(fullPath).Directory;
        while (directory is not null)
        {
            if (directory.Name.Equals("solidworks", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("output", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private bool IsUnderRealArtifactRoot(string fullPath)
    {
        if (_configuredOutputRoot is not null)
        {
            var configuredRoot = _configuredOutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var realRoot = Path.GetFileName(configuredRoot).Equals("real", StringComparison.OrdinalIgnoreCase)
                ? NormalizeDirectory(configuredRoot)
                : NormalizeDirectory(Path.Combine(configuredRoot, "real"));

            return IsUnderRoot(fullPath, realRoot);
        }

        return IsUnderOutputSolidWorksRealSegment(fullPath);
    }

    private static bool IsUnderOutputSolidWorksRealSegment(string fullPath)
    {
        var directory = new FileInfo(fullPath).Directory;
        while (directory is not null)
        {
            if (directory.Name.Equals("real", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("solidworks", StringComparison.OrdinalIgnoreCase) == true &&
                directory.Parent.Parent?.Name.Equals("output", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
}
