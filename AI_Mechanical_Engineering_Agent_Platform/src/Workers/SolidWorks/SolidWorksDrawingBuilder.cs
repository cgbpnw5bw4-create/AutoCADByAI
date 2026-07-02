using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksDrawingBuilder
{
    Task<SolidWorksDrawingBuildResult> CreateBasicViewsDrawingAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken);
}

public sealed record SolidWorksDrawingBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    bool RealCadExecuted)
{
    public static SolidWorksDrawingBuildResult Completed(
        IReadOnlyList<SolidWorksArtifact> generatedArtifacts,
        IReadOnlyList<string> logs) =>
        new("Completed", generatedArtifacts, logs, Array.Empty<string>(), RealCadExecuted: true);

    public static SolidWorksDrawingBuildResult Failed(
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        IReadOnlyList<SolidWorksArtifact>? generatedArtifacts = null) =>
        new("Failed", generatedArtifacts ?? Array.Empty<SolidWorksArtifact>(), logs, issues, RealCadExecuted: false);
}

public static class SolidWorksDrawingBuildOutput
{
    public const string ExecutionMode = "RealDrawingBasicViews";

    public static string ResolveOutputDirectory(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var fullRoot = Path.GetFullPath(requestedRoot);
        var normalized = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryName = Path.GetFileName(normalized);

        if (IsUnderDrawingRoot(normalized) &&
            !directoryName.Equals("plate_basic_4holes_drawing", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var target = directoryName.Equals("plate_basic_4holes_drawing", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(normalized, TimestampSegment())
            : Path.Combine(normalized, "plate_basic_4holes_drawing", TimestampSegment());

        if (!Directory.Exists(target) || Directory.GetFileSystemEntries(target).Length == 0)
        {
            return target;
        }

        return Path.Combine(
            Path.GetDirectoryName(target) ?? fullRoot,
            $"plate_basic_4holes_drawing_{TimestampSegment()}");
    }

    public static SolidWorksArtifact Artifact(
        string artifactId,
        string artifactType,
        string filePath,
        string expectedExtension,
        string description)
    {
        var fullPath = Path.GetFullPath(filePath);
        var exists = File.Exists(fullPath);
        return new SolidWorksArtifact(
            artifactId,
            artifactType,
            fullPath,
            expectedExtension,
            exists,
            exists ? new FileInfo(fullPath).Length : 0,
            description);
    }

    private static bool IsUnderDrawingRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null)
        {
            if (directory.Name.Equals("plate_basic_4holes_drawing", StringComparison.OrdinalIgnoreCase) &&
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

public static class SolidWorksDrawingReportWriter
{
    public static async Task WriteAsync(
        string reportPath,
        SolidWorksDrawingReport report,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            reportPath,
            JsonSerializer.Serialize(report, JsonOptions()),
            cancellationToken);
    }

    public static void Write(string reportPath, SolidWorksDrawingReport report)
    {
        var directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions()));
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };
}

public sealed class LateBoundSolidWorksDrawingBuilder : ISolidWorksDrawingBuilder
{
    public Task<SolidWorksDrawingBuildResult> CreateBasicViewsDrawingAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => CreateCore(application, request, options, solidWorksVersion, cancellationToken),
            cancellationToken);
    }

