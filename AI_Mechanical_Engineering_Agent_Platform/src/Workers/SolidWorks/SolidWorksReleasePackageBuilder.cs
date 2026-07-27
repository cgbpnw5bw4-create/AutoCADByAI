using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public sealed record SolidWorksReleasePackageBuildResult(
    string Status,
    string OutputDirectory,
    string? ManifestPath,
    string? QualityReportPath,
    string? SummaryPath,
    string? FailureStage,
    bool ArtifactsCollected,
    bool ReportsCollected,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues);

public sealed class SolidWorksReleasePackageBuilder
{
    private const string PartName = "plate_basic_4holes";

    public async Task<SolidWorksReleasePackageBuildResult> BuildAsync(
        string projectRoot,
        string? outputRoot = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var logs = new List<string>();
        var issues = new List<string>();
        var root = Path.GetFullPath(projectRoot);
        var releaseRoot = Path.GetFullPath(outputRoot ?? Path.Combine(root, "output", "solidworks", "release", PartName));
        var outputDirectory = Path.Combine(releaseRoot, TimestampSegment());
        var artifactsDirectory = Path.Combine(outputDirectory, "artifacts");
        var reportsDirectory = Path.Combine(outputDirectory, "reports");
        var manifestPath = Path.Combine(outputDirectory, "release_manifest.json");
        var qualityReportPath = Path.Combine(outputDirectory, "package_quality_report.json");
        var summaryPath = Path.Combine(outputDirectory, "release_summary.md");

        Directory.CreateDirectory(artifactsDirectory);
        Directory.CreateDirectory(reportsDirectory);

        var manifest = new SolidWorksReleaseManifest
        {
            SourceRoot = root,
            OutputDirectory = outputDirectory
        };

        try
        {
            var sourceSet = SolidWorksReleaseSourceSet.Discover(root);

            AddReleaseItem(
                manifest.Artifacts,
                "plate_basic_4holes.SLDPRT",
                "Artifact",
                sourceSet.SldprtPath,
                Path.Combine(artifactsDirectory, "plate_basic_4holes.SLDPRT"));
            AddReleaseItem(
                manifest.Artifacts,
                "plate_basic_4holes.STEP",
                "Artifact",
                sourceSet.StepPath,
                Path.Combine(artifactsDirectory, "plate_basic_4holes.STEP"));
            AddReleaseItem(
                manifest.Artifacts,
                "plate_basic_4holes.SLDDRW",
                "Artifact",
                sourceSet.DrawingPath,
                Path.Combine(artifactsDirectory, "plate_basic_4holes.SLDDRW"));
            AddReleaseItem(
                manifest.Artifacts,
                "plate_basic_4holes.pdf",
                "Artifact",
                sourceSet.PdfPath,
                Path.Combine(artifactsDirectory, "plate_basic_4holes.pdf"));

            AddReleaseItem(
                manifest.Reports,
                "build_report.json",
                "Report",
                sourceSet.BuildReportPath,
                Path.Combine(reportsDirectory, "build_report.json"),
                readReportStatus: true);
            AddReleaseItem(
                manifest.Reports,
                "diagnostic_report.json",
                "Report",
                sourceSet.DiagnosticReportPath,
                Path.Combine(reportsDirectory, "diagnostic_report.json"),
                readReportStatus: true);
            AddReleaseItem(
                manifest.Reports,
                "drawing_report.json",
                "Report",
                sourceSet.DrawingReportPath,
                Path.Combine(reportsDirectory, "drawing_report.json"),
                readReportStatus: true);
            AddReleaseItem(
                manifest.Reports,
                "dimension_report.json",
                "Report",
                sourceSet.DimensionReportPath,
                Path.Combine(reportsDirectory, "dimension_report.json"),
                readReportStatus: true);
            AddReleaseItem(
                manifest.Reports,
                "title_block_report.json",
                "Report",
                sourceSet.TitleBlockReportPath,
                Path.Combine(reportsDirectory, "title_block_report.json"),
                readReportStatus: true);

            CopyItems(manifest.Artifacts, logs, issues);
            CopyItems(manifest.Reports, logs, issues);

            var artifactsCollected = manifest.Artifacts.All(item => item.Exists && item.SizeBytes > 0);
            var reportsCollected = manifest.Reports.All(item => item.Exists && item.SizeBytes > 0);
            var packageFailureStage = DetermineFailureStage(manifest, copyFailed: issues.Any(issue =>
                issue.Contains("copy_failed", StringComparison.OrdinalIgnoreCase)));

            manifest.Errors.AddRange(issues);
            manifest.Warnings.AddRange(sourceSet.Warnings);
            ApplyManifestSemanticStatus(manifest, packageFailureStage);

            try
            {
                await WriteJsonAsync(manifestPath, manifest, cancellationToken);
                logs.Add($"release_manifest_written: {manifestPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Failed(
                    outputDirectory,
                    null,
                    null,
                    null,
                    "manifest_write_failed",
                    logs,
                    issues.Append(ex.GetBaseException().Message).ToArray());
            }

            try
            {
                await File.WriteAllTextAsync(summaryPath, BuildSummary(manifest), cancellationToken);
                logs.Add($"release_summary_written: {summaryPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Failed(
                    outputDirectory,
                    manifestPath,
                    null,
                    null,
                    "release_summary_write_failed",
                    logs,
                    issues.Append(ex.GetBaseException().Message).ToArray());
            }

            var qualityReport = new SolidWorksReleasePackageValidator().Validate(manifest, manifestPath, summaryPath, outputDirectory);
            try
            {
                await WriteJsonAsync(qualityReportPath, qualityReport, cancellationToken);
                logs.Add($"package_quality_report_written: {qualityReportPath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return Failed(
                    outputDirectory,
                    manifestPath,
                    null,
                    summaryPath,
                    "quality_report_write_failed",
                    logs,
                    issues.Append(ex.GetBaseException().Message).ToArray());
            }

            var finalStage = qualityReport.FailureStage ?? manifest.FailureStage;
            return new SolidWorksReleasePackageBuildResult(
                qualityReport.FinalStatus == "Passed" ? "Completed" : "Failed",
                outputDirectory,
                manifestPath,
                qualityReportPath,
                summaryPath,
                finalStage,
                artifactsCollected,
                reportsCollected,
                logs,
                qualityReport.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            var failureStage = ex.Message.Contains("copy", StringComparison.OrdinalIgnoreCase)
                ? "artifact_copy_failed"
                : "package_validation_failed";
            return Failed(
                outputDirectory,
                File.Exists(manifestPath) ? manifestPath : null,
                File.Exists(qualityReportPath) ? qualityReportPath : null,
                File.Exists(summaryPath) ? summaryPath : null,
                failureStage,
                logs,
                issues.Append(ex.GetBaseException().Message).ToArray());
        }
    }

    private static void AddReleaseItem(
        List<SolidWorksReleaseManifestItem> items,
        string name,
        string kind,
        string? sourcePath,
        string packagePath,
        bool readReportStatus = false)
    {
        var item = new SolidWorksReleaseManifestItem
        {
            Name = name,
            Kind = kind,
            Required = true,
            SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFullPath(sourcePath),
            PackagePath = Path.GetFullPath(packagePath)
        };

        if (!string.IsNullOrWhiteSpace(item.SourcePath) && File.Exists(item.SourcePath))
        {
            var info = new FileInfo(item.SourcePath);
            item.SizeBytes = info.Length;
            item.SourceLastWriteTimeUtc = info.LastWriteTimeUtc;
            item.Sha256 = info.Length > 0 ? ComputeSha256(info.FullName) : null;
            if (readReportStatus)
            {
                var status = ReadReportStatus(info.FullName);
                item.FinalStatus = status.FinalStatus;
                item.FailureStage = status.FailureStage;
            }
        }

        items.Add(item);
    }

    private static void CopyItems(
        IEnumerable<SolidWorksReleaseManifestItem> items,
        List<string> logs,
        List<string> issues)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                issues.Add($"source_missing: {item.Name}");
                item.Exists = false;
                item.SizeBytes = 0;
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.PackagePath!)!);
                File.Copy(item.SourcePath, item.PackagePath!, overwrite: true);
                var info = new FileInfo(item.PackagePath!);
                item.Exists = info.Exists;
                item.SizeBytes = info.Exists ? info.Length : 0;
                item.Sha256 = info.Exists && info.Length > 0 ? ComputeSha256(info.FullName) : null;
                logs.Add($"copied: {item.Name} -> {item.PackagePath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"copy_failed: {item.Name}: {ex.GetBaseException().Message}");
                item.Exists = false;
                item.SizeBytes = 0;
            }
        }
    }

    private static string? DetermineFailureStage(SolidWorksReleaseManifest manifest, bool copyFailed)
    {
        if (copyFailed)
        {
            return "artifact_copy_failed";
        }

        if (manifest.Artifacts.Any(item => item.Required && (!item.Exists || item.SizeBytes <= 0)))
        {
            return "source_artifacts_missing";
        }

        if (manifest.Reports.Any(item => item.Required && (!item.Exists || item.SizeBytes <= 0)))
        {
            return "source_report_missing";
        }

        return null;
    }

    private static void ApplyManifestSemanticStatus(
        SolidWorksReleaseManifest manifest,
        string? packageFailureStage)
    {
        var statuses = manifest.Reports.Select(ToSourceReportStatus).ToArray();
        manifest.SourceReportsChecked = manifest.Reports.All(item =>
            item.Exists &&
            !string.IsNullOrWhiteSpace(item.FinalStatus));
        manifest.SourceReportFailures.Clear();
        manifest.SourceReportFailures.AddRange(statuses.Where(IsFailedSourceReport));
        manifest.SourceReportWarnings.Clear();
        manifest.SourceReportWarnings.AddRange(statuses.Where(IsWarningSourceReport));
        manifest.AllSourceReportsPassed =
            manifest.SourceReportsChecked &&
            manifest.Reports.Count > 0 &&
            statuses.All(status => string.Equals(status.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase));
        manifest.PackageBuildStatus = packageFailureStage is null && manifest.SourceReportsChecked ? "Passed" : "Failed";
        manifest.DeliverableStatus =
            manifest.PackageBuildStatus == "Passed" && manifest.AllSourceReportsPassed
                ? "Deliverable"
                : "NotDeliverable";
        manifest.FailureStage = packageFailureStage ??
            (!manifest.SourceReportsChecked
                ? "source_report_missing"
                : manifest.AllSourceReportsPassed ? null : "source_report_failed");
        manifest.FinalStatus = manifest.DeliverableStatus == "Deliverable" ? "Passed" : "Failed";
    }

    private static string BuildSummary(SolidWorksReleaseManifest manifest)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# V1.5 SolidWorks 工程发布包摘要");
        builder.AppendLine();
        builder.AppendLine($"- 零件：`{manifest.PartName}`");
        builder.AppendLine($"- 生成时间：`{manifest.CreatedAt:O}`");
        builder.AppendLine($"- 发布目录：`{manifest.OutputDirectory}`");
        builder.AppendLine($"- package_build_status：`{manifest.PackageBuildStatus}`");
        builder.AppendLine($"- all_source_reports_passed：`{manifest.AllSourceReportsPassed}`");
        builder.AppendLine($"- deliverable_status：`{manifest.DeliverableStatus}`");
        builder.AppendLine($"- final_status：`{manifest.FinalStatus}`");
        builder.AppendLine($"- failure_stage：`{manifest.FailureStage ?? "none"}`");
        builder.AppendLine();
        builder.AppendLine("## 交付文件");
        foreach (var item in manifest.Artifacts)
        {
            builder.AppendLine($"- `{item.Name}`：{ItemStatus(item)}，size={item.SizeBytes}");
        }

        builder.AppendLine();
        builder.AppendLine("## 报告文件");
        foreach (var item in manifest.Reports)
        {
            builder.AppendLine($"- `{item.Name}`：{ItemStatus(item)}，final_status=`{item.FinalStatus ?? "missing"}`，failure_stage=`{item.FailureStage ?? "none"}`");
        }

        builder.AppendLine();
        builder.AppendLine("## 源报告结论");
        builder.AppendLine($"- source_reports_checked：`{manifest.SourceReportsChecked}`");
        builder.AppendLine($"- source_report_failures：`{manifest.SourceReportFailures.Count}`");
        builder.AppendLine($"- source_report_warnings：`{manifest.SourceReportWarnings.Count}`");
        foreach (var failure in manifest.SourceReportFailures)
        {
            builder.AppendLine($"- 失败报告 `{failure.Name}`：final_status=`{failure.FinalStatus ?? "missing"}`，failure_stage=`{failure.FailureStage ?? "none"}`");
        }

        builder.AppendLine();
        builder.AppendLine("## 质量边界");
        builder.AppendLine("- `package_build_status` 只表示打包流程是否成功，不代表源 CAD 结果可交付。");
        builder.AppendLine("- `deliverable_status` 表示最终交付是否可用，任一源报告失败时必须为 `NotDeliverable`。");
        builder.AppendLine("- 本阶段不做几何 OCR、PDF 视觉识别、BOM、装配图或复杂图纸审查。");
        builder.AppendLine("- 默认 self-check 不启动 SolidWorks，也不调用 COM。");
        return builder.ToString();
    }

    private static string ItemStatus(SolidWorksReleaseManifestItem item) =>
        item.Exists && item.SizeBytes > 0 ? "已收集" : "缺失";

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions()), cancellationToken);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private static (string? FinalStatus, string? FailureStage) ReadReportStatus(string reportPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var finalStatus = root.TryGetProperty("final_status", out var finalStatusElement)
                ? finalStatusElement.GetString()
                : null;
            var failureStage = root.TryGetProperty("failure_stage", out var failureStageElement)
                ? failureStageElement.GetString()
                : null;
            return (finalStatus, failureStage);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ("InvalidJson", "source_report_missing");
        }
    }

    internal static SolidWorksSourceReportStatus ToSourceReportStatus(SolidWorksReleaseManifestItem item)
    {
        var message = item switch
        {
            { Exists: false } => "source report was not collected.",
            { FinalStatus: null } => "source report does not expose final_status.",
            _ when string.Equals(item.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) => "source report passed.",
            _ when string.Equals(item.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase) => "source report failed and blocks deliverable output.",
            _ => "source report did not pass cleanly."
        };

        return new SolidWorksSourceReportStatus(
            item.Name,
            item.FinalStatus,
            item.FailureStage,
            item.SourcePath,
            item.PackagePath,
            message);
    }

    internal static bool IsFailedSourceReport(SolidWorksSourceReportStatus status) =>
        string.Equals(status.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase);

    internal static bool IsWarningSourceReport(SolidWorksSourceReportStatus status) =>
        !string.IsNullOrWhiteSpace(status.FinalStatus) &&
        !string.Equals(status.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase);

    private static SolidWorksReleasePackageBuildResult Failed(
        string outputDirectory,
        string? manifestPath,
        string? qualityReportPath,
        string? summaryPath,
        string failureStage,
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues) =>
        new(
            "Failed",
            outputDirectory,
            manifestPath,
            qualityReportPath,
            summaryPath,
            failureStage,
            ArtifactsCollected: false,
            ReportsCollected: false,
            logs,
            issues);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string TimestampSegment() =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
}

