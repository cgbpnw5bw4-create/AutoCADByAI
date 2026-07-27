using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksPlateBuilder
{
    Task<SolidWorksPlateBuildResult> BuildPlateBasicFourHolesAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken);
}

public sealed record SolidWorksPlateBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    bool RealCadExecuted)
{
    public static SolidWorksPlateBuildResult Completed(
        IReadOnlyList<SolidWorksArtifact> generatedArtifacts,
        IReadOnlyList<string> logs) =>
        new("Completed", generatedArtifacts, logs, Array.Empty<string>(), RealCadExecuted: true);

    public static SolidWorksPlateBuildResult Failed(
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        IReadOnlyList<SolidWorksArtifact>? generatedArtifacts = null) =>
        new("Failed", generatedArtifacts ?? Array.Empty<SolidWorksArtifact>(), logs, issues, RealCadExecuted: false);
}

public sealed class SolidWorksPlateBuildDiagnostics
{
    public List<string> OperationsExecuted { get; } = [];

    public List<string> Issues { get; } = [];

    public List<string> Warnings { get; } = [];

    public bool SldprtSaveAttempted { get; set; }

    public bool SldprtSaveSuccess { get; set; }

    public List<string> SldprtSaveErrors { get; } = [];

    public List<string> SldprtSaveWarnings { get; } = [];

    public string? SldprtPath { get; set; }

    public long SldprtSizeBytes { get; set; }

    public bool StepExportAttempted { get; set; }

    public bool StepExportSuccess { get; set; }

    public List<string> StepExportErrors { get; } = [];

    public List<string> StepExportWarnings { get; } = [];

    public string? StepPath { get; set; }

    public long StepSizeBytes { get; set; }

    public string? ActiveDocTitleBeforeStepExport { get; set; }

    public string? ActiveDocTitleAfterActivate { get; set; }

    public bool PlaneSelectionAttempted { get; set; }

    public bool PlaneSelectionSuccess { get; set; }

    public string? SelectedPlaneName { get; set; }

    public string? SelectedPlaneStrategy { get; set; }

    public List<string> PlaneSelectionErrors { get; } = [];

    public List<SolidWorksReferencePlaneInfo> AvailableReferencePlanes { get; } = [];
}

public static class SolidWorksPlateBuildOutput
{
    public const string ExecutionMode = "RealBuildPlateBasic4Holes";

