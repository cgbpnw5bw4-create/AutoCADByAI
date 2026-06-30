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
    bool ConnectionSmokeTestOnly = false);

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
    int ConnectTimeoutSeconds)
{
    public const int DefaultConnectTimeoutSeconds = 30;
    public const int MinimumConnectTimeoutSeconds = 1;
    public const int MaximumConnectTimeoutSeconds = 600;

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
            ParseTimeout(Get("SW_CONNECT_TIMEOUT_SECONDS")));
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

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

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
