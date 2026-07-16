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
        SolidWorksFakeSuccessGuard.NormalizeDrawingReport(report);
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
        SolidWorksFakeSuccessGuard.NormalizeDrawingReport(report);
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
    private readonly ISolidWorksComFacade _comFacade;
    private readonly ISolidWorksFileVerifier _fileVerifier;

    public LateBoundSolidWorksDrawingBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null)
    {
        _comFacade = comFacade ?? new LateBoundSolidWorksComFacade();
        _fileVerifier = fileVerifier ?? new SolidWorksFileVerifier();
    }

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

    private SolidWorksDrawingBuildResult CreateCore(
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

            var modelViews = ResolveModelViewNames(partDocument, report, logs);
            CreateRequiredViews(drawingDocument, sourcePartPath, modelViews, report, logs);

            SaveDrawing(drawingDocument, drawingPath, report, logs);
            ExportPdf(application, drawingDocument, pdfPath, report, logs);
            SolidWorksFakeSuccessGuard.RequireDrawingCanPass(report);

            report.FinalStatus = "Passed";
            report.CompletedAt = DateTimeOffset.UtcNow;
            RefreshFileState(report);
            report.Operations.Add("drawing_report_write_started");
            SolidWorksDrawingReportWriter.Write(reportPath, report);
            report.Operations.Add("drawing_report_written");

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

    private void CreateRequiredViews(
        object drawingDocument,
        string sourcePartPath,
        SolidWorksModelViewNames modelViews,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        if (TryCreateView(drawingDocument, sourcePartPath, modelViews.Front, 0.10d, 0.20d, "front_view_create_failed", report, logs))
        {
            CreateView(drawingDocument, sourcePartPath, modelViews.Top, 0.10d, 0.10d, "top_view_create_failed", report, logs);
            CreateView(drawingDocument, sourcePartPath, modelViews.Right, 0.22d, 0.20d, "right_view_create_failed", report, logs);
            CreateView(drawingDocument, sourcePartPath, modelViews.Isometric, 0.24d, 0.10d, "isometric_view_create_failed", report, logs);
            return;
        }

        report.Warnings.Add("front_view_named_creation_failed: falling back to Create3rdAngleViews2 for the documented standard orthographic view API.");
        report.Operations.Add("third_angle_views_create_started");
        if (!TryInvokeBool(drawingDocument, "Create3rdAngleViews2", sourcePartPath))
        {
            throw new InvalidOperationException("front_view_create_failed: CreateDrawViewFromModelView3 returned null for *Front and Create3rdAngleViews2 returned false.");
        }

        report.ViewsCreated.AddRange(new[] { "Front", "Top", "Right" });
        report.Operations.Add("third_angle_views_create_success");
        logs.Add("Created Front, Top and Right views using Create3rdAngleViews2 fallback.");
        CreateView(drawingDocument, sourcePartPath, modelViews.Isometric, 0.24d, 0.10d, "isometric_view_create_failed", report, logs);
    }

    private SolidWorksModelViewNames ResolveModelViewNames(
        object partDocument,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        var available = new List<string>();
        var rawViewNames = TryInvoke(partDocument, "GetModelViewNames");
        if (rawViewNames is System.Collections.IEnumerable values)
        {
            foreach (var value in values)
            {
                if (value is string viewName && !string.IsNullOrWhiteSpace(viewName))
                {
                    available.Add(viewName);
                }
            }
        }

        if (available.Count == 0)
        {
            report.Warnings.Add("model_view_names_unavailable: GetModelViewNames did not return usable names; invariant English standard names will be attempted.");
            return SolidWorksModelViewNames.InvariantEnglish;
        }

        report.Operations.Add("model_view_names_read");
        logs.Add($"Read {available.Count} model view names from SolidWorks.");

        return new SolidWorksModelViewNames(
            ResolveAvailableModelViewName(available, "*Front", "*前视"),
            ResolveAvailableModelViewName(available, "*Top", "*上视"),
            ResolveAvailableModelViewName(available, "*Right", "*右视"),
            ResolveAvailableModelViewName(available, "*Isometric", "*等轴测"));
    }

    private static string ResolveAvailableModelViewName(IReadOnlyList<string> available, params string[] candidates) =>
        candidates.FirstOrDefault(candidate => available.Contains(candidate, StringComparer.OrdinalIgnoreCase)) ?? candidates[0];

    private static string CanonicalizeViewName(string viewName) => viewName.TrimStart('*') switch
    {
        "前视" or "Front" => "Front",
        "上视" or "Top" => "Top",
        "右视" or "Right" => "Right",
        "等轴测" or "Isometric" => "Isometric",
        var name => name
    };

    private bool TryCreateView(
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
            return false;
        }

        // The report uses canonical drawing semantics; the COM invocation above
        // still used the exact localized name returned by this SolidWorks instance.
        report.ViewsCreated.Add(CanonicalizeViewName(viewName));
        report.Operations.Add(failureStage.Replace("_failed", "_success", StringComparison.OrdinalIgnoreCase));
        logs.Add($"Created drawing view {viewName}.");
        return true;
    }

    private void CreateView(
        object drawingDocument,
        string sourcePartPath,
        string viewName,
        double x,
        double y,
        string failureStage,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        if (!TryCreateView(drawingDocument, sourcePartPath, viewName, x, y, failureStage, report, logs))
        {
            throw new InvalidOperationException($"{failureStage}: CreateDrawViewFromModelView3 returned null for {viewName}.");
        }
    }

    private void SaveDrawing(
        object drawingDocument,
        string drawingPath,
        SolidWorksDrawingReport report,
        List<string> logs)
    {
        report.Operations.Add("slddrw_save_started");
        Directory.CreateDirectory(Path.GetDirectoryName(drawingPath)!);
        var errors = new List<string>();
        var warnings = new List<string>();
        var saved = _comFacade.TryExtensionSaveAs(drawingDocument, drawingPath, null, errors, warnings) ||
                    TryInvokeBool(drawingDocument, "SaveAs3", drawingPath, 0, 1) ||
                    TryInvokeBool(drawingDocument, "SaveAs", drawingPath);

        if (!saved)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("slddrw_save_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"slddrw_save_failed: {drawingPath}");
        }

        RefreshFileState(report);
        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            throw new IOException($"slddrw_save_failed: {drawingPath}");
        }

        report.Operations.Add("slddrw_save_success");
        logs.Add($"Saved SolidWorks drawing: {drawingPath}.");
    }

    private void ExportPdf(
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
        var exported = _comFacade.TryExtensionSaveAs(drawingDocument, pdfPath, pdfData, errors, warnings) ||
                       _comFacade.TryExtensionSaveAs(drawingDocument, pdfPath, null, errors, warnings) ||
                       TryInvokeBool(drawingDocument, "SaveAs3", pdfPath, 0, 1) ||
                       TryInvokeBool(drawingDocument, "SaveAs", pdfPath);

        if (!exported)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("pdf_export_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"pdf_export_failed: {pdfPath}");
        }

        RefreshFileState(report);
        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            throw new IOException($"pdf_export_failed: {pdfPath}");
        }

        report.Operations.Add("pdf_export_success");
        logs.Add($"Exported drawing PDF: {pdfPath}.");
    }

    private object? OpenPart(object application, string sourcePartPath)
    {
        const int swDocPart = 1;
        const int swOpenDocOptionsSilent = 1;
        return TryInvoke(application, "OpenDoc6", sourcePartPath, swDocPart, swOpenDocOptionsSilent, string.Empty, 0, 0) ??
               TryInvoke(application, "OpenDoc", sourcePartPath, swDocPart) ??
               TryGetProperty(application, "ActiveDoc");
    }

    private bool ActivateDocument(object application, object document)
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

    private object? TryGetPdfExportData(object application, List<string> warnings)
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

    private SolidWorksDrawingBuildResult FailWithReport(
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

    private void RefreshFileState(SolidWorksDrawingReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.SlddrwPath))
        {
            var state = _fileVerifier.GetState(report.SlddrwPath);
            report.SlddrwPath = state.Path;
            report.SlddrwExists = state.Exists;
            report.SlddrwSizeBytes = state.SizeBytes;
        }

        if (!string.IsNullOrWhiteSpace(report.PdfPath))
        {
            var state = _fileVerifier.GetState(report.PdfPath);
            report.PdfPath = state.Path;
            report.PdfExists = state.Exists;
            report.PdfSizeBytes = state.SizeBytes;
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

    private object? TryGetProperty(object? target, string name) => _comFacade.TryGetProperty(target, name);

    private object? TryInvoke(object? target, string name, params object?[] args) => _comFacade.TryInvoke(target, name, args);

    private bool TryInvokeBool(object? target, string name, params object?[] args) => _comFacade.TryInvokeBool(target, name, args);

    private void ReleaseComObject(object value) => _comFacade.ReleaseComObject(value);

    private sealed record SolidWorksModelViewNames(string Front, string Top, string Right, string Isometric)
    {
        public static SolidWorksModelViewNames InvariantEnglish { get; } = new("*Front", "*Top", "*Right", "*Isometric");
    }
}