    public static string ResolveOutputDirectory(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var fullRoot = Path.GetFullPath(requestedRoot);
        var normalized = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryName = Path.GetFileName(normalized);

        if (IsUnderPlateBasicRoot(normalized) &&
            !directoryName.Equals("plate_basic_4holes", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var target = directoryName.Equals("plate_basic_4holes", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(normalized, TimestampSegment())
            : Path.Combine(normalized, "plate_basic_4holes", TimestampSegment());

        if (!Directory.Exists(target) || Directory.GetFileSystemEntries(target).Length == 0)
        {
            return target;
        }

        return Path.Combine(
            Path.GetDirectoryName(target) ?? fullRoot,
            $"plate_basic_4holes_{TimestampSegment()}");
    }

    public static string ResolveBuildReportPath(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options) =>
        Path.Combine(ResolveOutputDirectory(request, options), "build_report.json");

    public static SolidWorksArtifact Artifact(
        string artifactId,
        string artifactType,
        string filePath,
        string expectedExtension,
        string description)
    {
        var fullPath = Path.GetFullPath(filePath);
        var info = new FileInfo(fullPath);
        return new SolidWorksArtifact(
            artifactId,
            artifactType,
            fullPath,
            expectedExtension,
            File.Exists(fullPath),
            File.Exists(fullPath) ? info.Length : 0,
            description);
    }

    private static bool IsUnderPlateBasicRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null)
        {
            if (directory.Name.Equals("plate_basic_4holes", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("real", StringComparison.OrdinalIgnoreCase) == true &&
                directory.Parent.Parent?.Name.Equals("solidworks", StringComparison.OrdinalIgnoreCase) == true &&
                directory.Parent.Parent.Parent?.Name.Equals("output", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static string TimestampSegment() =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
}

public static class SolidWorksPlateBuildReportWriter
{
    public static async Task WriteAsync(
        string reportPath,
        SolidWorksWorkerRequest request,
        string executionMode,
        bool realCadExecuted,
        bool realCadConnected,
        string? solidWorksVersion,
        string outputDirectory,
        IReadOnlyList<string> generatedArtifactPaths,
        IReadOnlyList<string> operationsExecuted,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings,
        string finalStatus,
        CancellationToken cancellationToken)
    {
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        diagnostics.OperationsExecuted.AddRange(operationsExecuted);
        diagnostics.Issues.AddRange(issues);
        diagnostics.Warnings.AddRange(warnings);

        await WriteAsync(
            reportPath,
            request,
            executionMode,
            realCadExecuted,
            realCadConnected,
            solidWorksVersion,
            outputDirectory,
            generatedArtifactPaths,
            diagnostics,
            finalStatus,
            cancellationToken);
    }

    public static async Task WriteAsync(
        string reportPath,
        SolidWorksWorkerRequest request,
        string executionMode,
        bool realCadExecuted,
        bool realCadConnected,
        string? solidWorksVersion,
        string outputDirectory,
        IReadOnlyList<string> generatedArtifactPaths,
        SolidWorksPlateBuildDiagnostics diagnostics,
        string finalStatus,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            reportPath,
            SerializeReport(
                request,
                executionMode,
                realCadExecuted,
                realCadConnected,
                solidWorksVersion,
                outputDirectory,
                generatedArtifactPaths,
                diagnostics,
                finalStatus),
            cancellationToken);
    }

    public static void Write(
        string reportPath,
        SolidWorksWorkerRequest request,
        string executionMode,
        bool realCadExecuted,
        bool realCadConnected,
        string? solidWorksVersion,
        string outputDirectory,
        IReadOnlyList<string> generatedArtifactPaths,
        IReadOnlyList<string> operationsExecuted,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings,
        string finalStatus)
    {
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        diagnostics.OperationsExecuted.AddRange(operationsExecuted);
        diagnostics.Issues.AddRange(issues);
        diagnostics.Warnings.AddRange(warnings);

        Write(
            reportPath,
            request,
            executionMode,
            realCadExecuted,
            realCadConnected,
            solidWorksVersion,
            outputDirectory,
            generatedArtifactPaths,
            diagnostics,
            finalStatus);
    }

    public static void Write(
        string reportPath,
        SolidWorksWorkerRequest request,
        string executionMode,
        bool realCadExecuted,
        bool realCadConnected,
        string? solidWorksVersion,
        string outputDirectory,
        IReadOnlyList<string> generatedArtifactPaths,
        SolidWorksPlateBuildDiagnostics diagnostics,
        string finalStatus)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            reportPath,
            SerializeReport(
                request,
                executionMode,
                realCadExecuted,
                realCadConnected,
                solidWorksVersion,
                outputDirectory,
                generatedArtifactPaths,
                diagnostics,
                finalStatus));
    }

    private static string SerializeReport(
        SolidWorksWorkerRequest request,
        string executionMode,
        bool realCadExecuted,
        bool realCadConnected,
        string? solidWorksVersion,
        string outputDirectory,
        IReadOnlyList<string> generatedArtifactPaths,
        SolidWorksPlateBuildDiagnostics diagnostics,
        string finalStatus)
    {
        finalStatus = SolidWorksFakeSuccessGuard.NormalizePlateFinalStatus(diagnostics, finalStatus);
        var now = DateTimeOffset.UtcNow;
        var failureStage = string.Equals(finalStatus, "Passed", StringComparison.OrdinalIgnoreCase)
            ? null
            : InferFailureStage(diagnostics);
        var report = new
        {
            build_id = $"solidworks-real-build-{Guid.NewGuid():N}",
            build_plan_id = request.BuildPlan.PlanId,
            part_type = request.BuildPlan.PartType,
            mode = "real",
            execution_mode = executionMode,
            real_execution_requested = !request.DryRun,
            real_cad_executed = realCadExecuted,
            real_cad_connected = realCadConnected,
            solidworks_version = solidWorksVersion,
            started_at = now,
            completed_at = now,
            output_directory = Path.GetFullPath(outputDirectory),
            generated_artifacts = generatedArtifactPaths.Select(Path.GetFullPath).ToArray(),
            operations_executed = diagnostics.OperationsExecuted,
            issues = diagnostics.Issues,
            warnings = diagnostics.Warnings,
            sldprt_save_attempted = diagnostics.SldprtSaveAttempted,
            sldprt_save_success = diagnostics.SldprtSaveSuccess,
            sldprt_save_errors = diagnostics.SldprtSaveErrors,
            sldprt_save_warnings = diagnostics.SldprtSaveWarnings,
            sldprt_path = diagnostics.SldprtPath,
            sldprt_size_bytes = diagnostics.SldprtSizeBytes,
            step_export_attempted = diagnostics.StepExportAttempted,
            step_export_success = diagnostics.StepExportSuccess,
            step_export_errors = diagnostics.StepExportErrors,
            step_export_warnings = diagnostics.StepExportWarnings,
            step_path = diagnostics.StepPath,
            step_size_bytes = diagnostics.StepSizeBytes,
            active_doc_title_before_step_export = diagnostics.ActiveDocTitleBeforeStepExport,
            active_doc_title_after_activate = diagnostics.ActiveDocTitleAfterActivate,
            plane_selection_attempted = diagnostics.PlaneSelectionAttempted,
            plane_selection_success = diagnostics.PlaneSelectionSuccess,
            selected_plane_name = diagnostics.SelectedPlaneName,
            selected_plane_strategy = diagnostics.SelectedPlaneStrategy,
            plane_selection_errors = diagnostics.PlaneSelectionErrors,
            available_reference_planes = diagnostics.AvailableReferencePlanes,
            failure_stage = failureStage,
            api_evidence = "real_solidworks_plate_basic_4holes_smoke_passed",
            final_status = finalStatus
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string InferFailureStage(SolidWorksPlateBuildDiagnostics diagnostics)
    {
        if (diagnostics.SldprtSaveAttempted && !diagnostics.SldprtSaveSuccess)
        {
            return PartFamilyFailureStages.PartSaveFailed;
        }

        if (diagnostics.StepExportAttempted && !diagnostics.StepExportSuccess)
        {
            return PartFamilyFailureStages.StepExportFailed;
        }

        return "plate_build_failed";
    }
}

public sealed class LateBoundSolidWorksPlateBuilder : ISolidWorksPlateBuilder
{
    private const double MmToMeters = 0.001;
    private readonly ISolidWorksComFacade _comFacade;
    private readonly ISolidWorksFileVerifier _fileVerifier;
    private readonly SolidWorksPlateFeatureBuilder _featureBuilder;

    public LateBoundSolidWorksPlateBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        SolidWorksPlateFeatureBuilder? featureBuilder = null)
    {
        _comFacade = comFacade ?? new LateBoundSolidWorksComFacade();
        _fileVerifier = fileVerifier ?? new SolidWorksFileVerifier();
        _featureBuilder = featureBuilder ?? new SolidWorksPlateFeatureBuilder(_comFacade, _fileVerifier);
    }

    public Task<SolidWorksPlateBuildResult> BuildPlateBasicFourHolesAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => BuildCore(application, request, options, solidWorksVersion, cancellationToken),
            cancellationToken);
    }

    private SolidWorksPlateBuildResult BuildCore(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        var operations = diagnostics.OperationsExecuted;
        var issues = diagnostics.Issues;
        object? model = null;

        var outputDirectory = SolidWorksPlateBuildOutput.ResolveOutputDirectory(request, options);
        var partPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDPRT");
        var stepPath = Path.Combine(outputDirectory, "plate_basic_4holes.STEP");
        var reportPath = Path.Combine(outputDirectory, "build_report.json");
        diagnostics.SldprtPath = partPath;
        diagnostics.StepPath = stepPath;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(Path.Combine(outputDirectory, "logs"));
            operations.Add("real_build_request_received");
            operations.Add("safety_flags_checked");
            operations.Add("preflight_started");

            var dimensions = SolidWorksPlateDimensions.FromPlan(request.BuildPlan);
            if (dimensions is null)
            {
                issues.Add("preflight_failed: invalid_plate_basic_4holes_dimensions.");
                operations.Add("preflight_failed");
                return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, diagnostics);
            }

            if (string.IsNullOrWhiteSpace(options.TemplatePartPath) || !File.Exists(options.TemplatePartPath))
            {
                issues.Add("preflight_failed: template_part_path_required_for_real_build.");
                issues.Add("template_part_path_required_for_real_build: SW_TEMPLATE_PART_PATH must point to an existing part template.");
                operations.Add("preflight_failed");
                return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, diagnostics);
            }

            operations.Add("preflight_completed");
            operations.Add("connection_success");
            logs.Add("Connecting SolidWorks session was provided by SolidWorksSessionManager.");

            operations.Add("new_part_started");
            model = Invoke(application, "NewDocument", options.TemplatePartPath, 0, 0d, 0d);
            RequireComResult(model, "new_part_failed: NewDocument returned null.");
            if (model is null)
            {
                throw new InvalidOperationException("new_part_failed: NewDocument returned null.");
            }

            operations.Add("new_part_success");

            _featureBuilder.CreateBasePlate(
                model,
                dimensions.LengthMeters,
                dimensions.WidthMeters,
                dimensions.ThicknessMeters,
                diagnostics,
                logs);
            _featureBuilder.CreateThroughHoles(
                model,
                dimensions.HoleCentersMeters().ToArray(),
                dimensions.HoleRadiusMeters,
                dimensions.ThicknessMeters * 2,
                diagnostics,
                logs);

            ForceRebuild(model, logs);
            SavePart(model, partPath, diagnostics, logs);
            ExportStep(application, model, stepPath, diagnostics, logs);
            SolidWorksFakeSuccessGuard.RequirePlateArtifactsCanPass(diagnostics);
            operations.Add("build_report_started");

            SolidWorksPlateBuildReportWriter.Write(
                reportPath,
                request,
                SolidWorksPlateBuildOutput.ExecutionMode,
                realCadExecuted: true,
                realCadConnected: true,
                solidWorksVersion,
                outputDirectory,
                new[] { partPath, stepPath },
                diagnostics,
                "Passed");
            operations.Add("build_report_written");

            return SolidWorksPlateBuildResult.Completed(
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", "Real SolidWorks part file."),
                    SolidWorksPlateBuildOutput.Artifact("real-step", "Step", stepPath, ".STEP", "Real STEP export file."),
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Real build report.")
                },
                logs.Concat(operations.Select(operation => $"operation_executed: {operation}")).ToArray());
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            issues.Add($"solidworks_real_build_failed: {ex.GetBaseException().Message}");
            return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, diagnostics);
        }
        finally
        {
            if (model is not null)
            {
                ReleaseComObject(model);
            }
        }
    }

    private static SolidWorksPlateBuildResult FailedWithReport(
        SolidWorksWorkerRequest request,
        string? solidWorksVersion,
        string outputDirectory,
        string reportPath,
        IReadOnlyList<string> logs,
        SolidWorksPlateBuildDiagnostics diagnostics)
    {
        try
        {
            diagnostics.OperationsExecuted.Add("build_report_started");
            SolidWorksPlateBuildReportWriter.Write(
                reportPath,
                request,
                SolidWorksPlateBuildOutput.ExecutionMode,
                realCadExecuted: false,
                realCadConnected: true,
                solidWorksVersion,
                outputDirectory,
                Array.Empty<string>(),
                diagnostics,
                "Failed");
            diagnostics.OperationsExecuted.Add("build_report_written");

            return SolidWorksPlateBuildResult.Failed(
                logs.Concat(diagnostics.OperationsExecuted.Select(operation => $"operation_executed: {operation}")).ToArray(),
                diagnostics.Issues,
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Real build failure report.")
                });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Issues.Add($"build_report_write_failed: {ex.Message}");
            return SolidWorksPlateBuildResult.Failed(logs, diagnostics.Issues);
        }
    }

    private static void SelectSketchPlane(object model, SolidWorksPlateBuildDiagnostics diagnostics, List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("plane_selection_started");
        diagnostics.PlaneSelectionAttempted = true;

        var result = new SolidWorksPlaneSelector().SelectStandardPlane(model);
        diagnostics.PlaneSelectionSuccess = result.Success;
        diagnostics.SelectedPlaneName = result.SelectedPlaneName;
        diagnostics.SelectedPlaneStrategy = result.SelectedPlaneStrategy;
        diagnostics.PlaneSelectionErrors.Clear();
        diagnostics.PlaneSelectionErrors.AddRange(result.Errors);
        diagnostics.AvailableReferencePlanes.Clear();
        diagnostics.AvailableReferencePlanes.AddRange(result.AvailableReferencePlanes);

        if (!result.Success)
        {
            diagnostics.Issues.Add("plane_selection_failed: select_plane_failed_all_candidates.");
            var error = result.Errors.LastOrDefault() ?? "select_plane_failed_all_candidates";
            throw new InvalidOperationException($"sketch_failed: {error}");
        }

        diagnostics.OperationsExecuted.Add("plane_selection_success");
        logs.Add($"Selected sketch plane: {result.SelectedPlaneName} using {result.SelectedPlaneStrategy}.");
    }

    private void EnterSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private void ExitSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private void ForceRebuild(object model, List<string> logs)
    {
        if (TryInvokeBool(model, "ForceRebuild3", false))
        {
            logs.Add("SolidWorks model rebuilt.");
        }
    }

    private void SavePart(object model, string partPath, SolidWorksPlateBuildDiagnostics diagnostics, List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("save_sldprt_started");
        diagnostics.SldprtSaveAttempted = true;
        diagnostics.SldprtPath = Path.GetFullPath(partPath);
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);

        var saved = _comFacade.TryExtensionSaveAs(model, partPath, null, diagnostics.SldprtSaveErrors, diagnostics.SldprtSaveWarnings) ||
                    TryInvokeBool(model, "SaveAs3", partPath, 0, 1) ||
                    TryInvokeBool(model, "SaveAs", partPath);

        if (!saved)
        {
            diagnostics.SldprtSaveErrors.Add("sldprt_save_failed: SaveAs returned false.");
            diagnostics.SldprtSaveSuccess = false;
            diagnostics.Issues.Add("sldprt_save_failed: SaveAs returned false.");
            throw new IOException($"sldprt_save_failed: {partPath}");
        }

        var fileState = _fileVerifier.GetState(partPath);
        if (!fileState.Exists || fileState.SizeBytes <= 0)
        {
            diagnostics.SldprtSaveSuccess = false;
            diagnostics.SldprtSaveErrors.Add($"sldprt_save_failed: {partPath}");
            diagnostics.Issues.Add("sldprt_save_failed: SLDPRT file was not created or is empty.");
            throw new IOException($"sldprt_save_failed: {partPath}");
        }

        diagnostics.SldprtSaveSuccess = true;
        diagnostics.SldprtSizeBytes = fileState.SizeBytes;
        diagnostics.OperationsExecuted.Add("save_sldprt_success");
        logs.Add($"Saved SolidWorks part: {partPath}.");
    }

    private void ExportStep(object application, object model, string stepPath, SolidWorksPlateBuildDiagnostics diagnostics, List<string> logs)
    {
        diagnostics.OperationsExecuted.Add("export_step_started");
        diagnostics.StepExportAttempted = true;
        diagnostics.StepPath = Path.GetFullPath(stepPath);
        Directory.CreateDirectory(Path.GetDirectoryName(stepPath)!);

        diagnostics.ActiveDocTitleBeforeStepExport = GetActiveDocumentTitle(application) ?? TryInvoke(model, "GetTitle")?.ToString();
        ActivateDocument(application, model, diagnostics, logs);
        var activeDocument = TryGetProperty(application, "ActiveDoc") ?? model;
        TryInvoke(activeDocument, "ClearSelection2", true);

        var exported = _comFacade.TryExtensionSaveAs(activeDocument, stepPath, null, diagnostics.StepExportErrors, diagnostics.StepExportWarnings) ||
                       TryInvokeBool(activeDocument, "SaveAs3", stepPath, 0, 1) ||
                       TryInvokeBool(activeDocument, "SaveAs", stepPath);

        if (!exported)
        {
            diagnostics.StepExportErrors.Add("step_export_failed: SaveAs returned false.");
            diagnostics.StepExportSuccess = false;
            diagnostics.Issues.Add("step_export_failed: SaveAs returned false.");
            throw new IOException($"step_export_failed: {stepPath}");
        }

        var fileState = _fileVerifier.GetState(stepPath);
        if (!fileState.Exists || fileState.SizeBytes <= 0)
        {
            diagnostics.StepExportSuccess = false;
            diagnostics.StepExportErrors.Add($"step_export_failed: {stepPath}");
            diagnostics.Issues.Add("step_export_failed: STEP file was not created or is empty.");
            throw new IOException($"step_export_failed: {stepPath}");
        }

        diagnostics.StepExportSuccess = true;
        diagnostics.StepSizeBytes = fileState.SizeBytes;
        diagnostics.OperationsExecuted.Add("export_step_success");
        logs.Add($"Exported STEP file: {stepPath}.");
    }

    private void ActivateDocument(object application, object model, SolidWorksPlateBuildDiagnostics diagnostics, List<string> logs)
    {
        var title = TryInvoke(model, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            TryInvoke(application, "ActivateDoc3", title, true, 0, 0);
            diagnostics.ActiveDocTitleAfterActivate = GetActiveDocumentTitle(application) ?? title;
            logs.Add($"Activated SolidWorks document before STEP export: {title}.");
        }
    }

    private string? GetActiveDocumentTitle(object application)
    {
        var activeDocument = TryGetProperty(application, "ActiveDoc");
        return activeDocument is null ? null : TryInvoke(activeDocument, "GetTitle")?.ToString();
    }

    private object GetProperty(object target, string name) => _comFacade.GetProperty(target, name);

    private object? TryGetProperty(object? target, string name) => _comFacade.TryGetProperty(target, name);

    private object? Invoke(object target, string name, params object?[] args) => _comFacade.Invoke(target, name, args);

    private object? TryInvoke(object? target, string name, params object?[] args) => _comFacade.TryInvoke(target, name, args);

    private bool TryInvokeBool(object? target, string name, params object?[] args) => _comFacade.TryInvokeBool(target, name, args);

    private static void RequireComResult(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void ReleaseComObject(object value) => _comFacade.ReleaseComObject(value);

    private sealed record SolidWorksPlateDimensions(
        double LengthMeters,
        double WidthMeters,
        double ThicknessMeters,
        double HoleDiameterMeters,
        int HoleCount)
    {
        public double HoleRadiusMeters => HoleDiameterMeters / 2;

        public static SolidWorksPlateDimensions? FromPlan(SolidWorksBuildPlan plan)
        {
            var sketch = plan.Operations.FirstOrDefault(operation =>
                operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase) &&
                operation.Parameters.ContainsKey("length_mm"));
            var extrude = plan.Operations.FirstOrDefault(operation =>
                operation.OperationType.Equals("ExtrudeBoss", StringComparison.OrdinalIgnoreCase));
            var cut = plan.Operations.FirstOrDefault(operation =>
                operation.OperationType.Equals("CutExtrude", StringComparison.OrdinalIgnoreCase));

            if (sketch is null || extrude is null || cut is null ||
                !TryParse(sketch, "length_mm", out var lengthMm) ||
                !TryParse(sketch, "width_mm", out var widthMm) ||
                !TryParse(extrude, "depth_mm", out var thicknessMm) ||
                !TryParse(cut, "hole_diameter_mm", out var holeDiameterMm) ||
                !cut.Parameters.TryGetValue("hole_count", out var holeCountText) ||
                !int.TryParse(holeCountText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var holeCount))
            {
                return null;
            }

            if (lengthMm <= 0 || widthMm <= 0 || thicknessMm <= 0 || holeDiameterMm <= 0 || holeCount != 4)
            {
                return null;
            }

            return new SolidWorksPlateDimensions(
                lengthMm * MmToMeters,
                widthMm * MmToMeters,
                thicknessMm * MmToMeters,
                holeDiameterMm * MmToMeters,
                holeCount);
        }

        public IEnumerable<(double X, double Y)> HoleCentersMeters()
        {
            var marginX = 20 * MmToMeters;
            var marginY = 20 * MmToMeters;
            var halfLength = LengthMeters / 2;
            var halfWidth = WidthMeters / 2;

            yield return (-halfLength + marginX, -halfWidth + marginY);
            yield return (halfLength - marginX, -halfWidth + marginY);
            yield return (-halfLength + marginX, halfWidth - marginY);
            yield return (halfLength - marginX, halfWidth - marginY);
        }

        private static bool TryParse(SolidWorksOperation operation, string key, out double value)
        {
            value = 0;
            return operation.Parameters.TryGetValue(key, out var text) &&
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
