using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksDrawingTitleBlockBuilder
{
    Task<SolidWorksDrawingTitleBlockBuildResult> ApplyTitleBlockAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken);
}

public sealed record SolidWorksDrawingTitleBlockBuildResult(
    string Status,
    IReadOnlyList<SolidWorksArtifact> GeneratedArtifacts,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    bool RealCadExecuted)
{
    public static SolidWorksDrawingTitleBlockBuildResult Completed(
        IReadOnlyList<SolidWorksArtifact> generatedArtifacts,
        IReadOnlyList<string> logs) =>
        new("Completed", generatedArtifacts, logs, Array.Empty<string>(), RealCadExecuted: true);

    public static SolidWorksDrawingTitleBlockBuildResult Failed(
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        IReadOnlyList<SolidWorksArtifact>? generatedArtifacts = null) =>
        new("Failed", generatedArtifacts ?? Array.Empty<SolidWorksArtifact>(), logs, issues, RealCadExecuted: false);
}

public static class SolidWorksDrawingTitleBlockBuildOutput
{
    public const string ExecutionMode = "RealDrawingTitleBlock";

    public static string ResolveOutputDirectory(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var fullRoot = Path.GetFullPath(requestedRoot);
        var normalized = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryName = Path.GetFileName(normalized);

        if (IsUnderTitleBlockRoot(normalized) &&
            !directoryName.Equals("plate_basic_4holes_title_block", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var target = directoryName.Equals("plate_basic_4holes_title_block", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(normalized, TimestampSegment())
            : Path.Combine(normalized, "plate_basic_4holes_title_block", TimestampSegment());

        if (!Directory.Exists(target) || Directory.GetFileSystemEntries(target).Length == 0)
        {
            return target;
        }

        return Path.Combine(
            Path.GetDirectoryName(target) ?? fullRoot,
            $"plate_basic_4holes_title_block_{TimestampSegment()}");
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

    private static bool IsUnderTitleBlockRoot(string path)
    {
        var directory = new DirectoryInfo(path);
        while (directory is not null)
        {
            if (directory.Name.Equals("plate_basic_4holes_title_block", StringComparison.OrdinalIgnoreCase) &&
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

public static class SolidWorksDrawingTitleBlockReportWriter
{
    public static async Task WriteAsync(
        string reportPath,
        SolidWorksDrawingTitleBlockReport report,
        CancellationToken cancellationToken = default)
    {
        SolidWorksFakeSuccessGuard.NormalizeTitleBlockReport(report);
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

    public static void Write(string reportPath, SolidWorksDrawingTitleBlockReport report)
    {
        SolidWorksFakeSuccessGuard.NormalizeTitleBlockReport(report);
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

public sealed class LateBoundSolidWorksDrawingTitleBlockBuilder : ISolidWorksDrawingTitleBlockBuilder
{
    private const int SwDocDrawing = 3;
    private const int SwOpenDocOptionsSilent = 1;
    private const int SwCustomInfoText = 30;
    private const int SwCustomPropertyReplaceValue = 2;
    private readonly ISolidWorksComFacade _comFacade;
    private readonly ISolidWorksFileVerifier _fileVerifier;
    private readonly ISolidWorksPropertyReader _propertyReader;

    public LateBoundSolidWorksDrawingTitleBlockBuilder(
        ISolidWorksComFacade? comFacade = null,
        ISolidWorksFileVerifier? fileVerifier = null,
        ISolidWorksPropertyReader? propertyReader = null)
    {
        _comFacade = comFacade ?? new LateBoundSolidWorksComFacade();
        _fileVerifier = fileVerifier ?? new SolidWorksFileVerifier();
        _propertyReader = propertyReader ?? new SolidWorksCustomPropertyReader(_comFacade);
    }

    public Task<SolidWorksDrawingTitleBlockBuildResult> ApplyTitleBlockAsync(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(
            () => ApplyCore(application, request, options, solidWorksVersion, cancellationToken),
            cancellationToken);
    }

    private SolidWorksDrawingTitleBlockBuildResult ApplyCore(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var issues = new List<string>();
        object? drawingDocument = null;
        var outputDirectory = SolidWorksDrawingTitleBlockBuildOutput.ResolveOutputDirectory(request, options);
        var drawingPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.SLDDRW");
        var pdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.pdf");
        var reportPath = Path.Combine(outputDirectory, "title_block_report.json");
        var report = new SolidWorksDrawingTitleBlockReport
        {
            SourceDimensionedDrawingPath = request.SourceDimensionedDrawingPath ?? request.SourceDrawingPath,
            OutputDirectory = outputDirectory,
            SolidWorksConnected = true,
            SolidWorksVersion = solidWorksVersion,
            SlddrwPath = drawingPath,
            PdfPath = pdfPath,
            TitleBlockPopulationStrategy = "custom_properties_only",
            TitleBlockFieldsVerifiedInSheetFormat = false
        };
        report.Warnings.Add("title_block_sheet_format_not_verified: custom properties are written and read back, but sheet format note rendering is not verified in V1.3.");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            cancellationToken.ThrowIfCancellationRequested();
            report.Operations.Add("drawing_title_block_request_received");
            report.Operations.Add("safety_flags_checked");

            var sourceDrawingCandidate =
                request.SourceDimensionedDrawingPath ??
                request.SourceDrawingPath ??
                request.SourcePartPath;
            var sourceDrawingPath = Path.GetFullPath(sourceDrawingCandidate ?? string.Empty);
            if (string.IsNullOrWhiteSpace(sourceDrawingCandidate) || !File.Exists(sourceDrawingPath))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "source_dimensioned_drawing_missing",
                    $"source_dimensioned_drawing_missing: {sourceDrawingCandidate ?? "<null>"}");
            }

            report.SourceDimensionedDrawingPath = sourceDrawingPath;
            report.Operations.Add("source_dimensioned_drawing_checked");

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

            var sheet = TryInvoke(drawingDocument, "GetCurrentSheet");
            if (sheet is null)
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "title_block_template_missing",
                    "title_block_template_missing: GetCurrentSheet returned null.");
            }

            report.TitleBlockTemplateDetected = true;
            report.Operations.Add("title_block_template_detected");

            var scale = ReadSheetScale(sheet, report);
            if (string.IsNullOrWhiteSpace(scale))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "drawing_property_read_failed",
                    "drawing_property_read_failed: ISheet.GetProperties2 did not return usable sheet scale data.");
            }

            report.DrawingPropertiesRead = true;
            report.Operations.Add("drawing_properties_read_success");

            PopulateTitleFields(request, report, scale);
            var customPropertyManager = GetCustomPropertyManager(drawingDocument);
            if (customPropertyManager is null)
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "custom_property_write_failed",
                    "custom_property_write_failed: IModelDocExtension.CustomPropertyManager returned null.");
            }

            report.Operations.Add("custom_property_write_started");
            WriteRequiredProperties(customPropertyManager, report);
            if (report.Properties.Any(property =>
                    !string.Equals(property.Status, "Passed", StringComparison.OrdinalIgnoreCase)))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "custom_property_write_failed",
                    "custom_property_write_failed: one or more title block custom properties were not written.");
            }

            report.CustomPropertiesWritten = true;
            report.Operations.Add("custom_property_write_success");

            if (!UpdateTitleBlock(drawingDocument))
            {
                return FailWithReport(
                    report,
                    reportPath,
                    logs,
                    issues,
                    "title_block_update_failed",
                    "title_block_update_failed: drawing rebuild did not report success.");
            }

            report.TitleBlockUpdated = true;
            report.Operations.Add("title_block_update_success");

            SaveDrawing(drawingDocument, drawingPath, report, logs);
            ExportPdf(application, drawingDocument, pdfPath, report, logs);
            SolidWorksFakeSuccessGuard.RequireTitleBlockCanPass(report);

            report.FinalStatus = "Passed";
            report.CompletedAt = DateTimeOffset.UtcNow;
            RefreshFileState(report);
            report.Operations.Add("title_block_report_write_started");
            report.Operations.Add("title_block_report_written");
            SolidWorksDrawingTitleBlockReportWriter.Write(reportPath, report);

            return SolidWorksDrawingTitleBlockBuildResult.Completed(
                new[]
                {
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-drawing", "Drawing", drawingPath, ".SLDDRW", "SolidWorks drawing with minimal title block metadata."),
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-drawing-pdf", "Pdf", pdfPath, ".pdf", "PDF exported from the title block drawing."),
                    SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-report", "TitleBlockReport", reportPath, ".json", "SolidWorks title block report.")
                },
                logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray());
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var stage = report.FailureStage ?? MapDrawingTitleBlockFailureStage(ex.GetBaseException().Message);
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

    private string? ReadSheetScale(object sheet, SolidWorksDrawingTitleBlockReport report)
    {
        report.Operations.Add("drawing_properties_read_started");
        var properties = TryInvoke(sheet, "GetProperties2");
        if (properties is not Array values || values.Length < 4)
        {
            return null;
        }

        var scaleNumerator = ConvertToDouble(values.GetValue(2));
        var scaleDenominator = ConvertToDouble(values.GetValue(3));
        if (scaleNumerator <= 0d || scaleDenominator <= 0d)
        {
            report.Warnings.Add("sheet_scale_auto: GetProperties2 did not expose positive scale values; scale recorded as auto.");
            return "auto";
        }

        return $"{scaleNumerator.ToString("0.###", CultureInfo.InvariantCulture)}:{scaleDenominator.ToString("0.###", CultureInfo.InvariantCulture)}";
    }

    private static void PopulateTitleFields(
        SolidWorksWorkerRequest request,
        SolidWorksDrawingTitleBlockReport report,
        string scale)
    {
        report.PartName = "plate_basic_4holes";
        report.DrawingNumber = "PLATE-BASIC-4HOLES";
        report.Material = ResolveMaterial(request);
        report.Scale = scale;
        report.DrawingDate = DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        report.Revision = "A";
    }

    private static string ResolveMaterial(SolidWorksWorkerRequest request)
    {
        var material = request.BuildPlan.Operations
            .SelectMany(operation => operation.Parameters)
            .FirstOrDefault(parameter =>
                parameter.Key.Contains("material", StringComparison.OrdinalIgnoreCase) ||
                parameter.Key.Contains("材质", StringComparison.OrdinalIgnoreCase) ||
                parameter.Key.Contains("材料", StringComparison.OrdinalIgnoreCase))
            .Value;

        return string.IsNullOrWhiteSpace(material) ? "Q235" : material;
    }

    private void WriteRequiredProperties(
        object customPropertyManager,
        SolidWorksDrawingTitleBlockReport report)
    {
        foreach (var (name, value) in RequiredProperties(report))
        {
            report.Properties.Add(WriteProperty(customPropertyManager, name, value));
        }
    }

    private static IEnumerable<(string Name, string Value)> RequiredProperties(SolidWorksDrawingTitleBlockReport report)
    {
        yield return ("PartName", report.PartName);
        yield return ("DrawingNumber", report.DrawingNumber);
        yield return ("Material", report.Material);
        yield return ("Scale", report.Scale);
        yield return ("DrawingDate", report.DrawingDate);
        yield return ("Revision", report.Revision);
    }

    private SolidWorksDrawingTitleBlockProperty WriteProperty(
        object customPropertyManager,
        string name,
        string value)
    {
        var addResult = TryInvoke(customPropertyManager, "Add3", name, SwCustomInfoText, value, SwCustomPropertyReplaceValue);
        var setResult = addResult is null || !ApiResultLooksSuccessful(addResult)
            ? TryInvoke(customPropertyManager, "Set2", name, value)
            : addResult;

        var verifiedValue = _propertyReader.ReadCustomProperty(customPropertyManager, name);
        var verified = !string.IsNullOrWhiteSpace(verifiedValue) &&
                       string.Equals(verifiedValue, value, StringComparison.OrdinalIgnoreCase);
        var succeeded = ApiResultLooksSuccessful(setResult) && verified;

        return new SolidWorksDrawingTitleBlockProperty(
            name,
            value,
            succeeded ? "Passed" : "Failed",
            "custom_property_write_failed",
            "IModelDocExtension.CustomPropertyManager_Add3_Set2_Get6",
            succeeded
                ? "Custom property written and accepted by SolidWorks API."
                : $"Custom property write failed or verification mismatch. Actual value: {verifiedValue ?? "<unavailable>"}.");
    }

    private static bool ApiResultLooksSuccessful(object? value) =>
        value switch
        {
            null => false,
            bool boolean => boolean,
            int integer => integer >= 0,
            short integer => integer >= 0,
            long integer => integer >= 0,
            _ => true
        };

    private bool UpdateTitleBlock(object drawingDocument) =>
        TryInvokeBool(drawingDocument, "ForceRebuild3", false) ||
        TryInvokeBool(drawingDocument, "EditRebuild3");

    private void SaveDrawing(
        object drawingDocument,
        string drawingPath,
        SolidWorksDrawingTitleBlockReport report,
        List<string> logs)
    {
        report.Operations.Add("title_block_save_started");
        Directory.CreateDirectory(Path.GetDirectoryName(drawingPath)!);
        var errors = new List<string>();
        var warnings = new List<string>();
        var saved = _comFacade.TryExtensionSaveAs(drawingDocument, drawingPath, null, errors, warnings) ||
                    TryInvokeBool(drawingDocument, "SaveAs3", drawingPath, 0, 1) ||
                    TryInvokeBool(drawingDocument, "SaveAs", drawingPath);

        if (!saved)
        {
            report.Errors.AddRange(errors.DefaultIfEmpty("title_block_save_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"title_block_save_failed: {drawingPath}");
        }

        RefreshFileState(report);
        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            throw new IOException($"title_block_save_failed: {drawingPath}");
        }

        report.Operations.Add("title_block_save_success");
        logs.Add($"Saved title block SolidWorks drawing: {drawingPath}.");
    }

    private void ExportPdf(
        object application,
        object drawingDocument,
        string pdfPath,
        SolidWorksDrawingTitleBlockReport report,
        List<string> logs)
    {
        report.Operations.Add("title_block_pdf_export_started");
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
            report.Errors.AddRange(errors.DefaultIfEmpty("title_block_pdf_export_failed: SaveAs returned false."));
            report.Warnings.AddRange(warnings);
            throw new IOException($"title_block_pdf_export_failed: {pdfPath}");
        }

        RefreshFileState(report);
        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            throw new IOException($"title_block_pdf_export_failed: {pdfPath}");
        }

        report.Operations.Add("title_block_pdf_export_success");
        logs.Add($"Exported title block drawing PDF: {pdfPath}.");
    }

    private object? OpenDrawing(object application, string sourceDrawingPath) =>
        TryInvoke(application, "OpenDoc6", sourceDrawingPath, SwDocDrawing, SwOpenDocOptionsSilent, string.Empty, 0, 0) ??
        TryInvoke(application, "OpenDoc", sourceDrawingPath, SwDocDrawing) ??
        TryGetProperty(application, "ActiveDoc");

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

    private object? GetCustomPropertyManager(object drawingDocument)
    {
        var extension = TryGetProperty(drawingDocument, "Extension");
        return extension is null
            ? null
            : TryInvoke(extension, "CustomPropertyManager", string.Empty) ??
              TryGetIndexedProperty(extension, "CustomPropertyManager", string.Empty);
    }

    private object? TryGetPdfExportData(object application, List<string> warnings)
    {
        try
        {
            const int swExportPdfData = 1;
            var pdfData = TryInvoke(application, "GetExportFileData", swExportPdfData);
            if (pdfData is null)
            {
                warnings.Add("title_block_pdf_export_data_unavailable: GetExportFileData returned null; SaveAs without export data will be attempted.");
                return null;
            }

            TryInvoke(pdfData, "SetSheets", 1, null);
            return pdfData;
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or MissingMethodException)
        {
            warnings.Add($"title_block_pdf_export_data_unavailable: {ex.GetBaseException().Message}");
            return null;
        }
    }

    private SolidWorksDrawingTitleBlockBuildResult FailWithReport(
        SolidWorksDrawingTitleBlockReport report,
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
        report.Operations.Add("title_block_report_write_started");

        SolidWorksArtifact[] generatedArtifacts;
        try
        {
            report.Operations.Add("title_block_report_written");
            SolidWorksDrawingTitleBlockReportWriter.Write(reportPath, report);
            generatedArtifacts =
            [
                SolidWorksDrawingTitleBlockBuildOutput.Artifact("real-title-block-report", "TitleBlockReport", reportPath, ".json", "SolidWorks title block failure report.")
            ];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var reportIssue = $"title_block_report_write_failed: {ex.Message}";
            report.FailureStage = "title_block_report_write_failed";
            issues.Add(reportIssue);
            generatedArtifacts = [];
        }

        return SolidWorksDrawingTitleBlockBuildResult.Failed(
            logs.Concat(report.Operations.Select(operation => $"operation_executed: {operation}")).ToArray(),
            issues,
            generatedArtifacts);
    }

    private void RefreshFileState(SolidWorksDrawingTitleBlockReport report)
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

    public static string MapDrawingTitleBlockFailureStage(string text)
    {
        if (text.Contains("source_dimensioned_drawing_missing", StringComparison.OrdinalIgnoreCase))
        {
            return "source_dimensioned_drawing_missing";
        }

        if (text.Contains("source_drawing_open", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("OpenDoc", StringComparison.OrdinalIgnoreCase))
        {
            return "source_drawing_open_failed";
        }

        if (text.Contains("title_block_template", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("GetCurrentSheet", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_template_missing";
        }

        if (text.Contains("custom_property", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("CustomPropertyManager", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Add3", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Set2", StringComparison.OrdinalIgnoreCase))
        {
            return "custom_property_write_failed";
        }

        if (text.Contains("drawing_property", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("GetProperties2", StringComparison.OrdinalIgnoreCase))
        {
            return "drawing_property_read_failed";
        }

        if (text.Contains("title_block_update", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("ForceRebuild", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("EditRebuild", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_update_failed";
        }

        if (text.Contains("title_block_save", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("slddrw", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("save", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_save_failed";
        }

        if (text.Contains("title_block_pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("pdf", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("export", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_pdf_export_failed";
        }

        if (text.Contains("title_block_report", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("report", StringComparison.OrdinalIgnoreCase))
        {
            return "title_block_report_write_failed";
        }

        return "drawing_title_block_api_evidence_insufficient";
    }

    private static double ConvertToDouble(object? value) =>
        value is null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    private object GetProperty(object target, string name) => _comFacade.GetProperty(target, name);

    private object? TryGetProperty(object? target, string name) => _comFacade.TryGetProperty(target, name);

    private object? TryGetIndexedProperty(object target, string name, params object?[] args) =>
        _comFacade.TryGetIndexedProperty(target, name, args);

    private object? TryInvoke(object? target, string name, params object?[] args) => _comFacade.TryInvoke(target, name, args);

    private bool TryInvokeBool(object? target, string name, params object?[] args) => _comFacade.TryInvokeBool(target, name, args);

    private void ReleaseComObject(object value) => _comFacade.ReleaseComObject(value);
}
