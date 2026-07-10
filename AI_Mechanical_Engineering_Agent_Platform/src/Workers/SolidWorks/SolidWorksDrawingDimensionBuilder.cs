using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksDrawingDimensionBuilder
{
    Task<SolidWorksDrawingDimensionBuildResult> CreateDimensionedDrawingAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken);
}

public sealed record SolidWorksDrawingDimensionBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    bool RealCadExecuted)
{
    public static SolidWorksDrawingDimensionBuildResult Completed(
        IReadOnlyList<SolidWorksArtifact> generatedArtifacts,
        IReadOnlyList<string> logs) =>
        new("Completed", generatedArtifacts, logs, Array.Empty<string>(), RealCadExecuted: true);

    public static SolidWorksDrawingDimensionBuildResult Failed(
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        IReadOnlyList<SolidWorksArtifact>? generatedArtifacts = null) =>
        new("Failed", generatedArtifacts ?? Array.Empty<SolidWorksArtifact>(), logs, issues, RealCadExecuted: false);
}

public static class SolidWorksDrawingDimensionBuildOutput
{
    public const string ExecutionMode = "RealDrawingDimensions";

    public static string ResolveOutputDirectory(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var fullRoot = Path.GetFullPath(requestedRoot);
        var normalized = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryName = Path.GetFileName(normalized);

        if (IsUnderDimensionRoot(normalized) &&
            !directoryName.Equals("plate_basic_4holes_drawing_dimensions", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var target = directoryName.Equals("plate_basic_4holes_drawing_dimensions", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(normalized, TimestampSegment())
            : Path.Combine(normalized, "plate_basic_4holes_drawing_dimensions", TimestampSegment());

        if (!Directory.Exists(target) || Directory.GetFileSystemEntries(target).Length == 0)
        {
            return target;
        }

        return Path.Combine(
            Path.GetDirectoryName(target) ?? fullRoot,
            $"plate_basic_4holes_drawing_dimensions_{TimestampSegment()}");
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

    private static bool IsUnderDimensionRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null)
        {
            if (directory.Name.Equals("plate_basic_4holes_drawing_dimensions", StringComparison.OrdinalIgnoreCase) &&
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

public static class SolidWorksDrawingDimensionReportWriter
{
    public static async Task WriteAsync(
        string reportPath,
        SolidWorksDrawingDimensionReport report,
        CancellationToken cancellationToken = default)
    {
        SolidWorksFakeSuccessGuard.NormalizeDimensionReport(report);
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

    public static void Write(string reportPath, SolidWorksDrawingDimensionReport report)
    {
        SolidWorksFakeSuccessGuard.NormalizeDimensionReport(report);
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

public sealed class LateBoundSolidWorksDrawingDimensionBuilder : ISolidWorksDrawingDimensionBuilder
{
    private const double MmToMeters = 0.001d;
    private const double DimensionTextHeightMeters = 0.0035d;
    private readonly ISolidWorksComFacade _comFacade;
    private readonly ISolidWorksFileVerifier _fileVerifier;

    public LateBoundSolidWorksDrawingDimensionBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null)
    {
        _comFacade = comFacade ?? new LateBoundSolidWorksComFacade();
        _fileVerifier = fileVerifier ?? new SolidWorksFileVerifier();
    }

    public Task<SolidWorksDrawingDimensionBuildResult> CreateDimensionedDrawingAsync(
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

    private SolidWorksDrawingDimensionBuildResult CreateCore(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var issues = new List<string>();
        object? drawingDocument = null;
        var outputDirectory = SolidWorksDrawingDimensionBuildOutput.ResolveOutputDirectory(request, options);
        var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.SLDDRW");
        var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.pdf");
        var reportPath = Path.Combine(outputDirectory, "dimension_report.json");
        var report = new SolidWorksDrawingDimensionReport
        {
            SourceDrawingPath = request.SourceDrawingPath,
            OutputDirectory = outputDirectory,
            SolidWorksConnected = true,
            SolidWorksVersion = solidWorksVersion,
            SlddrwPath = drawingPath,
            PdfPath = pdfPath
        };

        try
        {
            Directory.CreateDirectory(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            report.Operations.Add("drawing_dimension_request_received");
            report.Operations.Add("safety_flags_checked");

            var sourceDrawingCandidate = request.SourceDrawingPath ?? request.SourcePartPath;
            var sourceDrawingPath = Path.GetFullPath(sourceDrawingCandidate ?? string.Empty);
            if (string.IsNullOrWhiteSpace(sourceDrawingCandidate) || !File.Exists(sourceDrawingPath))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_drawing_missing",
                    $"source_drawing_missing: {sourceDrawingCandidate ?? "<null>"}");
            }

            report.SourceDrawingPath = sourceDrawingPath;
            report.Operations.Add("source_drawing_checked");

            report.Operations.Add("source_drawing_open_started");
            drawingDocument = OpenDrawing(application, sourceDrawingPath);
            if (drawingDocument is null)
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_drawing_open_failed",
                    $"source_drawing_open_failed: {sourceDrawingPath}");
            }

            report.DrawingOpened = true;
            report.Operations.Add("source_drawing_open_success");

            if (!ActivateDocument(application, drawingDocument))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_drawing_open_failed",
                    $"source_drawing_open_failed: could not activate drawing {sourceDrawingPath}");
            }

            var views = ConfirmRequiredViews(drawingDocument, report);
            if (views.Count < 4)
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "drawing_view_missing",
                    $"drawing_view_missing: confirmed {string.Join(", ", report.ViewsConfirmed)}");
            }

            var frontView = views.First(view => view.ExpectedName.Equals("Front", StringComparison.OrdinalIgnoreCase));
            if (!ActivateDrawingView(drawingDocument, frontView))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "drawing_view_activate_failed",
                    $"drawing_view_activate_failed: {frontView.ViewName}");
            }

            AddRequiredDimensions(drawingDocument, report);
            SaveDrawing(drawingDocument, drawingPath, report, logs);
            ExportPdf(application, drawingDocument, pdfPath, report, logs);
            SolidWorksFakeSuccessGuard.RequireDimensionCanPass(report);

            report.FinalStatus = "Passed";
            report.CompletedAt = DateTimeOffset.UtcNow;
            RefreshFileState(report);
            report.Operations.Add("dimension_report_write_started");
            report.Operations.Add("dimension_report_written");
            SolidWorksDrawingDimensionReportWriter.Write(reportPath, report);

            return SolidWorksDrawingDimensionBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimensioned-drawing", "Drawing", drawingPath, ".SLDDRW", "带基础尺寸的真实 SolidWorks 工程图文件。"),
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimensioned-drawing-pdf", "Pdf", pdfPath, ".pdf", "带基础尺寸的工程图 PDF。"),
                    SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimension-report", "DimensionReport", reportPath, ".json", "真实工程图基础尺寸报告。")
                },
                logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray());
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var stage = report.FailureStage ?? MapDrawingDimensionFailureStage(ex.GetBaseException().Message);
            return FailWithReport(report, reportPath, logs, issues, stage, ex.GetBaseException().Message);
        }
        finally
        {
            if (drawingDocument is not null)
            {
                ReleaseComObject(drawingDocument);
            }
        }
    }

    private void AddRequiredDimensions(object drawingDocument, SolidWorksDrawingDimensionReport report)
    {
        report.Operations.Add("length_dimension_started");
        if (!TryAddLinearDimension(
                drawingDocument,
                report,
                "plate_length",
                expectedValueMm: 160,
                failureStage: "length_dimension_failed",
                new DrawingPoint(0.020d, 0.150d, 0d),
                new DrawingPoint(0.180d, 0.150d, 0d),
                new DrawingPoint(0.100d, 0.138d, 0d),
                angleRadians: 0d))
        {
            throw new InvalidOperationException("length_dimension_failed: IDrawingDoc.CreateLinearDim4 returned null.");
        }

        report.LengthDimensionAdded = true;
        report.Operations.Add("length_dimension_success");

        report.Operations.Add("width_dimension_started");
        if (!TryAddLinearDimension(
                drawingDocument,
                report,
                "plate_width",
                expectedValueMm: 80,
                failureStage: "width_dimension_failed",
                new DrawingPoint(0.014d, 0.160d, 0d),
                new DrawingPoint(0.014d, 0.240d, 0d),
                new DrawingPoint(0.003d, 0.200d, 0d),
                angleRadians: Math.PI / 2d))
        {
            throw new InvalidOperationException("width_dimension_failed: IDrawingDoc.CreateLinearDim4 returned null.");
        }

        report.WidthDimensionAdded = true;
        report.Operations.Add("width_dimension_success");

        report.Operations.Add("thickness_dimension_started");
        if (!TryAddLinearDimension(
                drawingDocument,
                report,
                "plate_thickness",
                expectedValueMm: 12,
                failureStage: "thickness_dimension_failed",
                new DrawingPoint(0.210d, 0.190d, 0d),
                new DrawingPoint(0.222d, 0.190d, 0d),
                new DrawingPoint(0.216d, 0.178d, 0d),
                angleRadians: 0d))
        {
            throw new InvalidOperationException("thickness_dimension_failed: IDrawingDoc.CreateLinearDim4 returned null.");
        }

        report.ThicknessDimensionAdded = true;
        report.Operations.Add("thickness_dimension_success");

        report.Operations.Add("hole_diameter_dimension_started");
        if (!TryAddDiameterDimension(
                drawingDocument,
                report,
                "hole_diameter",
                expectedValueMm: 10,
                failureStage: "hole_diameter_dimension_failed",
                new DrawingPoint(0.060d, 0.180d, 0d),
                new DrawingPoint(0.065d, 0.180d, 0d),
                new DrawingPoint(0.055d, 0.180d, 0d),
                new DrawingPoint(0.085d, 0.192d, 0d)))
        {
            throw new InvalidOperationException("hole_diameter_dimension_failed: IDrawingDoc.ICreateDiamDim4 returned null.");
        }

        report.HoleDiameterDimensionAdded = true;
        report.Operations.Add("hole_diameter_dimension_success");

        report.Operations.Add("hole_position_dimension_started");
        var horizontalCenterDistanceAdded = TryAddLinearDimension(
            drawingDocument,
            report,
            "hole_center_distance_x",
            expectedValueMm: 120,
            failureStage: "hole_position_dimension_failed",
            new DrawingPoint(0.040d, 0.170d, 0d),
            new DrawingPoint(0.160d, 0.170d, 0d),
            new DrawingPoint(0.100d, 0.158d, 0d),
            angleRadians: 0d);
        var verticalCenterDistanceAdded = TryAddLinearDimension(
            drawingDocument,
            report,
            "hole_center_distance_y",
            expectedValueMm: 40,
            failureStage: "hole_position_dimension_failed",
            new DrawingPoint(0.040d, 0.180d, 0d),
            new DrawingPoint(0.040d, 0.220d, 0d),
            new DrawingPoint(0.028d, 0.200d, 0d),
            angleRadians: Math.PI / 2d);

        if (!horizontalCenterDistanceAdded || !verticalCenterDistanceAdded)
        {
            throw new InvalidOperationException("hole_position_dimension_failed: center distance dimension creation failed.");
        }

        report.HolePositionDimensionAdded = true;
        report.Operations.Add("hole_position_dimension_success");
        report.Warnings.Add("hole_position_dimension_strategy: 使用 CreateLinearDim4 非关联中心距，后续可在 V1.3 之后补充可关联孔中心选取。");
    }

    private bool TryAddLinearDimension(
        object drawingDocument,
        SolidWorksDrawingDimensionReport report,
        string name,
        double expectedValueMm,
        string failureStage,
        DrawingPoint start,
        DrawingPoint end,
        DrawingPoint text,
        double angleRadians)
    {
        var displayDimension = TryInvoke(
            drawingDocument,
            "CreateLinearDim4",
            Point(start),
            Point(end),
            Point(0d, 0d, 1d),
            Point(start),
            Point(end),
            Point(text),
            expectedValueMm * MmToMeters,
            angleRadians,
            DimensionTextHeightMeters);

        var status = displayDimension is null ? "Failed" : "Passed";
        var success = displayDimension is not null;
        report.Dimensions.Add(new SolidWorksDrawingDimensionResult(
            name,
            expectedValueMm,
            status,
            failureStage,
            "IDrawingDoc.CreateLinearDim4_non_associative",
            displayDimension is null ? "CreateLinearDim4 returned null." : "CreateLinearDim4 returned a display dimension.",
            Attempted: true,
            Success: success,
            FailureReason: success ? null : "CreateLinearDim4 returned null."));

        return success;
    }

    private bool TryAddDiameterDimension(
        object drawingDocument,
        SolidWorksDrawingDimensionReport report,
        string name,
        double expectedValueMm,
        string failureStage,
        DrawingPoint center,
        DrawingPoint nearest,
        DrawingPoint farthest,
        DrawingPoint text)
    {
        var displayDimension = TryInvoke(
            drawingDocument,
            "ICreateDiamDim4",
            Point(center),
            Point(nearest),
            Point(farthest),
            Point(0d, 0d, 1d),
            Point(text),
            expectedValueMm * MmToMeters,
            DimensionTextHeightMeters) ??
            TryInvoke(
                drawingDocument,
                "CreateDiamDim4",
                Point(center),
                Point(nearest),
                Point(farthest),
                Point(0d, 0d, 1d),
                Point(text),
                expectedValueMm * MmToMeters,
                DimensionTextHeightMeters);

        var status = displayDimension is null ? "Failed" : "Passed";
        var success = displayDimension is not null;
        report.Dimensions.Add(new SolidWorksDrawingDimensionResult(
            name,
            expectedValueMm,
            status,
            failureStage,
            "IDrawingDoc.ICreateDiamDim4_non_associative",
            displayDimension is null ? "ICreateDiamDim4 returned null." : "ICreateDiamDim4 returned a display dimension.",
            Attempted: true,
            Success: success,
            FailureReason: success ? null : "ICreateDiamDim4 returned null."));

        return success;
    }

    private List<ConfirmedDrawingView> ConfirmRequiredViews(
        object drawingDocument,
        SolidWorksDrawingDimensionReport report)
    {
        report.Operations.Add("drawing_views_confirm_started");
        var discovered = DiscoverModelViews(drawingDocument);
        var confirmed = new List<ConfirmedDrawingView>();
        var expectedViews = new[] { "Front", "Top", "Right", "Isometric" };

        foreach (var expected in expectedViews)
        {
            var match = discovered.FirstOrDefault(view =>
                ViewLooksLike(view, expected) &&
                confirmed.All(existing => !ReferenceEquals(existing.ViewObject, view.ViewObject)));
            if (match is not null)
            {
                confirmed.Add(match with { ExpectedName = expected });
                report.ViewsConfirmed.Add(expected);
            }
        }

        if (confirmed.Count < expectedViews.Length && discovered.Count >= expectedViews.Length)
        {
            report.ViewsConfirmedByPositionFallback = true;
            report.Warnings.Add("drawing_view_name_fallback: 视图名称或方向名不可完全读取，按 V1.1 创建顺序确认 Front/Top/Right/Isometric。");
            confirmed.Clear();
            report.ViewsConfirmed.Clear();
            for (var index = 0; index < expectedViews.Length; index++)
            {
                confirmed.Add(discovered[index] with { ExpectedName = expectedViews[index] });
                report.ViewsConfirmed.Add(expectedViews[index]);
            }
        }

        report.Operations.Add(confirmed.Count == expectedViews.Length
            ? "drawing_views_confirm_success"
            : "drawing_views_confirm_failed");
        return confirmed;
    }

    private List<ConfirmedDrawingView> DiscoverModelViews(object drawingDocument)
    {
        var result = new List<ConfirmedDrawingView>();
        object? view = TryInvoke(drawingDocument, "GetFirstView");
        if (view is not null)
        {
            view = TryInvoke(view, "GetNextView");
        }

        while (view is not null)
        {
            var viewName = TryInvoke(view, "GetName2")?.ToString() ??
                           TryGetProperty(view, "Name")?.ToString() ??
                           $"DrawingView{result.Count + 1}";
            var orientationName = TryInvoke(view, "GetOrientationName")?.ToString() ??
                                  TryGetProperty(view, "OrientationName")?.ToString();

            result.Add(new ConfirmedDrawingView(
                ExpectedName: string.Empty,
                ViewName: viewName,
                OrientationName: orientationName,
                ViewObject: view));

            view = TryInvoke(view, "GetNextView");
        }

        return result;
    }

    private static bool ViewLooksLike(ConfirmedDrawingView view, string expected)
    {
        var candidates = new[] { view.ViewName, view.OrientationName }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        return candidates.Any(value =>
            value!.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
            value.Contains($"*{expected}", StringComparison.OrdinalIgnoreCase));
    }

    private bool ActivateDrawingView(object drawingDocument, ConfirmedDrawingView view)
    {
        if (!string.IsNullOrWhiteSpace(view.ViewName) &&
            TryInvokeBool(drawingDocument, "ActivateView", view.ViewName))
        {
            return true;
        }

        return TryInvokeBool(view.ViewObject, "Select2", false, 0);
    }

    private void SaveDrawing(
        object drawingDocument,
        string drawingPath,
        SolidWorksDrawingDimensionReport report,
        List<string> logs)
    {
        report.Operations.Add("dimension_save_started");
        Directory.CreateDirectory(Path.GetDirectoryName(drawingPath)!);
        var errors = new List<string>();
        var warnings = new List<string>();
        TryInvoke(drawingDocument, "ForceRebuild3", false);
        var saved = _comFacade.TryExtensionSaveAs(drawingDocument, drawingPath, null, errors, warnings) ||
                    TryInvokeBool(drawingDocument, "SaveAs3", drawingPath, 0, 1) ||
                    TryInvokeBool(drawingDocument, "SaveAs", drawingPath);

        if (!saved)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("dimension_save_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"dimension_save_failed: {drawingPath}");
        }

        RefreshFileState(report);
        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            throw new IOException($"dimension_save_failed: {drawingPath}");
        }

        report.Operations.Add("dimension_save_success");
        logs.Add($"Saved dimensioned SolidWorks drawing: {drawingPath}.");
    }

    private void ExportPdf(
        object application,
        object drawingDocument,
        string pdfPath,
        SolidWorksDrawingDimensionReport report,
        List<string> logs)
    {
        report.Operations.Add("dimension_pdf_export_started");
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
            report.Errors.AddRange(errors.DefaultIfEmpty("dimension_pdf_export_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"dimension_pdf_export_failed: {pdfPath}");
        }

        RefreshFileState(report);
        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            throw new IOException($"dimension_pdf_export_failed: {pdfPath}");
        }

        report.Operations.Add("dimension_pdf_export_success");
        logs.Add($"Exported dimensioned drawing PDF: {pdfPath}.");
    }

    private object? OpenDrawing(object application, string sourceDrawingPath)
    {
        const int swDocDrawing = 3;
        const int swOpenDocOptionsSilent = 1;
        return TryInvoke(application, "OpenDoc6", sourceDrawingPath, swDocDrawing, swOpenDocOptionsSilent, string.Empty, 0, 0) ??
               TryInvoke(application, "OpenDoc", sourceDrawingPath, swDocDrawing) ??
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

    private object? TryGetPdfExportData(object application, List<string> warnings)
    {
        try
        {
            const int swExportPdfData = 1;
            var pdfData = TryInvoke(application, "GetExportFileData", swExportPdfData);
            if (pdfData is null)
            {
                warnings.Add("dimension_pdf_export_data_unavailable: GetExportFileData returned null; SaveAs without export data will be attempted.");
                return null;
            }

            TryInvoke(pdfData, "SetSheets", 1, null);
            return pdfData;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException)
        {
            warnings.Add($"dimension_pdf_export_data_unavailable: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private SolidWorksDrawingDimensionBuildResult FailWithReport(
        SolidWorksDrawingDimensionReport report,
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
        report.Operations.Add("dimension_report_write_started");

        SolidWorksArtifact[] generatedArtifacts;
        try
        {
            report.Operations.Add("dimension_report_written");
            SolidWorksDrawingDimensionReportWriter.Write(reportPath, report);
            generatedArtifacts =
            [
                SolidWorksDrawingDimensionBuildOutput.Artifact("real-dimension-report", "DimensionReport", reportPath, ".json", "真实工程图基础尺寸失败报告。")
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var reportIssue = $"dimension_report_write_failed: {ex.Message}";
            report.FailureStage = "dimension_report_write_failed";
            issues.Add(reportIssue);
            generatedArtifacts = [];
        }

        return SolidWorksDrawingDimensionBuildResult.Failed(
            logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray(),
            issues,
            generatedArtifacts);
    }

    private void RefreshFileState(SolidWorksDrawingDimensionReport report)
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

    public static string MapDrawingDimensionFailureStage(string text)
    {
        if (text.Contains("source_drawing_missing", StringComparison.OrdinalIgnoreCase))
        {
            return "source_drawing_missing";
        }

        if (text.Contains("source_drawing_open", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OpenDoc", StringComparison.OrdinalIgnoreCase))
        {
            return "source_drawing_open_failed";
        }

        if (text.Contains("drawing_view_missing", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_view_missing";
        }

        if (text.Contains("drawing_view_activate", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ActivateView", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_view_activate_failed";
        }

        if (text.Contains("length_dimension", StringComparison.OrdinalIgnoreCase))
        {
            return "length_dimension_failed";
        }

        if (text.Contains("width_dimension", StringComparison.OrdinalIgnoreCase))
        {
            return "width_dimension_failed";
        }

        if (text.Contains("thickness_dimension", StringComparison.OrdinalIgnoreCase))
        {
            return "thickness_dimension_failed";
        }

        if (text.Contains("hole_diameter", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("CreateDiam", StringComparison.OrdinalIgnoreCase))
        {
            return "hole_diameter_dimension_failed";
        }

        if (text.Contains("hole_position", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("center distance", StringComparison.OrdinalIgnoreCase))
        {
            return "hole_position_dimension_failed";
        }

        if (text.Contains("dimension_save", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("slddrw", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "dimension_save_failed";
        }

        if (text.Contains("dimension_pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return "dimension_pdf_export_failed";
        }

        if (text.Contains("dimension_report", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "dimension_report_write_failed";
        }

        return "drawing_dimension_api_evidence_insufficient";
    }

    private object GetProperty(object target, string name) => _comFacade.GetProperty(target, name);

    private object? TryGetProperty(object? target, string name) => _comFacade.TryGetProperty(target, name);

    private object? Invoke(object target, string name, params object?[] args) => _comFacade.Invoke(target, name, args);

    private object? TryInvoke(object? target, string name, params object?[] args) => _comFacade.TryInvoke(target, name, args);

    private bool TryInvokeBool(object? target, string name, params object?[] args) => _comFacade.TryInvokeBool(target, name, args);

    private static double[] Point(DrawingPoint point) => [point.X, point.Y, point.Z];

    private static double[] Point(double x, double y, double z) => [x, y, z];

    private void ReleaseComObject(object value) => _comFacade.ReleaseComObject(value);

    private sealed record ConfirmedDrawingView(
        string ExpectedName,
        string ViewName,
        string? OrientationName,
        object ViewObject);

    private readonly record struct DrawingPoint(double X, double Y, double Z);
}
