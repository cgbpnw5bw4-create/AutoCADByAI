using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using DomainSchemas;

namespace SolidWorksWorker;

public interface ISolidWorksComFacade
{
    object GetProperty(object target, string name);

    object? TryGetProperty(object? target, string name);

    object? TryGetIndexedProperty(object target, string name, params object?[] args);

    object? Invoke(object target, string name, params object?[] args);

    object? InvokeWithArgs(object target, string name, object?[] args);

    object? TryInvoke(object? target, string name, params object?[] args);

    object? TryInvokeWithArgs(object? target, string name, object?[] args);

    bool TryInvokeBool(object? target, string name, params object?[] args);

    bool TrySetProperty(object target, string name, object? value);

    bool TryExtensionSaveAs(
        object model,
        string path,
        object? exportData,
        List<string> errors,
        List<string> warnings);

    void ReleaseComObject(object value);
}

public interface ISolidWorksFileVerifier
{
    SolidWorksFileState GetState(string path);

    bool IsNonEmptyFile(string path);
}

public interface ISolidWorksPropertyReader
{
    string? ReadCustomProperty(object customPropertyManager, string name);
}

public sealed record SolidWorksFileState(
    string Path,
    bool Exists,
    long SizeBytes);

public sealed class LateBoundSolidWorksComFacade : ISolidWorksComFacade
{
    public object GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            Array.Empty<object>())
        ?? throw new InvalidOperationException($"solidworks_property_missing: {name}.");

    public object? TryGetProperty(object? target, string name)
    {
        if (target is null)
        {
            return null;
        }

        try
        {
            return GetProperty(target, name);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    public object? TryGetIndexedProperty(object target, string name, params object?[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                name,
                BindingFlags.GetProperty,
                binder: null,
                target,
                args);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    public object? Invoke(object target, string name, params object?[] args) =>
        InvokeWithArgs(target, name, args);

    public object? InvokeWithArgs(object target, string name, object?[] args) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.InvokeMethod,
            binder: null,
            target,
            args);

    public object? TryInvoke(object? target, string name, params object?[] args)
    {
        if (target is null)
        {
            return null;
        }

        try
        {
            return Invoke(target, name, args);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    public object? TryInvokeWithArgs(object? target, string name, object?[] args)
    {
        if (target is null)
        {
            return null;
        }

        try
        {
            return InvokeWithArgs(target, name, args);
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return null;
        }
    }

    public bool TryInvokeBool(object? target, string name, params object?[] args)
    {
        var value = TryInvoke(target, name, args);
        return value is bool boolean && boolean;
    }

    public bool TrySetProperty(object target, string name, object? value)
    {
        try
        {
            target.GetType().InvokeMember(
                name,
                BindingFlags.SetProperty,
                binder: null,
                target,
                new[] { value });
            return true;
        }
        catch (Exception ex) when (IsComReflectionException(ex))
        {
            return false;
        }
    }

    public bool TryExtensionSaveAs(
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
            var result = InvokeWithArgs(extension, "SaveAs", args);
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
        catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or COMException or InvalidOperationException)
        {
            errors.Add($"save_as_exception: {ex.GetBaseException().Message}");
            return false;
        }
    }

    public void ReleaseComObject(object value)
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

    private static bool IsComReflectionException(Exception ex) =>
        ex is MissingMethodException or TargetInvocationException or COMException or InvalidOperationException;
}

public sealed class SolidWorksFileVerifier : ISolidWorksFileVerifier
{
    public SolidWorksFileState GetState(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var exists = File.Exists(fullPath);
        return new SolidWorksFileState(
            fullPath,
            exists,
            exists ? new FileInfo(fullPath).Length : 0);
    }

    public bool IsNonEmptyFile(string path)
    {
        var state = GetState(path);
        return state.Exists && state.SizeBytes > 0;
    }
}

public sealed class SolidWorksCustomPropertyReader : ISolidWorksPropertyReader
{
    private readonly ISolidWorksComFacade _comFacade;

    public SolidWorksCustomPropertyReader(ISolidWorksComFacade? comFacade = null)
    {
        _comFacade = comFacade ?? new LateBoundSolidWorksComFacade();
    }

    public string? ReadCustomProperty(object customPropertyManager, string name)
    {
        var args = new object?[] { name, false, string.Empty, string.Empty, false, false };
        _ = _comFacade.TryInvokeWithArgs(customPropertyManager, "Get6", args);
        var get6Value = args[2]?.ToString();
        if (!string.IsNullOrWhiteSpace(get6Value))
        {
            return get6Value;
        }

        // Late-bound COM can fail to marshal Get6's six by-reference values even
        // when Add3 has committed the property.  Keep Get6 as the preferred API,
        // then use the direct-value Get method only to verify that exact write.
        return _comFacade.TryInvoke(customPropertyManager, "Get", name)?.ToString();
    }
}

public static class SolidWorksFakeSuccessGuard
{
    private static readonly string[] RequiredDrawingViews = ["Front", "Top", "Right", "Isometric"];

    private static readonly string[] RequiredDimensionNames =
    [
        "plate_length",
        "plate_width",
        "plate_thickness",
        "hole_diameter",
        "hole_center_distance_x",
        "hole_center_distance_y"
    ];

    private static readonly string[] RequiredTitleBlockProperties =
    [
        "PartName",
        "DrawingNumber",
        "Material",
        "Scale",
        "DrawingDate",
        "Revision"
    ];

    public static string NormalizePlateFinalStatus(
        SolidWorksPlateBuildDiagnostics diagnostics,
        string finalStatus)
    {
        if (!string.Equals(finalStatus, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            return finalStatus;
        }

        var failures = ValidatePlate(diagnostics).ToArray();
        if (failures.Length == 0)
        {
            return finalStatus;
        }

        diagnostics.Issues.AddRange(failures);
        return "Failed";
    }

    public static void RequirePlateArtifactsCanPass(SolidWorksPlateBuildDiagnostics diagnostics)
    {
        var failures = ValidatePlate(diagnostics).ToArray();
        if (failures.Length > 0)
        {
            diagnostics.Issues.AddRange(failures);
            throw new InvalidOperationException(failures[0]);
        }
    }

    public static void NormalizeDrawingReport(SolidWorksDrawingReport report) =>
        NormalizeReport(report, ValidateDrawing(report), "drawing_fake_success_rejected");

    public static void NormalizeDimensionReport(SolidWorksDrawingDimensionReport report) =>
        NormalizeReport(report, ValidateDimension(report), "dimension_fake_success_rejected");

    public static void NormalizeTitleBlockReport(SolidWorksDrawingTitleBlockReport report) =>
        NormalizeReport(report, ValidateTitleBlock(report), "title_block_fake_success_rejected");

    public static void RequireDrawingCanPass(SolidWorksDrawingReport report)
    {
        var failures = ValidateDrawing(report).ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidOperationException(failures[0]);
        }
    }

    public static void RequireDimensionCanPass(SolidWorksDrawingDimensionReport report)
    {
        var failures = ValidateDimension(report).ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidOperationException(failures[0]);
        }
    }

    public static void RequireTitleBlockCanPass(SolidWorksDrawingTitleBlockReport report)
    {
        var failures = ValidateTitleBlock(report).ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidOperationException(failures[0]);
        }
    }

    private static IEnumerable<string> ValidatePlate(SolidWorksPlateBuildDiagnostics diagnostics)
    {
        if (!diagnostics.OperationsExecuted.Any(operation =>
                operation.Contains("cut_holes_success", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "cut_holes_failed: FeatureCut returned null or no cut feature success was recorded.";
        }

        if (!diagnostics.SldprtSaveSuccess || diagnostics.SldprtSizeBytes <= 0)
        {
            yield return "sldprt_save_failed: SLDPRT file is missing or empty.";
        }

        if (!diagnostics.StepExportSuccess || diagnostics.StepSizeBytes <= 0)
        {
            yield return "step_export_failed: STEP file is missing or empty.";
        }
    }

    private static IEnumerable<string> ValidateDrawing(SolidWorksDrawingReport report)
    {
        if (!report.DrawingCreated)
        {
            yield return "drawing_document_create_failed: drawing document was not created.";
        }

        foreach (var view in RequiredDrawingViews)
        {
            if (!report.ViewsCreated.Contains(view, StringComparer.OrdinalIgnoreCase))
            {
                yield return $"{view.ToLowerInvariant()}_view_create_failed: drawing view was not created.";
            }
        }

        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            yield return "slddrw_save_failed: SLDDRW file is missing or empty.";
        }

        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            yield return "pdf_export_failed: PDF file is missing or empty.";
        }
    }

    private static IEnumerable<string> ValidateDimension(SolidWorksDrawingDimensionReport report)
    {
        if (!report.DrawingOpened)
        {
            yield return "source_drawing_open_failed: drawing was not opened.";
        }

        foreach (var view in RequiredDrawingViews)
        {
            if (!report.ViewsConfirmed.Contains(view, StringComparer.OrdinalIgnoreCase))
            {
                yield return "drawing_view_missing: required drawing view was not confirmed.";
            }
        }

        if (report.ViewsConfirmedByPositionFallback)
        {
            yield return "drawing_view_missing: required drawing views were inferred only by position fallback.";
        }

        foreach (var name in RequiredDimensionNames)
        {
            var dimension = report.Dimensions.FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (dimension is null)
            {
                yield return $"{name}_dimension_failed: required dimension was not attempted.";
                continue;
            }

            var failed = !dimension.Attempted ||
                         !dimension.Success ||
                         !dimension.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase);
            if (failed)
            {
                yield return $"{dimension.FailureStage}: dimension {name} failed attempted/success/status validation.";
            }

            if (failed && string.IsNullOrWhiteSpace(dimension.FailureReason))
            {
                yield return $"{dimension.FailureStage}: failed dimension {name} must provide failure_reason.";
            }
        }

        if (report.Dimensions.Count > 0 &&
            report.Dimensions.All(dimension => !dimension.Success ||
                                               !dimension.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase)))
        {
            yield return "drawing_dimension_api_evidence_insufficient: all required dimensions failed.";
        }

        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            yield return "dimension_save_failed: SLDDRW file is missing or empty.";
        }

        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            yield return "dimension_pdf_export_failed: PDF file is missing or empty.";
        }
    }

    private static IEnumerable<string> ValidateTitleBlock(SolidWorksDrawingTitleBlockReport report)
    {
        if (!report.DrawingOpened)
        {
            yield return "source_drawing_open_failed: drawing was not opened.";
        }

        if (!report.CustomPropertiesWritten)
        {
            yield return "custom_property_write_failed: custom properties were not written.";
        }

        foreach (var name in RequiredTitleBlockProperties)
        {
            var property = report.Properties.FirstOrDefault(item =>
                item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property is null ||
                !property.Status.Equals("Passed", StringComparison.OrdinalIgnoreCase))
            {
                yield return "custom_property_write_failed: title block property readback failed.";
            }
        }

        if (!report.SlddrwExists || report.SlddrwSizeBytes <= 0)
        {
            yield return "title_block_save_failed: SLDDRW file is missing or empty.";
        }

        if (!report.PdfExists || report.PdfSizeBytes <= 0)
        {
            yield return "title_block_pdf_export_failed: PDF file is missing or empty.";
        }
    }

    private static void NormalizeReport<TReport>(
        TReport report,
        IEnumerable<string> failures,
        string fallbackStage)
        where TReport : class
    {
        var finalStatus = report switch
        {
            SolidWorksDrawingReport drawingReport => drawingReport.FinalStatus,
            SolidWorksDrawingDimensionReport dimensionReport => dimensionReport.FinalStatus,
            SolidWorksDrawingTitleBlockReport titleBlockReport => titleBlockReport.FinalStatus,
            _ => "Failed"
        };

        if (!string.Equals(finalStatus, "Passed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var failureList = failures.ToArray();
        if (failureList.Length == 0)
        {
            return;
        }

        switch (report)
        {
            case SolidWorksDrawingReport drawingReport:
                drawingReport.FinalStatus = "Failed";
                drawingReport.FailureStage ??= ResolveStage(failureList[0], fallbackStage);
                drawingReport.Errors.AddRange(failureList);
                break;
            case SolidWorksDrawingDimensionReport dimensionReport:
                dimensionReport.FinalStatus = "Failed";
                dimensionReport.FailureStage ??= ResolveStage(failureList[0], fallbackStage);
                dimensionReport.Errors.AddRange(failureList);
                break;
            case SolidWorksDrawingTitleBlockReport titleBlockReport:
                titleBlockReport.FinalStatus = "Failed";
                titleBlockReport.FailureStage ??= ResolveStage(failureList[0], fallbackStage);
                titleBlockReport.Errors.AddRange(failureList);
                break;
        }
    }

    private static string ResolveStage(string failure, string fallbackStage)
    {
        var separator = failure.IndexOf(':', StringComparison.Ordinal);
        return separator > 0 ? failure[..separator] : fallbackStage;
    }
}
