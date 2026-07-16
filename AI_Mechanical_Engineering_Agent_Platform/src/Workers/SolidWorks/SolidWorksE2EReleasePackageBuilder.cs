using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

/// <summary>
/// V1.7 release builder. Unlike the legacy release builder, this class never
/// discovers "latest" files: every source is provided by the current workflow
/// run and is recorded in the manifest before it is copied.
/// </summary>
public sealed class SolidWorksE2EReleasePackageBuilder
{
    public async Task<SolidWorksReleasePackageBuildResult> BuildFromSourcesAsync(
        string projectRoot,
        SolidWorksReleasePackageSourceSet sources,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(projectRoot);
        var releaseDirectory = Path.GetFullPath(outputDirectory);
        var artifactsDirectory = Path.Combine(releaseDirectory, "artifacts");
        var reportsDirectory = Path.Combine(releaseDirectory, "reports");
        var manifestPath = Path.Combine(releaseDirectory, "release_manifest.json");
        var qualityReportPath = Path.Combine(reportsDirectory, "package_quality_report.json");
        var summaryPath = Path.Combine(releaseDirectory, "release_summary.md");
        var logs = new List<string>();
        var issues = new List<string>();

        Directory.CreateDirectory(artifactsDirectory);
        Directory.CreateDirectory(reportsDirectory);

        var manifest = new SolidWorksReleaseManifest
        {
            SourceRoot = root,
            OutputDirectory = releaseDirectory,
            RequireRealExecutionEvidence = sources.RequireRealExecutionEvidence
        };
        manifest.SourceExecutionEvidence.AddRange(sources.ExecutionEvidence);
        manifest.Warnings.AddRange(sources.Warnings ?? Array.Empty<string>());

        AddItem(manifest.Artifacts, "plate_basic_4holes.SLDPRT", "Artifact", sources.SldprtPath, Path.Combine(artifactsDirectory, "plate_basic_4holes.SLDPRT"), issues);
        AddItem(manifest.Artifacts, "plate_basic_4holes.STEP", "Artifact", sources.StepPath, Path.Combine(artifactsDirectory, "plate_basic_4holes.STEP"), issues);
        AddItem(manifest.Artifacts, "plate_basic_4holes.SLDDRW", "Artifact", sources.DrawingPath, Path.Combine(artifactsDirectory, "plate_basic_4holes.SLDDRW"), issues);
        AddItem(manifest.Artifacts, "plate_basic_4holes.pdf", "Artifact", sources.PdfPath, Path.Combine(artifactsDirectory, "plate_basic_4holes.pdf"), issues);

        AddItem(manifest.Reports, "build_report.json", "Report", sources.BuildReportPath, Path.Combine(reportsDirectory, "build_report.json"), issues, readReportStatus: true);
        AddItem(manifest.Reports, "drawing_report.json", "Report", sources.DrawingReportPath, Path.Combine(reportsDirectory, "drawing_report.json"), issues, readReportStatus: true);
        AddItem(manifest.Reports, "dimension_report.json", "Report", sources.DimensionReportPath, Path.Combine(reportsDirectory, "dimension_report.json"), issues, readReportStatus: true);
        AddItem(manifest.Reports, "title_block_report.json", "Report", sources.TitleBlockReportPath, Path.Combine(reportsDirectory, "title_block_report.json"), issues, readReportStatus: true);

        CopyItems(manifest.Artifacts, logs, issues);
        CopyItems(manifest.Reports, logs, issues);
        manifest.Errors.AddRange(issues);

        ApplySemanticStatus(manifest, DetermineFailureStage(manifest, issues));

        try
        {
            await WriteJsonAsync(manifestPath, manifest, cancellationToken);
            await File.WriteAllTextAsync(summaryPath, BuildSummary(manifest), cancellationToken);

            var qualityReport = new SolidWorksReleasePackageValidator().Validate(
                manifest,
                manifestPath,
                summaryPath,
                releaseDirectory);
            await WriteJsonAsync(qualityReportPath, qualityReport, cancellationToken);
            logs.Add($"e2e_release_manifest_written: {manifestPath}");
            logs.Add($"e2e_package_quality_report_written: {qualityReportPath}");

            return new SolidWorksReleasePackageBuildResult(
                qualityReport.FinalStatus == "Passed" ? "Completed" : "Failed",
                releaseDirectory,
                manifestPath,
                qualityReportPath,
                summaryPath,
                qualityReport.FailureStage ?? manifest.FailureStage,
                manifest.Artifacts.All(item => item.Exists && item.SizeBytes > 0),
                manifest.Reports.All(item => item.Exists && item.SizeBytes > 0),
                logs,
                qualityReport.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            issues.Add(ex.GetBaseException().Message);
            return new SolidWorksReleasePackageBuildResult(
                "Failed",
                releaseDirectory,
                File.Exists(manifestPath) ? manifestPath : null,
                File.Exists(qualityReportPath) ? qualityReportPath : null,
                File.Exists(summaryPath) ? summaryPath : null,
                "package_validation_failed",
                false,
                false,
                logs,
                issues);
        }
    }

    private static void AddItem(
        List<SolidWorksReleaseManifestItem> items,
        string name,
        string kind,
        string? sourcePath,
        string packagePath,
        List<string> issues,
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
            try
            {
                var info = new FileInfo(item.SourcePath);
                item.SizeBytes = info.Length;
                item.SourceLastWriteTimeUtc = info.LastWriteTimeUtc;
                item.Sha256 = info.Length > 0 ? ComputeSha256(info.FullName) : null;
                if (readReportStatus)
                {
                    (item.FinalStatus, item.FailureStage) = ReadReportStatus(info.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"source_read_failed: {item.Name}: {ex.GetBaseException().Message}");
            }
        }

        items.Add(item);
    }

    private static void CopyItems(IEnumerable<SolidWorksReleaseManifestItem> items, List<string> logs, List<string> issues)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SourcePath) || !File.Exists(item.SourcePath))
            {
                issues.Add($"source_missing: {item.Name}");
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(item.PackagePath!)!);
                File.Copy(item.SourcePath, item.PackagePath!, overwrite: true);
                var copied = new FileInfo(item.PackagePath!);
                item.Exists = copied.Exists;
                item.SizeBytes = copied.Exists ? copied.Length : 0;
                item.Sha256 = copied.Exists && copied.Length > 0 ? ComputeSha256(copied.FullName) : null;
                logs.Add($"copied_current_run_source: {item.Name} -> {item.PackagePath}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add($"copy_failed: {item.Name}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static void ApplySemanticStatus(SolidWorksReleaseManifest manifest, string? packageFailureStage)
    {
        var statuses = manifest.Reports.Select(ToStatus).ToArray();
        manifest.SourceReportsChecked = manifest.Reports.All(item => item.Exists && !string.IsNullOrWhiteSpace(item.FinalStatus));
        manifest.SourceReportFailures.Clear();
        manifest.SourceReportFailures.AddRange(statuses.Where(status => string.Equals(status.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase)));
        manifest.SourceReportWarnings.Clear();
        manifest.SourceReportWarnings.AddRange(statuses.Where(status => !string.IsNullOrWhiteSpace(status.FinalStatus) && !string.Equals(status.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) && !string.Equals(status.FinalStatus, "Failed", StringComparison.OrdinalIgnoreCase)));
        manifest.RealExecutionEvidencePassed = !manifest.RequireRealExecutionEvidence || HasCompleteRealEvidence(manifest.SourceExecutionEvidence);
        manifest.AllSourceReportsPassed =
            manifest.SourceReportsChecked &&
            statuses.All(status => string.Equals(status.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase)) &&
            manifest.RealExecutionEvidencePassed;
        manifest.PackageBuildStatus = packageFailureStage is null && manifest.SourceReportsChecked ? "Passed" : "Failed";
        manifest.DeliverableStatus = manifest.PackageBuildStatus == "Passed" && manifest.AllSourceReportsPassed ? "Deliverable" : "NotDeliverable";
        manifest.FailureStage = packageFailureStage ??
            (!manifest.SourceReportsChecked ? "source_report_missing" :
                !manifest.RealExecutionEvidencePassed ? "real_execution_evidence_failed" :
                    manifest.AllSourceReportsPassed ? null : "source_report_failed");
        manifest.FinalStatus = manifest.DeliverableStatus == "Deliverable" ? "Passed" : "Failed";
    }

    private static bool HasCompleteRealEvidence(IReadOnlyList<SolidWorksReleaseExecutionEvidence> evidence)
    {
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["build"] = "RealBuildPlateBasic4Holes",
            ["drawing"] = "RealDrawingBasicViews",
            ["dimension"] = "RealDrawingDimensions",
            ["title_block"] = "RealDrawingTitleBlock"
        };
        return evidence.Count == expected.Count && evidence.All(item =>
            expected.TryGetValue(item.Stage, out var mode) &&
            string.Equals(item.WorkerName, "RealSolidWorksWorker", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ExecutionMode, mode, StringComparison.OrdinalIgnoreCase) &&
            item.RealCadExecuted && item.RealCadConnected && item.QualityGatePassed && string.IsNullOrWhiteSpace(item.FailureStage));
    }

    private static string? DetermineFailureStage(SolidWorksReleaseManifest manifest, IReadOnlyList<string> issues)
    {
        if (issues.Any(issue =>
                issue.StartsWith("copy_failed:", StringComparison.OrdinalIgnoreCase) ||
                issue.StartsWith("source_read_failed:", StringComparison.OrdinalIgnoreCase))) return "artifact_copy_failed";
        if (manifest.Artifacts.Any(item => !item.Exists || item.SizeBytes <= 0)) return "source_artifacts_missing";
        if (manifest.Reports.Any(item => !item.Exists || item.SizeBytes <= 0)) return "source_report_missing";
        return null;
    }

    private static SolidWorksSourceReportStatus ToStatus(SolidWorksReleaseManifestItem item) =>
        new(item.Name, item.FinalStatus, item.FailureStage, item.SourcePath, item.PackagePath,
            string.Equals(item.FinalStatus, "Passed", StringComparison.OrdinalIgnoreCase) ? "source report passed." : "source report does not pass.");

    private static (string? FinalStatus, string? FailureStage) ReadReportStatus(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            return (
                root.TryGetProperty("final_status", out var status) ? status.GetString() : null,
                root.TryGetProperty("failure_stage", out var stage) ? stage.GetString() : null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return ("InvalidJson", "source_report_missing");
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions()), cancellationToken);

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string BuildSummary(SolidWorksReleaseManifest manifest)
    {
        var text = new StringBuilder();
        text.AppendLine("# V1.7 SolidWorks 端到端发布包摘要");
        text.AppendLine();
        text.AppendLine($"- all_source_reports_passed：`{manifest.AllSourceReportsPassed}`");
        text.AppendLine($"- real_execution_evidence_passed：`{manifest.RealExecutionEvidencePassed}`");
        text.AppendLine($"- deliverable_status：`{manifest.DeliverableStatus}`");
        text.AppendLine($"- final_status：`{manifest.FinalStatus}`");
        text.AppendLine($"- failure_stage：`{manifest.FailureStage ?? "none"}`");
        text.AppendLine();
        text.AppendLine("本发布包只使用本次受控主工作流传入的精确源路径，不扫描历史 latest 输出，也不以 SmokeRunner 结果作为成功依据。");
        return text.ToString();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