    private static SolidWorksDrawingBuildResult CreateCore(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var issues = new List<string>();
        object? partDocument = null;
        object? drawingDocument = null;
        var outputDirectory = SolidWorksDrawingBuildOutput.ResolveOutputDirectory(request, options);
        var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDDRW");
        var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes.pdf");
        var reportPath = Path.Combine(outputDirectory, "drawing_report.json");
        var report = new SolidWorksDrawingReport
        {
            SourcePartPath = request.SourcePartPath,
            OutputDirectory = outputDirectory,
            SolidWorksConnected = true,
            SolidWorksVersion = solidWorksVersion,
            SlddrwPath = drawingPath,
            PdfPath = pdfPath
        };

        try
        {
            Directory.CreateDirectory(outputDirectory);
            report.Operations.Add("drawing_request_received");
            report.Operations.Add("safety_flags_checked");

            var sourcePartPath = Path.GetFullPath(request.SourcePartPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(request.SourcePartPath) || !File.Exists(sourcePartPath))
            {
                return FailWithReport(
                    request,
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_part_missing",
                    $"source_part_missing: {request.SourcePartPath ?? "<null>"}");
            }

            report.SourcePartPath = sourcePartPath;
            report.Operations.Add("source_part_checked");

            var drawingTemplatePath = ResolveDrawingTemplatePath(request, options);
            if (string.IsNullOrWhiteSpace(drawingTemplatePath) || !File.Exists(drawingTemplatePath))
            {
                return FailWithReport(
                    request,
                    report,
                    reportPath,
                    logs,
                    issues,
                    "drawing_template_missing",
                    "drawing_template_missing: SW_TEMPLATE_DRAWING_PATH or request.DrawingTemplatePath must point to an existing .drwdot template.");
            }

            report.Operations.Add("drawing_template_checked");

            report.Operations.Add("source_part_open_started");
            partDocument = OpenPart(application, sourcePartPath);
            if (partDocument is null)
            {
                return FailWithReport(
                    request,
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_part_open_failed",
                    $"source_part_open_failed: {sourcePartPath}");
            }

            report.Operations.Add("source_part_open_success");
            report.Operations.Add("source_part_activate_started");
            if (!ActivateDocument(application, partDocument))
            {
                return FailWithReport(
                    request,
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_part_activate_failed",
                    $"source_part_activate_failed: {TryInvoke(partDocument, "GetTitle")}");
            }

            report.Operations.Add("source_part_activate_success");

            report.Operations.Add("drawing_document_create_started");
            drawingDocument = TryInvoke(application, "NewDocument", drawingTemplatePath, 0, 0d, 0d) ??
                              TryGetProperty(application, "ActiveDoc");
            if (drawingDocument is null)
            {
                return FailWithReport(
                    request,
                    report,
                    reportPath,
                    logs,
                    issues,
                    "drawing_document_create_failed",
                    $"drawing_document_create_failed: {drawingTemplatePath}");
            }

            report.DrawingCreated = true;
            report.Operations.Add("drawing_document_create_success");

            CreateView(drawingDocument, sourcePartPath, "*Front", 0.10d, 0.20d, "front_view_create_failed", report, logs);
            CreateView(drawingDocument, sourcePartPath, "*Top", 0.10d, 0.10d, "top_view_create_failed", report, logs);
            CreateView(drawingDocument, sourcePartPath, "*Right", 0.22d, 0.20d, "right_view_create_failed", report, logs);
            CreateView(drawingDocument, sourcePartPath, "*Isometric", 0.24d, 0.10d, "isometric_view_create_failed", report, logs);

            SaveDrawing(drawingDocument, drawingPath, report, logs);
            ExportPdf(application, drawingDocument, pdfPath, report, logs);

            report.FinalStatus = "Passed";
            report.CompletedAt = DateTimeOffset.UtcNow;
            RefreshFileState(report);
            report.Operations.Add("drawing_report_write_started");
            SolidWorksDrawingReportWriter.Write(reportPath, report);
            report.Operations.Add("drawing_report_written");
            SolidWorksDrawingReportWriter.Write(reportPath, report);

            return SolidWorksDrawingBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing", "Drawing", drawingPath, ".SLDDRW", "真实 SolidWorks 工程图文件。"),
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing-pdf", "Pdf", pdfPath, ".pdf", "真实工程图 PDF 导出文件。"),
                    SolidWorksDrawingBuildOutput.Artifact("real-drawing-report", "DrawingReport", reportPath, ".json", "真实工程图报告。")
                },
                logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray());
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var stage = report.FailureStage ?? MapDrawingFailureStage(ex.GetBaseException().Message);
            return FailWithReport(request, report, reportPath, logs, issues, stage, ex.GetBaseException().Message);
        }
        finally
        {
            if (drawingDocument is not null)
            {
                ReleaseComObject(drawingDocument);
            }

            if (partDocument is not null)
            {
                ReleaseComObject(partDocument);
            }
        }
    }

    private static void CreateView(
        object drawingDocument,
        string sourcePartPath,
        string viewName,
        double x,
        double y,
        string failureStage,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        var operationName = failureStage.Replace("_failed", "_started", StringComparison.OrdinalIgnoreCase);
        report.Operations.Add(operationName);
        var view = TryInvoke(drawingDocument, "CreateDrawViewFromModelView3", sourcePartPath, viewName, x, y, 0d);
        if (view is null)
        {
            throw new InvalidOperationException($"{failureStage}: CreateDrawViewFromModelView3 returned null for {viewName}.");
        }

        report.ViewsCreated.Add(viewName.TrimStart('*'));
        report.Operations.Add(failureStage.Replace("_failed", "_success", StringComparison.OrdinalIgnoreCase));
        logs.Add($"Created drawing view {viewName}.");
    }

    private static void SaveDrawing(
        object drawingDocument,
        string drawingPath,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        report.Operations.Add("slddrw_save_started");
        Directory.CreateDirectory(Path.GetDirectoryName(drawingPath)!);
        var errors = new List<string>();
        var warnings = new List<string>();
        var saved = TryExtensionSaveAs(drawingDocument, drawingPath, null, errors, warnings) ||
                    TryInvokeBool(drawingDocument, "SaveAs3", drawingPath, 0, 1) ||
                    TryInvokeBool(drawingDocument, "SaveAs", drawingPath);

        if (!saved)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("slddrw_save_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
        }

        RefreshFileState(report);
        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            throw new IOException($"slddrw_save_failed: {drawingPath}");
        }

        report.Operations.Add("slddrw_save_success");
        logs.Add($"Saved SolidWorks drawing: {drawingPath}.");
    }

    private static void ExportPdf(
        object application,
        object drawingDocument,
        string pdfPath,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        report.Operations.Add("pdf_export_started");
        Directory.CreateDirectory(Path.GetDirectoryName(pdfPath)!);
        TryInvoke(drawingDocument, "ClearSelection2", true);
        ActivateDocument(application, drawingDocument);

        var errors = new List<string>();
        var warnings = new List<string>();
        var pdfData = TryGetPdfExportData(application, warnings);
        var exported = TryExtensionSaveAs(drawingDocument, pdfPath, pdfData, errors, warnings) ||
                       TryExtensionSaveAs(drawingDocument, pdfPath, null, errors, warnings) ||
                       TryInvokeBool(drawingDocument, "SaveAs3", pdfPath, 0, 1) ||
                       TryInvokeBool(drawingDocument, "SaveAs", pdfPath);

        if (!exported)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("pdf_export_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
        }

        RefreshFileState(report);
        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            throw new IOException($"pdf_export_failed: {pdfPath}");
        }

        report.Operations.Add("pdf_export_success");
        logs.Add($"Exported drawing PDF: {pdfPath}.");
    }

    private static object? OpenPart(object application, string sourcePartPath)
    {
        const int swDocPart = 1;
        const int swOpenDocOptionsSilent = 1;
        return TryInvoke(application, "OpenDoc6", sourcePartPath, swDocPart, swOpenDocOptionsSilent, string.Empty, 0, 0) ??
               TryInvoke(application, "OpenDoc", sourcePartPath, swDocPart) ??
               TryGetProperty(application, "ActiveDoc");
    }

    private static bool ActivateDocument(object application, object document)
    {
        var title = TryInvoke(document, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            TryInvoke(application, "ActivateDoc3", title, true, 0, 0);
        }

        var activeDocument = TryGetProperty(application, "ActiveDoc");
        if (activeDocument is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return true;
        }

        var activeTitle = TryInvoke(activeDocument, "GetTitle")?.ToString();
        return string.Equals(activeTitle, title, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDrawingTemplatePath(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options)
    {
        var candidates = new List<string?>();
        candidates.Add(request.DrawingTemplatePath);
        candidates.Add(options.DrawingTemplatePath);
        candidates.Add(Environment.GetEnvironmentVariable("SW_TEMPLATE_DRAWING_PATH"));

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrWhiteSpace(programData))
        {
            var solidWorksRoot = Path.Combine(programData, "SolidWorks");
            if (Directory.Exists(solidWorksRoot))
            {
                candidates.AddRange(Directory.EnumerateFiles(solidWorksRoot, "*.drwdot", SearchOption.AllDirectories)
                    .OrderBy(path => path.Contains("gb", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase));
            }
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .FirstOrDefault(File.Exists);
    }

    private static object? TryGetPdfExportData(object application, List<string> warnings)
    {
        try
        {
            const int swExportPdfData = 1;
            var pdfData = TryInvoke(application, "GetExportFileData", swExportPdfData);
            if (pdfData is null)
            {
                warnings.Add("pdf_export_data_unavailable: GetExportFileData returned null; SaveAs without export data will be attempted.");
                return null;
            }

            TryInvoke(pdfData, "SetSheets", 1, null);
            return pdfData;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException)
        {
            warnings.Add($"pdf_export_data_unavailable: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private static SolidWorksDrawingBuildResult FailWithReport(
        SolidWorksWorkerRequest request,
        SolidWorksDrawingReport report,
        string reportPath,
        IReadOnlyList<string> logs,
        List<string> issues,
        string failureStage,
        string issue)
    {
        report.FailureStage ??= failureStage;
        if (!report.Errors.Contains(issue, StringComparer.OrdinalIgnoreCase))
        {
            report.Errors.Add(issue);
        }

        if (!issues.Contains(issue, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(issue);
        }

        report.FinalStatus = "Failed";
        report.CompletedAt = DateTimeOffset.UtcNow;
        RefreshFileState(report);
        report.Operations.Add("drawing_report_write_started");

        SolidWorksArtifact[] generatedArtifacts;
        try
        {
            SolidWorksDrawingReportWriter.Write(reportPath, report);
            report.Operations.Add("drawing_report_written");
            SolidWorksDrawingReportWriter.Write(reportPath, report);
            generatedArtifacts = new[]
            {
                SolidWorksDrawingBuildOutput.Artifact("real-drawing-report", "DrawingReport", reportPath, ".json", "真实工程图失败报告。")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var reportIssue = $"drawing_report_write_failed: {ex.Message}";
            report.FailureStage = "drawing_report_write_failed";
            issues.Add(reportIssue);
            generatedArtifacts = Array.Empty<SolidWorksArtifact>();
        }

        return SolidWorksDrawingBuildResult.Failed(
            logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray(),
            issues,
            generatedArtifacts);
    }

    private static void RefreshFileState(SolidWorksDrawingReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.SlddrwPath))
        {
            report.SlddrwPath = Path.GetFullPath(report.SlddrwPath);
            report.SlddrwExists = File.Exists(report.SlddrwPath);
            report.SlddrwSizeBytes = report.SlddrwExists ? new FileInfo(report.SlddrwPath).Length : 0;
        }

        if (!string.IsNullOrWhiteSpace(report.PdfPath))
        {
            report.PdfPath = Path.GetFullPath(report.PdfPath);
            report.PdfExists = File.Exists(report.PdfPath);
            report.PdfSizeBytes = report.PdfExists ? new FileInfo(report.PdfPath).Length : 0;
        }
    }

    public static string MapDrawingFailureStage(string text)
    {
        if (text.Contains("source_part_missing", StringComparison.OrdinalIgnoreCase))
        {
            return "source_part_missing";
        }

        if (text.Contains("drawing_template", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_template_missing";
        }

        if (text.Contains("source_part_open", StringComparison.OrdinalIgnoreCase))
        {
            return "source_part_open_failed";
        }

        if (text.Contains("source_part_activate", StringComparison.OrdinalIgnoreCase))
        {
            return "source_part_activate_failed";
        }

        if (text.Contains("front_view", StringComparison.OrdinalIgnoreCase))
        {
            return "front_view_create_failed";
        }

        if (text.Contains("top_view", StringComparison.OrdinalIgnoreCase))
        {
            return "top_view_create_failed";
        }

        if (text.Contains("right_view", StringComparison.OrdinalIgnoreCase))
        {
            return "right_view_create_failed";
        }

        if (text.Contains("isometric_view", StringComparison.OrdinalIgnoreCase))
        {
            return "isometric_view_create_failed";
        }

        if (text.Contains("slddrw", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "slddrw_save_failed";
        }

        if (text.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return "pdf_export_failed";
        }

        if (text.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_report_write_failed";
        }

        if (text.Contains("drawing_document", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NewDocument", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_document_create_failed";
        }

        return "drawing_api_evidence_insufficient";
    }

    private static bool TryExtensionSaveAs(
        object model,
        string path,
        object? exportData,
        List<string> errors,
        List<string> warnings)
    {
        try
        {
            var extension = GetProperty(model, "Extension");
            var args = new object?[] { path, 0, 1, exportData, 0, 0 };
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
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