public sealed class SolidWorksReleasePackageValidator
{
    public SolidWorksPackageQualityReport Validate(
        SolidWorksReleaseManifest manifest,
        string manifestPath,
        string summaryPath,
        string outputDirectory)
    {
        var report = new SolidWorksPackageQualityReport
        {
            OutputDirectory = outputDirectory,
            PartName = manifest.PartName,
            RequestId = manifest.RequestId,
            RequiresDrawingDeliverables = manifest.RequiresDrawingDeliverables,
            ManifestExists = ExistingNonEmpty(manifestPath),
            ReleaseSummaryExists = ExistingNonEmpty(summaryPath)
        };

        AddCheck(report, "release_manifest_exists", report.ManifestExists, "manifest_write_failed", manifestPath);
        AddCheck(report, "release_summary_exists", report.ReleaseSummaryExists, "release_summary_write_failed", summaryPath);

        report.ArtifactsCollected = manifest.Artifacts.All(item => item.Exists && item.SizeBytes > 0);
        report.ReportsCollected = manifest.Reports.All(item => item.Exists && item.SizeBytes > 0);
        report.PdfExists = manifest.Artifacts.Any(item =>
            item.Name.Equals($"{manifest.PartName}.pdf", StringComparison.OrdinalIgnoreCase) &&
            item.Exists &&
            item.SizeBytes > 0);
        var pdfRequirementSatisfied = !manifest.RequiresDrawingDeliverables || report.PdfExists;
        report.PathsUnderReleaseDirectory = manifest.Artifacts.Concat(manifest.Reports).All(item =>
            !string.IsNullOrWhiteSpace(item.PackagePath) &&
            IsUnderDirectory(outputDirectory, item.PackagePath));
        report.FileSizesValid = manifest.Artifacts.Concat(manifest.Reports)
            .Where(item => item.Required)
            .All(item => item.Exists && item.SizeBytes > 0);
        report.ReportsFinalStatusChecked = manifest.Reports.All(item =>
            item.Exists &&
            !string.IsNullOrWhiteSpace(item.FinalStatus));
        report.FailureStagesChecked = manifest.Reports.All(item =>
            !item.Exists ||
            string.Equals(item.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(item.FailureStage));
        var sourceReportStatuses = manifest.Reports
            .Select(SolidWorksReleasePackageBuilder.ToSourceReportStatus)
            .ToArray();
        report.SourceReportsChecked = report.ReportsFinalStatusChecked;
        report.SourceReportFailures.Clear();
        report.SourceReportFailures.AddRange(sourceReportStatuses.Where(SolidWorksReleasePackageBuilder.IsFailedSourceReport));
        report.SourceReportWarnings.Clear();
        report.SourceReportWarnings.AddRange(sourceReportStatuses.Where(SolidWorksReleasePackageBuilder.IsWarningSourceReport));
        report.RequireRealExecutionEvidence = manifest.RequireRealExecutionEvidence;
        report.SourceExecutionEvidence.Clear();
        report.SourceExecutionEvidence.AddRange(manifest.SourceExecutionEvidence);
        report.RealExecutionEvidencePassed = !manifest.RequireRealExecutionEvidence || HasCompleteRealExecutionEvidence(manifest);
        report.AllSourceReportsPassed =
            report.SourceReportsChecked &&
            manifest.Reports.Count > 0 &&
            sourceReportStatuses.All(status => string.Equals(status.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase)) &&
            report.RealExecutionEvidencePassed;

        AddCheck(report, "artifacts_collected", report.ArtifactsCollected, "source_artifacts_missing",
            manifest.RequiresDrawingDeliverables ? "SLDPRT, STEP, SLDDRW and PDF must be collected." : "SLDPRT and STEP must be collected.");
        AddCheck(report, "reports_collected", report.ReportsCollected, "source_report_missing",
            manifest.RequiresDrawingDeliverables ? "build, drawing, dimension and title block reports must be collected." : "build_report.json must be collected.");
        AddCheck(report, "pdf_exists", pdfRequirementSatisfied, "source_artifacts_missing",
            manifest.RequiresDrawingDeliverables ? $"{manifest.PartName}.pdf must exist in artifacts." : "PDF is not required for a V1.9 build-only package.");
        AddCheck(report, "paths_under_release_directory", report.PathsUnderReleaseDirectory, "package_validation_failed", outputDirectory);
        AddCheck(report, "file_sizes_valid", report.FileSizesValid, "package_validation_failed", "all required copied files must be greater than zero bytes.");
        AddCheck(report, "reports_final_status_checked", report.ReportsFinalStatusChecked, "source_report_missing", "each collected report must expose final_status.");
        AddCheck(report, "failure_stages_checked", report.FailureStagesChecked, "package_validation_failed", "failed reports must expose failure_stage.");
        AddCheck(report, "source_reports_checked", report.SourceReportsChecked, "source_report_missing", "all source reports must expose final_status before deliverable evaluation.");
        AddCheck(report, "all_source_reports_passed", report.AllSourceReportsPassed, "source_report_failed", "all source reports must have final_status=Passed before deliverable_status can be Deliverable.");
        AddCheck(
            report,
            "real_execution_evidence_passed",
            report.RealExecutionEvidencePassed,
            "real_execution_evidence_failed",
            manifest.RequiresDrawingDeliverables
                ? "Complete drawing packages require all four stages to prove real worker execution and QualityGate passage."
                : "Build-only packages require the registered real part-family build stage to prove Worker, ArtifactValidator, Reviewer and QualityGate passage.");

        foreach (var item in manifest.Artifacts.Where(item => !item.Exists || item.SizeBytes <= 0))
        {
            report.Errors.Add($"missing_artifact: {item.Name}");
        }

        foreach (var item in manifest.Reports.Where(item => !item.Exists || item.SizeBytes <= 0))
        {
            report.Errors.Add($"missing_report: {item.Name}");
        }

        foreach (var failure in report.SourceReportFailures)
        {
            report.Errors.Add($"source_report_failed: {failure.Name}: final_status={failure.FinalStatus}; failure_stage={failure.FailureStage ?? "none"}");
        }

        foreach (var warning in report.SourceReportWarnings)
        {
            report.Warnings.Add($"source_report_warning: {warning.Name}: final_status={warning.FinalStatus}; failure_stage={warning.FailureStage ?? "none"}");
        }

        var packageFailureStage = DeterminePackageBuildFailureStage(report, manifest);
        report.PackageBuildStatus = packageFailureStage is null ? "Passed" : "Failed";
        report.DeliverableStatus =
            report.PackageBuildStatus == "Passed" && report.AllSourceReportsPassed
                ? "Deliverable"
                : "NotDeliverable";
        report.FailureStage = packageFailureStage ??
            (report.AllSourceReportsPassed ? null :
                !report.RealExecutionEvidencePassed ? "real_execution_evidence_failed" : "source_report_failed");
        report.FinalStatus = report.DeliverableStatus == "Deliverable" ? "Passed" : "Failed";
        AddCheck(
            report,
            "failed_source_reports_block_deliverable",
            report.SourceReportFailures.Count == 0 || report.DeliverableStatus != "Deliverable",
            "source_report_failed",
            "failed source reports must block deliverable_status.");
        return report;
    }

    private static string? DeterminePackageBuildFailureStage(SolidWorksPackageQualityReport report, SolidWorksReleaseManifest manifest)
    {
        if (!report.ManifestExists)
        {
            return "manifest_write_failed";
        }

        if (!report.ReleaseSummaryExists)
        {
            return "release_summary_write_failed";
        }

        if (!report.ArtifactsCollected || (manifest.RequiresDrawingDeliverables && !report.PdfExists))
        {
            return "source_artifacts_missing";
        }

        if (!report.ReportsCollected || !report.ReportsFinalStatusChecked)
        {
            return "source_report_missing";
        }

        if (!report.PathsUnderReleaseDirectory || !report.FileSizesValid || !report.FailureStagesChecked)
        {
            return "package_validation_failed";
        }

        if (!report.RealExecutionEvidencePassed)
        {
            return "real_execution_evidence_failed";
        }

        return string.Equals(manifest.FailureStage, "source_report_failed", StringComparison.OrdinalIgnoreCase)
            ? null
            : manifest.FailureStage;
    }

    private static void AddCheck(
        SolidWorksPackageQualityReport report,
        string name,
        bool passed,
        string failureStage,
        string message)
    {
        report.Checks.Add(new SolidWorksPackageQualityCheck(
            name,
            passed ? "Passed" : "Failed",
            passed ? null : failureStage,
            message));
    }

    private static bool HasCompleteRealExecutionEvidence(SolidWorksReleaseManifest manifest)
    {
        var evidence = manifest.SourceExecutionEvidence;
        if (!manifest.RequiresDrawingDeliverables)
        {
            var registry = PartFamilyBuilderRegistry.CreateDefault();
            return registry.TryGetBuilder(manifest.PartName, out var builder) &&
                   evidence.Count == 1 &&
                   evidence.All(item =>
                       string.Equals(item.Stage, "build", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(item.WorkerName, "RealSolidWorksWorker", StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(item.ExecutionMode, builder.RealExecutionMode, StringComparison.OrdinalIgnoreCase) &&
                       item.RealCadExecuted && item.RealCadConnected && item.QualityGatePassed &&
                       string.IsNullOrWhiteSpace(item.FailureStage));
        }

        var expectedModes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build"] = "RealBuildPlateBasic4Holes",
            ["drawing"] = "RealDrawingBasicViews",
            ["dimension"] = "RealDrawingDimensions",
            ["title_block"] = "RealDrawingTitleBlock"
        };

        return evidence.Count == expectedModes.Count && evidence.All(item =>
            expectedModes.TryGetValue(item.Stage, out var expectedMode) &&
            string.Equals(item.WorkerName, "RealSolidWorksWorker", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ExecutionMode, expectedMode, StringComparison.OrdinalIgnoreCase) &&
            item.RealCadExecuted &&
            item.RealCadConnected &&
            item.QualityGatePassed &&
            string.IsNullOrWhiteSpace(item.FailureStage));
    }

    private static bool ExistingNonEmpty(string path) =>
        File.Exists(path) && new FileInfo(path).Length > 0;

    private static bool IsUnderDirectory(string root, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        return !relative.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }
}

internal sealed record SolidWorksReleaseSourceSet(
    string? SldprtPath,
    string? StepPath,
    string? DrawingPath,
    string? PdfPath,
    string? BuildReportPath,
    string? DiagnosticReportPath,
    string? DrawingReportPath,
    string? DimensionReportPath,
    string? TitleBlockReportPath,
    IReadOnlyList<string> Warnings)
{
    public static SolidWorksReleaseSourceSet Discover(string projectRoot)
    {
        var outputRoot = Path.Combine(projectRoot, "output", "solidworks");
        var plateRoot = Path.Combine(outputRoot, "real", "plate_basic_4holes");
        var drawingRoot = Path.Combine(outputRoot, "real", "plate_basic_4holes_drawing");
        var dimensionRoot = Path.Combine(outputRoot, "real", "plate_basic_4holes_drawing_dimensions");
        var titleBlockRoot = Path.Combine(outputRoot, "real", "plate_basic_4holes_title_block");
        var diagnosticRoot = Path.Combine(outputRoot, "diagnostics", "plate_basic_4holes");
        var warnings = new List<string>();

        var sldprt = LatestFile(plateRoot, "plate_basic_4holes.SLDPRT");
        var step = LatestFile(plateRoot, "plate_basic_4holes.STEP");
        var drawing = FirstExistingLatest(
            LatestFile(titleBlockRoot, "plate_basic_4holes_title_block.SLDDRW"),
            LatestFile(dimensionRoot, "plate_basic_4holes_dimensioned.SLDDRW"),
            LatestFile(drawingRoot, "plate_basic_4holes.SLDDRW"));
        var pdf = FirstExistingLatest(
            LatestFile(titleBlockRoot, "plate_basic_4holes_title_block.pdf"),
            LatestFile(dimensionRoot, "plate_basic_4holes_dimensioned.pdf"),
            LatestFile(drawingRoot, "plate_basic_4holes.pdf"));
        var buildReport = !string.IsNullOrWhiteSpace(sldprt)
            ? ExistingSibling(sldprt, "build_report.json") ?? LatestFile(plateRoot, "build_report.json")
            : LatestFile(plateRoot, "build_report.json");

        if (!string.IsNullOrWhiteSpace(buildReport) &&
            !string.IsNullOrWhiteSpace(sldprt) &&
            !Path.GetDirectoryName(buildReport)!.Equals(Path.GetDirectoryName(sldprt), StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("build_report_not_in_selected_sldprt_directory");
        }

        return new SolidWorksReleaseSourceSet(
            sldprt,
            step,
            drawing,
            pdf,
            buildReport,
            LatestFile(diagnosticRoot, "diagnostic_report.json"),
            LatestFile(drawingRoot, "drawing_report.json"),
            LatestFile(dimensionRoot, "dimension_report.json"),
            LatestFile(titleBlockRoot, "title_block_report.json"),
            warnings);
    }

    private static string? ExistingSibling(string sourcePath, string fileName)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var candidate = Path.Combine(directory, fileName);
        return File.Exists(candidate) && new FileInfo(candidate).Length > 0 ? candidate : null;
    }

    private static string? LatestFile(string root, string fileName)
    {
        if (!Directory.Exists(root))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(info => info.Length > 0)
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private static string? FirstExistingLatest(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            File.Exists(candidate) &&
            new FileInfo(candidate).Length > 0);
}
