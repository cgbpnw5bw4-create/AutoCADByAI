namespace DomainSchemas;

public sealed record SolidWorksBuildPlan(
    string PlanId,
    string SourceCadModelSpecId,
    string TargetCadSystem,
    string PartType,
    string Unit,
    IReadOnlyList<SolidWorksOperation> Operations,
    IReadOnlyList<SolidWorksArtifact> ExpectedArtifacts,
    IReadOnlyList<string> ValidationRules,
    IReadOnlyList<string> RiskNotes);

public sealed record SolidWorksOperation(
    string OperationId,
    string OperationType,
    string SketchPlane,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> DependsOn,
    string ExpectedResult);

public sealed record SolidWorksWorkerRequest(
    string RequestId,
    SolidWorksBuildPlan BuildPlan,
    string OutputDirectory,
    bool DryRun = true,
    bool AllowRealCadExecution = false,
    bool ConnectionSmokeTestOnly = false,
    bool DrawingSmokeTestOnly = false,
    string? SourcePartPath = null,
    string? DrawingTemplatePath = null,
    bool DrawingDimensionSmokeTestOnly = false,
    string? SourceDrawingPath = null,
    bool DrawingTitleBlockSmokeTestOnly = false,
    string? SourceDimensionedDrawingPath = null);

public sealed record SolidWorksWorkerResult(
    string RequestId,
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string ExecutionMode = "Fake",
    bool RealCadExecuted = false,
    bool RealCadConnected = false,
    SolidWorksPreflightReport? PreflightReport = null);

public sealed record SolidWorksArtifact(
    string ArtifactId,
    string ArtifactType,
    string FilePath,
    string ExpectedExtension,
    bool Exists,
    long SizeBytes,
    string Description);

public sealed record SolidWorksRuntimeOptions(
    bool EnableRealExecution,
    bool Visible,
    string? TemplatePartPath,
    string OutputDirectory,
    int ConnectTimeoutSeconds,
    int ExecutionTimeoutSeconds,
    string? DrawingTemplatePath = null)
{
    public const int DefaultConnectTimeoutSeconds = 30;
    public const int MinimumConnectTimeoutSeconds = 1;
    public const int MaximumConnectTimeoutSeconds = 600;
    public const int DefaultExecutionTimeoutSeconds = 300;
    public const int MinimumExecutionTimeoutSeconds = 1;
    public const int MaximumExecutionTimeoutSeconds = 3_600;

    public static SolidWorksRuntimeOptions FromEnvironment(IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string name) =>
            environment is null
                ? Environment.GetEnvironmentVariable(name)
                : environment.TryGetValue(name, out var value) ? value : null;

        return new SolidWorksRuntimeOptions(
            ParseBool(Get("SW_ENABLE_REAL_EXECUTION")),
            ParseBool(Get("SW_VISIBLE")),
            EmptyToNull(Get("SW_TEMPLATE_PART_PATH")),
            EmptyToNull(Get("SW_OUTPUT_DIRECTORY")) ?? DefaultOutputDirectory(),
            ParseTimeout(Get("SW_CONNECT_TIMEOUT_SECONDS")),
            ParseExecutionTimeout(Get("SW_EXECUTION_TIMEOUT_SECONDS")),
            EmptyToNull(Get("SW_TEMPLATE_DRAWING_PATH")));
    }

    private static string DefaultOutputDirectory() =>
        Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "output", "solidworks", "real"));

    private static bool ParseBool(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static int ParseTimeout(string? value) =>
        int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, MinimumConnectTimeoutSeconds, MaximumConnectTimeoutSeconds)
            : DefaultConnectTimeoutSeconds;

    private static int ParseExecutionTimeout(string? value) =>
        int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, MinimumExecutionTimeoutSeconds, MaximumExecutionTimeoutSeconds)
            : DefaultExecutionTimeoutSeconds;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

public sealed class SolidWorksDrawingReport
{
    public string DrawingId { get; set; } = $"solidworks-drawing-{Guid.NewGuid():N}";

