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

public static class SolidWorksPlateBuildOutput
{
    public const string ExecutionMode = "RealBuildPlateBasic4Holes";

    public static string ResolveOutputDirectory(SolidWorksWorkerRequest request, SolidWorksRuntimeOptions options)
    {
        var requestedRoot = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? options.OutputDirectory
            : request.OutputDirectory;
        var fullRoot = Path.GetFullPath(requestedRoot);
        var directoryName = Path.GetFileName(fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var target = directoryName.Equals("plate_basic_4holes", StringComparison.OrdinalIgnoreCase)
            ? fullRoot
            : Path.Combine(fullRoot, "plate_basic_4holes");

        if (!Directory.Exists(target) || Directory.GetFileSystemEntries(target).Length == 0)
        {
            return target;
        }

        return Path.Combine(
            Path.GetDirectoryName(target) ?? fullRoot,
            $"plate_basic_4holes_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

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
                operationsExecuted,
                issues,
                warnings,
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
                operationsExecuted,
                issues,
                warnings,
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
        IReadOnlyList<string> operationsExecuted,
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings,
        string finalStatus)
    {
        var now = DateTimeOffset.UtcNow;
        var report = new
        {
            build_id = $"solidworks-real-build-{Guid.NewGuid():N}",
            build_plan_id = request.BuildPlan.PlanId,
            execution_mode = executionMode,
            real_cad_executed = realCadExecuted,
            real_cad_connected = realCadConnected,
            solidworks_version = solidWorksVersion,
            started_at = now,
            completed_at = now,
            output_directory = Path.GetFullPath(outputDirectory),
            generated_artifacts = generatedArtifactPaths.Select(Path.GetFullPath).ToArray(),
            operations_executed = operationsExecuted,
            issues,
            warnings,
            final_status = finalStatus
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}

public sealed class LateBoundSolidWorksPlateBuilder : ISolidWorksPlateBuilder
{
    private const double MmToMeters = 0.001;

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

    private static SolidWorksPlateBuildResult BuildCore(
        object application,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        string? solidWorksVersion,
        CancellationToken cancellationToken)
    {
        var logs = new List<string>();
        var issues = new List<string>();
        var warnings = new List<string>();
        var operations = new List<string>();
        object? model = null;

        var outputDirectory = SolidWorksPlateBuildOutput.ResolveOutputDirectory(request, options);
        var partPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDPRT");
        var stepPath = Path.Combine(outputDirectory, "plate_basic_4holes.STEP");
        var reportPath = Path.Combine(outputDirectory, "build_report.json");

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var dimensions = SolidWorksPlateDimensions.FromPlan(request.BuildPlan);
            if (dimensions is null)
            {
                issues.Add("invalid_plate_basic_4holes_dimensions: BuildPlan parameters are missing or invalid.");
                return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, issues, warnings, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(options.TemplatePartPath) || !File.Exists(options.TemplatePartPath))
            {
                issues.Add("template_part_path_required_for_real_build: SW_TEMPLATE_PART_PATH must point to an existing part template.");
                return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, issues, warnings, cancellationToken);
            }

            logs.Add("Connecting SolidWorks session was provided by SolidWorksSessionManager.");
            model = Invoke(application, "NewDocument", options.TemplatePartPath, 0, 0d, 0d);
            if (model is null)
            {
                issues.Add("solidworks_new_document_failed: NewDocument returned null.");
                return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, issues, warnings, cancellationToken);
            }

            operations.Add("NewDocument");
            SelectPlane(model, "Top Plane", "上视基准面", logs);
            EnterSketch(model);
            Invoke(GetProperty(model, "SketchManager"), "CreateCenterRectangle", 0d, 0d, 0d, dimensions.LengthMeters / 2, dimensions.WidthMeters / 2, 0d);
            ExitSketch(model);
            operations.Add("CreateSketch: base rectangle");

            var featureManager = GetProperty(model, "FeatureManager");
            Invoke(
                featureManager,
                "FeatureExtrusion2",
                true, false, false, 0, 0, dimensions.ThicknessMeters, 0d, false, false, false, false,
                0d, 0d, false, false, false, false, true, true, true, 0, 0d, false);
            operations.Add("ExtrudeBoss: plate thickness");

            SelectPlane(model, "Top Plane", "上视基准面", logs);
            EnterSketch(model);
            foreach (var (x, y) in dimensions.HoleCentersMeters())
            {
                Invoke(GetProperty(model, "SketchManager"), "CreateCircleByRadius", x, y, 0d, dimensions.HoleRadiusMeters);
            }

            ExitSketch(model);
            operations.Add("CreateSketch: four hole circles");
            Invoke(
                featureManager,
                "FeatureCut4",
                true, false, false, 1, 0, dimensions.ThicknessMeters * 2, 0d, false, false, false, false,
                0d, 0d, false, false, false, false, false, true, true, true, true, false, 0, 0d,
                false, false, false, false, false, false);
            operations.Add("CutExtrude: four through holes");

            ForceRebuild(model, logs);
            SavePart(model, partPath, logs);
            operations.Add("SavePart");
            ActivateDocument(application, model, logs);
            ExportStep(model, stepPath, logs);
            operations.Add("ExportStep");

            SolidWorksPlateBuildReportWriter.Write(
                reportPath,
                request,
                SolidWorksPlateBuildOutput.ExecutionMode,
                realCadExecuted: true,
                realCadConnected: true,
                solidWorksVersion,
                outputDirectory,
                new[] { partPath, stepPath },
                operations,
                issues,
                warnings,
                "Passed");

            return SolidWorksPlateBuildResult.Completed(
                new[]
                {
                    SolidWorksPlateBuildOutput.Artifact("real-part", "Part", partPath, ".SLDPRT", "真实 SolidWorks 零件文件。"),
                    SolidWorksPlateBuildOutput.Artifact("real-step", "Step", stepPath, ".STEP", "真实 STEP 导出文件。"),
                    SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "真实构建报告。")
                },
                logs.Concat(operations.Select(operation => $"operation_executed: {operation}")).ToArray());
        }
        catch (Exception ex) when (ex is COMException or TargetInvocationException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            issues.Add($"solidworks_real_build_failed: {ex.GetBaseException().Message}");
            return FailedWithReport(request, solidWorksVersion, outputDirectory, reportPath, logs, issues, warnings, cancellationToken);
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
        IReadOnlyList<string> issues,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        SolidWorksPlateBuildReportWriter.Write(
            reportPath,
            request,
            SolidWorksPlateBuildOutput.ExecutionMode,
            realCadExecuted: false,
            realCadConnected: true,
            solidWorksVersion,
            outputDirectory,
            Array.Empty<string>(),
            Array.Empty<string>(),
            issues,
            warnings,
            "Failed");

        return SolidWorksPlateBuildResult.Failed(
            logs,
            issues,
            new[]
            {
                SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "真实构建失败报告。")
            });
    }

