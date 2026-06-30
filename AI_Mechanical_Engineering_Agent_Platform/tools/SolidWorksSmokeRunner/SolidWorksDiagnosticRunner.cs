using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using SolidWorksWorker.Diagnostics;
using SolidWorksWorker;

namespace SolidWorksSmokeRunner;

public sealed class SolidWorksDiagnosticRunner
{
    private const double MmToMeters = 0.001d;
    public const int MaxRepairAttempts = 1;

    public SolidWorksDiagnosticReport Run(SolidWorksDiagnosticOptions options)
    {
        var outputDirectory = ResolveOutputDirectory(options.OutputRoot);
        Directory.CreateDirectory(outputDirectory);

        var report = new SolidWorksDiagnosticReport
        {
            OutputDirectory = outputDirectory,
            SldprtPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDPRT"),
            StepPath = Path.Combine(outputDirectory, "plate_basic_4holes.STEP")
        };

        object? application = null;
        object? model = null;

        try
        {
            if (!RunOperation(report, "connection_started", () =>
                {
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new InvalidOperationException("connection_failed: SolidWorks COM diagnostics require Windows.");
                    }

                    var type = Type.GetTypeFromProgID("SldWorks.Application")
                        ?? throw new InvalidOperationException("connection_failed: SldWorks.Application COM ProgID was not found.");
                    application = Activator.CreateInstance(type)
                        ?? throw new InvalidOperationException("connection_failed: Could not create SldWorks.Application.");
                    TrySetProperty(application, "Visible", options.Visible);
                    report.SolidWorksConnected = true;
                    report.SolidWorksVersion = ReadVersion(application);
                }))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "new_part_started", () =>
                {
                    model = CreateNewPart(application!, options.TemplatePartPath);
                    if (model is null)
                    {
                        throw new InvalidOperationException("new_part_failed: SolidWorks did not return a Part document.");
                    }

                    report.ActiveDocTitle = TryInvoke(model, "GetTitle")?.ToString();
                }))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "plane_selection_started", () => SelectSketchPlane(report, model!)))
            {
                return Finish(report);
            }
            AddSucceededOperation(report, "plane_selection_success");

            if (!RunOperation(report, "base_sketch_started", () =>
                {
                    EnterSketch(model!);
                    RequireComResult(
                        Invoke(GetProperty(model!, "SketchManager"), "CreateCenterRectangle", 0d, 0d, 0d, 80d * MmToMeters, 40d * MmToMeters, 0d),
                        "sketch_failed: CreateCenterRectangle returned null.");
                    ExitSketch(model!);
                }))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "extrude_started", () =>
                {
                    RequireComResult(
                        Invoke(
                            GetProperty(model!, "FeatureManager"),
                            "FeatureExtrusion2",
                            true, false, false, 0, 0, 12d * MmToMeters, 0d, false, false, false, false,
                            0d, 0d, false, false, false, false, true, true, true, 0, 0d, false),
                        "extrude_failed: FeatureExtrusion2 returned null.");
                }))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "plane_selection_started", () => SelectSketchPlane(report, model!)))
            {
                return Finish(report);
            }
            AddSucceededOperation(report, "plane_selection_success");

            if (!RunOperation(report, "hole_sketch_started", () =>
                {
                    EnterSketch(model!);
                    var sketchManager = GetProperty(model!, "SketchManager");
                    foreach (var (x, y) in HoleCentersMeters())
                    {
                        RequireComResult(
                            Invoke(sketchManager, "CreateCircleByRadius", x, y, 0d, 5d * MmToMeters),
                            "sketch_failed: CreateCircleByRadius returned null.");
                    }

                    ExitSketch(model!);
                }))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "cut_holes_started", () =>
                {
                    RequireComResult(
                        Invoke(
                            GetProperty(model!, "FeatureManager"),
                            "FeatureCut4",
                            true, false, false, 1, 0, 24d * MmToMeters, 0d, false, false, false, false,
                            0d, 0d, false, false, false, false, false, true, true, true, true, false, 0, 0d,
                            false, false, false, false, false, false),
                        "cut_holes_failed: FeatureCut4 returned null.");
                    TryInvoke(model!, "ForceRebuild3", false);
                }))
            {
                if (!TryRepairCutHolesOnce(report, model!, options))
                {
                    return Finish(report);
                }
            }

            if (!RunOperation(report, "save_sldprt_started", () => SavePart(model!, report)))
            {
                return Finish(report);
            }

            if (!RunOperation(report, "export_step_started", () => ExportStep(application!, model!, report)))
            {
                return Finish(report);
            }

            report.FinalStatus = "Passed";
            return Finish(report);
        }
        finally
        {
            if (model is not null)
            {
                ReleaseComObject(model);
            }

            if (application is not null)
            {
                ReleaseComObject(application);
            }
        }
    }

    private bool TryRepairCutHolesOnce(SolidWorksDiagnosticReport report, object model, SolidWorksDiagnosticOptions options)
    {
        if (!string.Equals(report.FailureStage, "cut_holes_failed", StringComparison.OrdinalIgnoreCase) ||
            report.ApiRepairAttempted)
        {
            return false;
        }

        report.ApiRepairAttempted = true;
        WriteReport(report);

        try
        {
            var projectRoot = FindProjectRoot(options.OutputRoot);
            var analyzer = new SolidWorksApiFailureAnalyzer();
            var analysis = analyzer.AnalyzeFailureStage("cut_holes_failed");
            var evidence = new SolidWorksApiEvidenceCollector().CollectCutHolesEvidence(
                projectRoot,
                Path.Combine(projectRoot, "output"),
                analysis,
                "当前诊断 Runner 的原始 cut_holes_started 调用直接执行 30 参数 FeatureCut4，失败信息为参数数量不匹配。");

            report.ApiEvidenceReportPath = evidence.JsonReportPath;
            report.ApiRepairStrategy = evidence.Report.SelectedApiStrategy;
            AddSucceededOperation(report, "api_evidence_report_created");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            report.RepairFailureReason = $"api_evidence_collection_failed: {ex.Message}";
            WriteReport(report);
            return false;
        }

        var repairDiagnostics = new SolidWorksPlateBuildDiagnostics();
        var logs = new List<string>();
        var repaired = RunOperation(report, "cut_holes_repair_started", () =>
        {
            new SolidWorksPlateFeatureBuilder().CreateThroughHoles(
                model,
                HoleCentersMeters().ToArray(),
                5d * MmToMeters,
                24d * MmToMeters,
                repairDiagnostics,
                logs);
            TryInvoke(model, "ForceRebuild3", false);
        });

        CopyRepairDiagnostics(report, repairDiagnostics, logs);
        if (!repaired)
        {
            report.RepairFailureReason = string.Join("; ", repairDiagnostics.Issues.Concat(repairDiagnostics.Warnings).DefaultIfEmpty("cut_holes_repair_failed"));
            WriteReport(report);
            return false;
        }

        report.Warnings.AddRange(report.Errors.Select(error => $"initial_cut_holes_failure_before_repair: {error}"));
        report.Errors.Clear();
        report.FailureStage = null;
        report.RepairFailureReason = null;
        AddSucceededOperation(report, "cut_holes_repair_success");
        return true;
    }

    public static string MapFailureStage(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Contains("connection", StringComparison.OrdinalIgnoreCase))
        {
            return "connection_failed";
        }

        if (text.Contains("new_part", StringComparison.OrdinalIgnoreCase))
        {
            return "new_part_failed";
        }

        if (text.Contains("sketch", StringComparison.OrdinalIgnoreCase))
        {
            return "sketch_failed";
        }

        if (text.Contains("extrude", StringComparison.OrdinalIgnoreCase))
        {
            return "extrude_failed";
        }

        if (text.Contains("cut_holes", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("hole", StringComparison.OrdinalIgnoreCase))
        {
            return "cut_holes_failed";
        }

        if (text.Contains("sldprt", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "save_sldprt_failed";
        }

        if (text.Contains("step", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return "export_step_failed";
        }

        if (text.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "build_report_failed";
        }

        return "unknown_failed";
    }

    private static string ResolveOutputDirectory(string outputRoot)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("output", "solidworks", "diagnostics")
            : outputRoot);
        return Path.Combine(root, "plate_basic_4holes", $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

    private static string FindProjectRoot(string outputRoot)
    {
        var candidates = new[]
        {
            new DirectoryInfo(Directory.GetCurrentDirectory()),
            new DirectoryInfo(Path.GetFullPath(outputRoot)),
            new DirectoryInfo(AppContext.BaseDirectory)
        };

        foreach (var candidate in candidates)
        {
            var directory = candidate;
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate AI_Mechanical_Engineering_Agent_Platform.sln for API evidence output.");
    }

    private bool RunOperation(SolidWorksDiagnosticReport report, string name, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        var operation = new SolidWorksDiagnosticOperation
        {
            Name = name,
            StartedAt = DateTimeOffset.UtcNow
        };

        try
        {
            action();
            operation.Success = true;
            return true;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException or MissingMethodException)
        {
            var message = ex.GetBaseException().Message;
            var stage = MapFailureStage($"{name}: {message}");
            operation.Error = $"{stage}: {message}";
            report.FailureStage ??= stage;
            report.Errors.Add(operation.Error);
            return false;
        }
        finally
        {
            stopwatch.Stop();
            operation.CompletedAt = DateTimeOffset.UtcNow;
            operation.ElapsedMs = stopwatch.ElapsedMilliseconds;
            report.Operations.Add(operation);
            WriteReport(report);
        }
    }

    private static void AddSucceededOperation(SolidWorksDiagnosticReport report, string name)
    {
        var now = DateTimeOffset.UtcNow;
        report.Operations.Add(new SolidWorksDiagnosticOperation
        {
            Name = name,
            StartedAt = now,
            CompletedAt = now,
            Success = true,
            ElapsedMs = 0
        });
        WriteReport(report);
    }

    private static void CopyRepairDiagnostics(
        SolidWorksDiagnosticReport report,
        SolidWorksPlateBuildDiagnostics diagnostics,
        IReadOnlyList<string> logs)
    {
        foreach (var warning in diagnostics.Warnings)
        {
            if (!report.Warnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
            {
                report.Warnings.Add(warning);
            }
        }

        foreach (var issue in diagnostics.Issues)
        {
            if (!report.Errors.Contains(issue, StringComparer.OrdinalIgnoreCase))
            {
                report.Errors.Add(issue);
            }
        }

        foreach (var log in logs)
        {
            report.Warnings.Add($"repair_log: {log}");
        }

        report.PlaneSelectionAttempted = report.PlaneSelectionAttempted || diagnostics.PlaneSelectionAttempted;
        report.PlaneSelectionSuccess = report.PlaneSelectionSuccess || diagnostics.PlaneSelectionSuccess;
        report.SelectedPlaneName ??= diagnostics.SelectedPlaneName;
        report.SelectedPlaneStrategy ??= diagnostics.SelectedPlaneStrategy;

        foreach (var error in diagnostics.PlaneSelectionErrors)
        {
            if (!report.PlaneSelectionErrors.Contains(error, StringComparer.OrdinalIgnoreCase))
            {
                report.PlaneSelectionErrors.Add(error);
            }
        }

        foreach (var plane in diagnostics.AvailableReferencePlanes)
        {
            if (!report.AvailableReferencePlanes.Any(existing =>
                    string.Equals(existing.Name, plane.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.TypeName, plane.TypeName, StringComparison.OrdinalIgnoreCase)))
            {
                report.AvailableReferencePlanes.Add(plane);
            }
        }
    }

    private static SolidWorksDiagnosticReport Finish(SolidWorksDiagnosticReport report)
    {
        report.CompletedAt = DateTimeOffset.UtcNow;
        if (report.FinalStatus != "Passed")
        {
            report.FinalStatus = "Failed";
        }

        RefreshFileState(report);
        WriteReport(report);
        return report;
    }

    private static object? CreateNewPart(object application, string? templatePartPath)
    {
        if (!string.IsNullOrWhiteSpace(templatePartPath))
        {
            if (!File.Exists(templatePartPath))
            {
                throw new InvalidOperationException($"new_part_failed: template file does not exist: {templatePartPath}");
            }

            return TryInvoke(application, "NewDocument", Path.GetFullPath(templatePartPath), 0, 0d, 0d) ??
                   TryGetProperty(application, "ActiveDoc");
        }

        return TryInvoke(application, "NewPart") ?? TryGetProperty(application, "ActiveDoc");
    }

    private static void SelectSketchPlane(SolidWorksDiagnosticReport report, object model)
    {
        report.PlaneSelectionAttempted = true;
        var result = new SolidWorksPlaneSelector().SelectStandardPlane(model);
        report.PlaneSelectionSuccess = result.Success;
        report.SelectedPlaneName = result.SelectedPlaneName;
        report.SelectedPlaneStrategy = result.SelectedPlaneStrategy;
        report.PlaneSelectionErrors.Clear();
        report.PlaneSelectionErrors.AddRange(result.Errors);
        report.AvailableReferencePlanes.Clear();
        report.AvailableReferencePlanes.AddRange(result.AvailableReferencePlanes);

        if (!result.Success)
        {
            var error = result.Errors.LastOrDefault() ?? "select_plane_failed_all_candidates";
            throw new InvalidOperationException($"sketch_failed: {error}");
        }
    }

    private static void EnterSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private static void ExitSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private static void SavePart(object model, SolidWorksDiagnosticReport report)
    {
        report.SaveSldprtAttempted = true;
        report.SldprtPath = Path.GetFullPath(report.SldprtPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(report.SldprtPath)!);

        var saved = TryExtensionSaveAs(model, report.SldprtPath, report.SaveSldprtErrors, report.SaveSldprtWarnings) ||
                    TryInvokeBool(model, "SaveAs3", report.SldprtPath, 0, 1) ||
                    TryInvokeBool(model, "SaveAs", report.SldprtPath);
        if (!saved)
        {
            report.SaveSldprtErrors.Add("save_sldprt_failed: SaveAs returned false.");
        }

        RefreshFileState(report);
        if (!report.SldprtExists || report.SldprtSizeBytes <= 0)
        {
            report.SaveSldprtSuccess = false;
            throw new IOException($"save_sldprt_failed: {report.SldprtPath}");
        }

        report.SaveSldprtSuccess = true;
    }

    private static void ExportStep(object application, object model, SolidWorksDiagnosticReport report)
    {
        report.ExportStepAttempted = true;
        report.StepPath = Path.GetFullPath(report.StepPath!);
        Directory.CreateDirectory(Path.GetDirectoryName(report.StepPath)!);

        report.ActiveDocTitleBeforeStepExport = GetActiveDocumentTitle(application) ?? TryInvoke(model, "GetTitle")?.ToString();
        ActivateDocument(application, model, report);
        var activeDocument = TryGetProperty(application, "ActiveDoc") ?? model;
        TryInvoke(activeDocument, "ClearSelection2", true);

        var exported = TryExtensionSaveAs(activeDocument, report.StepPath, report.ExportStepErrors, report.ExportStepWarnings) ||
                       TryInvokeBool(activeDocument, "SaveAs3", report.StepPath, 0, 1) ||
                       TryInvokeBool(activeDocument, "SaveAs", report.StepPath);
        if (!exported)
        {
            report.ExportStepErrors.Add("export_step_failed: SaveAs returned false.");
        }

        RefreshFileState(report);
        if (!report.StepExists || report.StepSizeBytes <= 0)
        {
            report.ExportStepSuccess = false;
            throw new IOException($"export_step_failed: {report.StepPath}");
        }

        report.ExportStepSuccess = true;
    }

    private static void ActivateDocument(object application, object model, SolidWorksDiagnosticReport report)
    {
        var title = TryInvoke(model, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            TryInvoke(application, "ActivateDoc3", title, true, 0, 0);
        }

        report.ActiveDocTitleAfterActivate = GetActiveDocumentTitle(application) ?? title;
    }

    private static string? GetActiveDocumentTitle(object application)
    {
        var activeDocument = TryGetProperty(application, "ActiveDoc");
        return activeDocument is null ? null : TryInvoke(activeDocument, "GetTitle")?.ToString();
    }

    private static bool TryExtensionSaveAs(
        object model,
        string path,
        List<string> errors,
        List<string> warnings)
    {
        try
        {
            var extension = GetProperty(model, "Extension");
            var args = new object?[] { path, 0, 1, null, 0, 0 };
            var result = Invoke(extension, "SaveAs", args);
            if (args[4] is not null && Convert.ToInt32(args[4], CultureInfo.InvariantCulture) != 0)
            {
                errors.Add($"save_as_errors: {args[4]}");
            }

            if (args[5] is not null && Convert.ToInt32(args[5], CultureInfo.InvariantCulture) != 0)
            {
                warnings.Add($"save_as_warnings: {args[5]}");
            }

            return result is bool value && value;
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
        {
            errors.Add($"save_as_exception: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static void RefreshFileState(SolidWorksDiagnosticReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.SldprtPath))
        {
            report.SldprtPath = Path.GetFullPath(report.SldprtPath);
            report.SldprtExists = File.Exists(report.SldprtPath);
            report.SldprtSizeBytes = report.SldprtExists ? new FileInfo(report.SldprtPath).Length : 0;
        }

        if (!string.IsNullOrWhiteSpace(report.StepPath))
        {
            report.StepPath = Path.GetFullPath(report.StepPath);
            report.StepExists = File.Exists(report.StepPath);
            report.StepSizeBytes = report.StepExists ? new FileInfo(report.StepPath).Length : 0;
        }
    }

    private static string? ReadVersion(object application)
    {
        var value = TryInvoke(application, "RevisionNumber") ?? TryGetProperty(application, "RevisionNumber");
        return value?.ToString();
    }

    private static IEnumerable<(double X, double Y)> HoleCentersMeters()
    {
        const double halfLength = 80d * MmToMeters;
        const double halfWidth = 40d * MmToMeters;
        const double marginX = 20d * MmToMeters;
        const double marginY = 20d * MmToMeters;

        yield return (-halfLength + marginX, -halfWidth + marginY);
        yield return (halfLength - marginX, -halfWidth + marginY);
        yield return (-halfLength + marginX, halfWidth - marginY);
        yield return (halfLength - marginX, halfWidth - marginY);
    }

    private static void RequireComResult(object? value, string message)
    {
        if (value is null)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void WriteReport(SolidWorksDiagnosticReport report)
    {
        try
        {
            Directory.CreateDirectory(report.OutputDirectory);
            var path = Path.Combine(report.OutputDirectory, "diagnostic_report.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report, JsonOptions()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            report.FailureStage ??= "build_report_failed";
            if (!report.Errors.Any(error => error.Contains("diagnostic_report_write_failed", StringComparison.OrdinalIgnoreCase)))
            {
                report.Errors.Add($"diagnostic_report_write_failed: {ex.Message}");
            }
        }
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };

    private static object GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            Array.Empty<object>())
        ?? throw new InvalidOperationException($"solidworks_property_missing: {name}.");

    private static object? TryGetProperty(object target, string name)
    {
        try
        {
            return GetProperty(target, name);
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void TrySetProperty(object target, string name, object value)
    {
        try
        {
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty,
                binder: null,
                target,
                new[] { value });
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
        {
            // Visibility is useful for diagnosis but should not block the runner.
        }
    }

    private static object? Invoke(object target, string name, params object?[] args) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);

    private static object? TryInvoke(object target, string name, params object?[] args)
    {
        try
        {
            return Invoke(target, name, args);
        }
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException)
        {
            return null;
        }
    }

    private static bool TryInvokeBool(object target, string name, params object?[] args)
    {
        var value = TryInvoke(target, name, args);
        return value is bool boolean && boolean;
    }

    private static void ReleaseComObject(object value)
    {
        if (OperatingSystem.IsWindows() && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