    public string? SourcePartPath { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public bool SolidWorksConnected { get; set; }

    public string? SolidWorksVersion { get; set; }

    public bool DrawingCreated { get; set; }

    public List<string> ViewsCreated { get; } = [];

    public string? SlddrwPath { get; set; }

    public bool SlddrwExists { get; set; }

    public long SlddrwSizeBytes { get; set; }

    public string? PdfPath { get; set; }

    public bool PdfExists { get; set; }

    public long PdfSizeBytes { get; set; }

    public List<string> Operations { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public string? FailureStage { get; set; }

    public string FinalStatus { get; set; } = "Failed";
}

public sealed class SolidWorksDrawingDimensionReport
{
    public string DimensionId { get; set; } = $"solidworks-drawing-dimension-{Guid.NewGuid():N}";

    public string? SourceDrawingPath { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public bool SolidWorksConnected { get; set; }

    public string? SolidWorksVersion { get; set; }

    public bool DrawingOpened { get; set; }

    public List<string> ViewsConfirmed { get; } = [];

    public bool LengthDimensionAdded { get; set; }

    public bool WidthDimensionAdded { get; set; }

    public bool ThicknessDimensionAdded { get; set; }

    public bool HoleDiameterDimensionAdded { get; set; }

    public bool HolePositionDimensionAdded { get; set; }

    public List<SolidWorksDrawingDimensionResult> Dimensions { get; } = [];

    public string? SlddrwPath { get; set; }

    public bool SlddrwExists { get; set; }

    public long SlddrwSizeBytes { get; set; }

    public string? PdfPath { get; set; }

    public bool PdfExists { get; set; }

    public long PdfSizeBytes { get; set; }

    public List<string> Operations { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public string? FailureStage { get; set; }

    public string FinalStatus { get; set; } = "Failed";
}

public sealed record SolidWorksDrawingDimensionResult(
    string Name,
    double ExpectedValueMm,
    string Status,
    string FailureStage,
    string ApiStrategy,
    string Message);

public sealed class SolidWorksDrawingTitleBlockReport
{
    public string TitleBlockId { get; set; } = $"solidworks-drawing-title-block-{Guid.NewGuid():N}";

    public string? SourceDimensionedDrawingPath { get; set; }

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public bool SolidWorksConnected { get; set; }

    public string? SolidWorksVersion { get; set; }

    public bool DrawingOpened { get; set; }

    public bool TitleBlockTemplateDetected { get; set; }

    public bool DrawingPropertiesRead { get; set; }

    public bool CustomPropertiesWritten { get; set; }

    public bool TitleBlockUpdated { get; set; }

    public string TitleBlockPopulationStrategy { get; set; } = "custom_properties_only";

    public bool TitleBlockFieldsVerifiedInSheetFormat { get; set; }

    public string PartName { get; set; } = string.Empty;

    public string DrawingNumber { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    public string Scale { get; set; } = string.Empty;

    public string DrawingDate { get; set; } = string.Empty;

    public string Revision { get; set; } = string.Empty;

    public List<SolidWorksDrawingTitleBlockProperty> Properties { get; } = [];

    public string? SlddrwPath { get; set; }

    public bool SlddrwExists { get; set; }

    public long SlddrwSizeBytes { get; set; }

    public string? PdfPath { get; set; }

    public bool PdfExists { get; set; }

    public long PdfSizeBytes { get; set; }

    public List<string> Operations { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public string? FailureStage { get; set; }

    public string FinalStatus { get; set; } = "Failed";
}

public sealed record SolidWorksDrawingTitleBlockProperty(
    string Name,
    string Value,
    string Status,
    string FailureStage,
    string ApiStrategy,
    string Message);

public sealed class SolidWorksReleaseManifest
{
    public string ReleaseId { get; set; } = $"solidworks-release-{Guid.NewGuid():N}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string PartName { get; set; } = "plate_basic_4holes";

    public string SourceRoot { get; set; } = string.Empty;

    public string OutputDirectory { get; set; } = string.Empty;

    public List<SolidWorksReleaseManifestItem> Artifacts { get; } = [];

    public List<SolidWorksReleaseManifestItem> Reports { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public string PackageBuildStatus { get; set; } = "Failed";

    public bool SourceReportsChecked { get; set; }

    public bool AllSourceReportsPassed { get; set; }

    public List<SolidWorksSourceReportStatus> SourceReportFailures { get; } = [];

    public List<SolidWorksSourceReportStatus> SourceReportWarnings { get; } = [];

    public string DeliverableStatus { get; set; } = "NotDeliverable";

    public string? FailureStage { get; set; }

    public string FinalStatus { get; set; } = "Failed";
}

public sealed class SolidWorksReleaseManifestItem
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = string.Empty;

    public bool Required { get; set; } = true;

    public string? SourcePath { get; set; }

    public string? PackagePath { get; set; }

    public bool Exists { get; set; }

    public long SizeBytes { get; set; }

    public string? Sha256 { get; set; }

    public DateTimeOffset? SourceLastWriteTimeUtc { get; set; }

    public string? FinalStatus { get; set; }

    public string? FailureStage { get; set; }
}

public sealed class SolidWorksPackageQualityReport
{
    public string QualityReportId { get; set; } = $"solidworks-release-quality-{Guid.NewGuid():N}";

    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;

    public string OutputDirectory { get; set; } = string.Empty;

    public bool ManifestExists { get; set; }

    public bool ReleaseSummaryExists { get; set; }

    public bool ArtifactsCollected { get; set; }

    public bool ReportsCollected { get; set; }

    public bool PdfExists { get; set; }

    public bool PathsUnderReleaseDirectory { get; set; }

    public bool FileSizesValid { get; set; }

    public bool ReportsFinalStatusChecked { get; set; }

    public bool FailureStagesChecked { get; set; }

    public string PackageBuildStatus { get; set; } = "Failed";

    public bool SourceReportsChecked { get; set; }

    public bool AllSourceReportsPassed { get; set; }

    public List<SolidWorksSourceReportStatus> SourceReportFailures { get; } = [];

    public List<SolidWorksSourceReportStatus> SourceReportWarnings { get; } = [];

    public string DeliverableStatus { get; set; } = "NotDeliverable";

    public List<SolidWorksPackageQualityCheck> Checks { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public string? FailureStage { get; set; }

    public string FinalStatus { get; set; } = "Failed";
}

public sealed record SolidWorksSourceReportStatus(
    string Name,
    string? FinalStatus,
    string? FailureStage,
    string? SourcePath,
    string? PackagePath,
    string Message);

public sealed record SolidWorksPackageQualityCheck(
    string Name,
    string Status,
    string? FailureStage,
    string Message);

public sealed record SolidWorksPreflightReport(
    string ReportId,
    DateTimeOffset CheckedAt,
    bool OsIsWindows,
    bool SolidWorksComAvailable,
    bool SolidWorksApplicationConnectable,
    string? SolidWorksVersion,
    string? TemplatePartPath,
    bool TemplatePartExists,
    string OutputDirectory,
    bool OutputDirectoryWritable,
    bool RealExecutionEnabled,
    bool VisibleMode,
    IReadOnlyList<string> Issues,
    IReadOnlyList<string> Warnings,
    string FinalStatus);

public static class SolidWorksPreflightEvaluator
{
    public static SolidWorksPreflightReport Evaluate(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        bool allowComProbe = false,
        Func<bool>? comAvailabilityProbe = null)
    {
        var issues = new List<string>();
        var warnings = new List<string>();
        var osIsWindows = OperatingSystem.IsWindows();
        var outputDirectory = Path.GetFullPath(
            string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? options.OutputDirectory
                : request.OutputDirectory);
        var outputDirectoryWritable = EnsureOutputDirectoryWritable(outputDirectory, issues);

        if (request.DryRun)
        {
            issues.Add("dry_run_mode_enabled: RealSolidWorksWorker requires dry_run=false before connection smoke test.");
        }

        if (!request.AllowRealCadExecution)
        {
            issues.Add("real_cad_execution_not_enabled: request.allow_real_cad_execution must be true before real connection.");
        }

        if (!options.EnableRealExecution)
        {
            issues.Add("missing_user_safety_confirmation: SW_ENABLE_REAL_EXECUTION=true is required before real connection.");
        }

        if (!osIsWindows)
        {
            warnings.Add("solidworks_windows_required: SolidWorks COM automation is only available on Windows.");
        }

        var templatePartPath = options.TemplatePartPath;
        var templatePartExists = !string.IsNullOrWhiteSpace(templatePartPath) && File.Exists(templatePartPath);
        if (!string.IsNullOrWhiteSpace(templatePartPath) && !templatePartExists)
        {
            warnings.Add("template_part_missing: SW_TEMPLATE_PART_PATH was provided but the file does not exist.");
        }

        var solidWorksComAvailable = false;
        if (allowComProbe && options.EnableRealExecution && osIsWindows)
        {
            try
            {
                solidWorksComAvailable = comAvailabilityProbe?.Invoke() == true;
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
            {
                issues.Add($"solidworks_com_probe_failed: {ex.Message}");
            }
        }

        return new SolidWorksPreflightReport(
            $"solidworks-preflight-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            osIsWindows,
            solidWorksComAvailable,
            SolidWorksApplicationConnectable: false,
            SolidWorksVersion: null,
            templatePartPath,
            templatePartExists,
            outputDirectory,
            outputDirectoryWritable,
            options.EnableRealExecution,
            options.Visible,
            issues,
            warnings,
            ResolveFinalStatus(options, osIsWindows, outputDirectoryWritable, issues, warnings));
    }

    private static bool EnsureOutputDirectoryWritable(string outputDirectory, List<string> issues)
    {
        try
        {
            Directory.CreateDirectory(outputDirectory);
            var probePath = Path.Combine(outputDirectory, $".write_probe_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "solidworks preflight write probe");
            File.Delete(probePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add($"output_directory_not_writable: {ex.Message}");
            return false;
        }
    }

    private static string ResolveFinalStatus(
        SolidWorksRuntimeOptions options,
        bool osIsWindows,
        bool outputDirectoryWritable,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings)
    {
        if (!options.EnableRealExecution)
        {
            return outputDirectoryWritable ? "Skipped" : "Failed";
        }

        if (!osIsWindows || !outputDirectoryWritable || issues.Count > 0)
        {
            return "Failed";
        }

        return warnings.Count > 0 ? "Warning" : "Passed";
    }
}