    private static void SelectPlane(object model, string englishName, string chineseName, List<string> logs)
    {
        var extension = GetProperty(model, "Extension");
        var selected = TryInvokeBool(extension, "SelectByID2", englishName, "PLANE", 0d, 0d, 0d, false, 0, null!, 0) ||
                       TryInvokeBool(extension, "SelectByID2", chineseName, "PLANE", 0d, 0d, 0d, false, 0, null!, 0);
        if (!selected)
        {
            throw new InvalidOperationException($"solidworks_select_plane_failed: {englishName}.");
        }

        logs.Add($"Selected sketch plane: {englishName}.");
    }

    private static void EnterSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    // SolidWorks toggles sketch edit mode when InsertSketch(true) is called again.
    private static void ExitSketch(object model) =>
        Invoke(GetProperty(model, "SketchManager"), "InsertSketch", true);

    private static void ForceRebuild(object model, List<string> logs)
    {
        if (TryInvokeBool(model, "ForceRebuild3", false))
        {
            logs.Add("SolidWorks model rebuilt.");
        }
    }

    private static void SavePart(object model, string partPath, List<string> logs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(partPath)!);
        if (!TryInvokeBool(model, "SaveAs3", partPath, 0, 2) &&
            !TryInvokeBool(model, "SaveAs", partPath))
        {
            var extension = GetProperty(model, "Extension");
            Invoke(extension, "SaveAs", partPath, 0, 1, null!, 0, 0);
        }

        if (!File.Exists(partPath) || new FileInfo(partPath).Length <= 0)
        {
            throw new IOException($"solidworks_part_save_failed: {partPath}");
        }

        logs.Add($"Saved SolidWorks part: {partPath}.");
    }

    private static void ActivateDocument(object application, object model, List<string> logs)
    {
        var title = TryInvoke(model, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            TryInvoke(application, "ActivateDoc3", title, true, 0, 0);
            logs.Add($"Activated SolidWorks document before STEP export: {title}.");
        }
    }

    private static void ExportStep(object model, string stepPath, List<string> logs)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(stepPath)!);
        if (!TryInvokeBool(model, "SaveAs3", stepPath, 0, 2) &&
            !TryInvokeBool(model, "SaveAs", stepPath))
        {
            var extension = GetProperty(model, "Extension");
            Invoke(extension, "SaveAs", stepPath, 0, 1, null!, 0, 0);
        }

        if (!File.Exists(stepPath) || new FileInfo(stepPath).Length <= 0)
        {
            throw new IOException($"solidworks_step_export_failed: {stepPath}");
        }

        logs.Add($"Exported STEP file: {stepPath}.");
    }

    private static object GetProperty(object target, string name) =>
        target.GetType().InvokeMember(
            name,
            BindingFlags.GetProperty,
            binder: null,
            target,
            Array.Empty<object>())
        ?? throw new InvalidOperationException($"solidworks_property_missing: {name}.");

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
