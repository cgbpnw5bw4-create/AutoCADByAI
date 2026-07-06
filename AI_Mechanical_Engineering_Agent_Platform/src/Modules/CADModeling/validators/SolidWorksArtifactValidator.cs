using DomainSchemas;
using QualityGate;
using System.Text.Json;

namespace PlatformCore.Modules.CADModeling.Validators;

public sealed class SolidWorksArtifactValidator : IValidator
{
    private readonly string? _configuredOutputRoot;

    public SolidWorksArtifactValidator(string? configuredOutputRoot = null)
    {
        _configuredOutputRoot = string.IsNullOrWhiteSpace(configuredOutputRoot)
            ? null
            : NormalizeDirectory(configuredOutputRoot);
    }

    public string Name => "solidworks-artifact-validator";

    public ReviewReport Validate(object payload)
    {
        var issues = new List<string>();
        if (payload is not SolidWorksWorkerResult result)
        {
            issues.Add("payload must be SolidWorksWorkerResult.");
            return Report(issues);
        }

        var isFakeMode = string.Equals(result.ExecutionMode, "Fake", StringComparison.OrdinalIgnoreCase);
        var isRealPlateBuildMode = string.Equals(result.ExecutionMode, "RealBuildPlateBasic4Holes", StringComparison.OrdinalIgnoreCase);
        var isRealDrawingMode = string.Equals(result.ExecutionMode, "RealDrawingBasicViews", StringComparison.OrdinalIgnoreCase);
        var isRealDrawingDimensionMode = string.Equals(result.ExecutionMode, "RealDrawingDimensions", StringComparison.OrdinalIgnoreCase);
        var isRealDrawingTitleBlockMode = string.Equals(result.ExecutionMode, "RealDrawingTitleBlock", StringComparison.OrdinalIgnoreCase);

        if (!isFakeMode && !isRealPlateBuildMode && !isRealDrawingMode && !isRealDrawingDimensionMode && !isRealDrawingTitleBlockMode)
        {
            issues.Add("execution_mode must be Fake, RealBuildPlateBasic4Holes, RealDrawingBasicViews, RealDrawingDimensions, or RealDrawingTitleBlock.");
        }

        if (result.GeneratedArtifacts.Count == 0)
        {
            issues.Add("generated_artifacts must not be empty.");
        }

        if (isFakeMode)
        {
            ValidateFakeMode(result, issues);
        }
        else if (isRealPlateBuildMode)
        {
            ValidateRealPlateBuildMode(result, issues);
        }
        else if (isRealDrawingMode)
        {
            ValidateRealDrawingMode(result, issues);
        }
        else if (isRealDrawingDimensionMode)
        {
            ValidateRealDrawingDimensionMode(result, issues);
        }
        else if (isRealDrawingTitleBlockMode)
        {
            ValidateRealDrawingTitleBlockMode(result, issues);
        }

        return Report(issues);
    }

    private void ValidateFakeMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must remain false in Fake mode.");
        }

        var hasBuildReport = false;
        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!File.Exists(fullPath))
            {
                issues.Add($"fake artifact does not exist: {artifact.FilePath}.");
                continue;
            }

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length <= 0)
            {
                issues.Add($"fake artifact is empty: {artifact.FilePath}.");
            }

            if (_configuredOutputRoot is not null)
            {
                if (!IsUnderRoot(fullPath, _configuredOutputRoot))
                {
                    issues.Add($"fake artifact path must be under configured output/solidworks root: {artifact.FilePath}.");
                }
            }
            else if (!IsUnderOutputSolidWorksSegment(fullPath))
            {
                issues.Add($"fake artifact path must be under output/solidworks: {artifact.FilePath}.");
            }

            if (Path.GetFileName(fullPath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase))
            {
                hasBuildReport = true;
            }
        }

        if (!hasBuildReport)
        {
            issues.Add("build_report.json was not generated.");
        }
    }

    private void ValidateRealPlateBuildMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealBuildPlateBasic4Holes.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealBuildPlateBasic4Holes.");
        }

        var part = FindArtifact(result, ".SLDPRT");
        var step = FindArtifact(result, ".STEP");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(part, ".SLDPRT", issues);
        ValidateRealArtifact(step, ".STEP", issues);
        ValidateRealArtifact(report, "build_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealBuildReport(report.FilePath, issues);
        }
    }

    private void ValidateRealDrawingMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingBasicViews.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingBasicViews.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("drawing_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "drawing_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingReport(report.FilePath, issues);
        }
    }

    private void ValidateRealDrawingDimensionMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingDimensions.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingDimensions.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("dimension_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "dimension_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing dimension artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingDimensionReport(report.FilePath, issues);
        }
    }

    private void ValidateRealDrawingTitleBlockMode(SolidWorksWorkerResult result, List<string> issues)
    {
        if (!result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must be true for RealDrawingTitleBlock.");
        }

        if (!result.RealCadConnected)
        {
            issues.Add("real_cad_connected must be true for RealDrawingTitleBlock.");
        }

        var drawing = FindArtifact(result, ".SLDDRW");
        var pdf = FindArtifact(result, ".pdf");
        var report = result.GeneratedArtifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("title_block_report.json", StringComparison.OrdinalIgnoreCase));

        ValidateRealArtifact(drawing, ".SLDDRW", issues);
        ValidateRealArtifact(pdf, ".pdf", issues);
        ValidateRealArtifact(report, "title_block_report.json", issues);

        foreach (var artifact in result.GeneratedArtifacts)
        {
            var fullPath = Path.GetFullPath(artifact.FilePath);
            if (!IsUnderRealArtifactRoot(fullPath))
            {
                issues.Add($"real drawing title block artifact path must be under output/solidworks/real: {artifact.FilePath}.");
            }
        }

        if (report is not null && File.Exists(report.FilePath))
        {
            ValidateRealDrawingTitleBlockReport(report.FilePath, issues);
        }
    }

    private static SolidWorksArtifact? FindArtifact(SolidWorksWorkerResult result, string extension) =>
        result.GeneratedArtifacts.FirstOrDefault(artifact =>
            string.Equals(artifact.ExpectedExtension, extension, StringComparison.OrdinalIgnoreCase) ||
            artifact.FilePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static void ValidateRealArtifact(SolidWorksArtifact? artifact, string name, List<string> issues)
    {
        if (artifact is null)
        {
            issues.Add($"{name} artifact was not generated.");
            return;
        }

        var fullPath = Path.GetFullPath(artifact.FilePath);
        if (!File.Exists(fullPath))
        {
            issues.Add($"{name} artifact does not exist: {artifact.FilePath}.");
            return;
        }

        if (new FileInfo(fullPath).Length <= 0)
        {
            issues.Add($"{name} artifact is empty: {artifact.FilePath}.");
        }
    }

    private static void ValidateRealBuildReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("real_cad_executed", out var executed) ||
                executed.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report real_cad_executed must be true.");
            }

            if (!root.TryGetProperty("execution_mode", out var executionMode) ||
                !string.Equals(executionMode.GetString(), "RealBuildPlateBasic4Holes", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("build_report execution_mode must be RealBuildPlateBasic4Holes.");
            }

            if (!root.TryGetProperty("sldprt_save_success", out var sldprtSaveSuccess) ||
                sldprtSaveSuccess.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report sldprt_save_success must be true.");
            }

            if (!root.TryGetProperty("step_export_success", out var stepExportSuccess) ||
                stepExportSuccess.ValueKind != JsonValueKind.True)
            {
                issues.Add("build_report step_export_success must be true.");
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"build_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateRealDrawingReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("drawing_created", out var drawingCreated) ||
                drawingCreated.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report drawing_created must be true.");
            }

            if (!root.TryGetProperty("slddrw_exists", out var slddrwExists) ||
                slddrwExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report slddrw_exists must be true.");
            }

            if (!root.TryGetProperty("pdf_exists", out var pdfExists) ||
                pdfExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("drawing_report pdf_exists must be true.");
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("drawing_report final_status must be Passed.");
            }

            if (!root.TryGetProperty("views_created", out var views) ||
                views.ValueKind != JsonValueKind.Array)
            {
                issues.Add("drawing_report views_created must be present.");
                return;
            }

            var viewNames = views.EnumerateArray()
                .Select(view => view.GetString())
                .Where(view => !string.IsNullOrWhiteSpace(view))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var expectedView in new[] { "Front", "Top", "Right", "Isometric" })
            {
                if (!viewNames.Contains(expectedView))
                {
                    issues.Add($"drawing_report missing view: {expectedView}.");
                }
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"drawing_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateRealDrawingDimensionReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("drawing_opened", out var drawingOpened) ||
                drawingOpened.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report drawing_opened must be true.");
            }

            if (!root.TryGetProperty("slddrw_exists", out var slddrwExists) ||
                slddrwExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report slddrw_exists must be true.");
            }

            if (!root.TryGetProperty("pdf_exists", out var pdfExists) ||
                pdfExists.ValueKind != JsonValueKind.True)
            {
                issues.Add("dimension_report pdf_exists must be true.");
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("dimension_report final_status must be Passed.");
            }

            foreach (var flagName in new[]
            {
                "length_dimension_added",
                "width_dimension_added",
                "thickness_dimension_added",
                "hole_diameter_dimension_added",
                "hole_position_dimension_added"
            })
            {
                if (!root.TryGetProperty(flagName, out var flag) || flag.ValueKind != JsonValueKind.True)
                {
                    issues.Add($"dimension_report {flagName} must be true.");
                }
            }

            if (!root.TryGetProperty("views_confirmed", out var views) ||
                views.ValueKind != JsonValueKind.Array)
            {
                issues.Add("dimension_report views_confirmed must be present.");
                return;
            }

            var viewNames = views.EnumerateArray()
                .Select(view => view.GetString())
                .Where(view => !string.IsNullOrWhiteSpace(view))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var expectedView in new[] { "Front", "Top", "Right", "Isometric" })
            {
                if (!viewNames.Contains(expectedView))
                {
                    issues.Add($"dimension_report missing confirmed view: {expectedView}.");
                }
            }

            if (!root.TryGetProperty("dimensions", out var dimensions) ||
                dimensions.ValueKind != JsonValueKind.Array ||
                dimensions.GetArrayLength() < 5)
            {
                issues.Add("dimension_report dimensions must include the required basic dimensions.");
                return;
            }

            var failedDimension = dimensions.EnumerateArray().FirstOrDefault(dimension =>
                !dimension.TryGetProperty("status", out var status) ||
                !string.Equals(status.GetString(), "Passed", StringComparison.OrdinalIgnoreCase));
            if (failedDimension.ValueKind != JsonValueKind.Undefined)
            {
                issues.Add("dimension_report dimensions must all have status Passed.");
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"dimension_report.json is invalid: {ex.Message}.");
        }
    }

    private static void ValidateRealDrawingTitleBlockReport(string reportPath, List<string> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            foreach (var flagName in new[]
            {
                "drawing_opened",
                "title_block_template_detected",
                "drawing_properties_read",
                "custom_properties_written",
                "title_block_updated",
                "slddrw_exists",
                "pdf_exists"
            })
            {
                if (!root.TryGetProperty(flagName, out var flag) || flag.ValueKind != JsonValueKind.True)
                {
                    issues.Add($"title_block_report {flagName} must be true.");
                }
            }

            if (!root.TryGetProperty("final_status", out var finalStatus) ||
                !string.Equals(finalStatus.GetString(), "Passed", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("title_block_report final_status must be Passed.");
            }

            if (!root.TryGetProperty("title_block_population_strategy", out var populationStrategy) ||
                !string.Equals(populationStrategy.GetString(), "custom_properties_only", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add("title_block_report title_block_population_strategy must be custom_properties_only.");
            }

            if (!root.TryGetProperty("title_block_fields_verified_in_sheet_format", out var fieldsVerified) ||
                fieldsVerified.ValueKind != JsonValueKind.False)
            {
                issues.Add("title_block_report title_block_fields_verified_in_sheet_format must be false until sheet format note rendering is verified.");
            }

            var expectedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["part_name"] = "plate_basic_4holes",
                ["drawing_number"] = "PLATE-BASIC-4HOLES",
                ["revision"] = "A"
            };

            foreach (var expected in expectedFields)
            {
                if (!root.TryGetProperty(expected.Key, out var field) ||
                    !string.Equals(field.GetString(), expected.Value, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"title_block_report {expected.Key} must be {expected.Value}.");
                }
            }

            foreach (var requiredField in new[] { "material", "scale", "drawing_date" })
            {
                if (!root.TryGetProperty(requiredField, out var field) ||
                    string.IsNullOrWhiteSpace(field.GetString()))
                {
                    issues.Add($"title_block_report {requiredField} must be present.");
                }
            }

            if (!root.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Array ||
                properties.GetArrayLength() < 6)
            {
                issues.Add("title_block_report properties must include the required title block fields.");
                return;
            }

            var failedProperty = properties.EnumerateArray().FirstOrDefault(property =>
                !property.TryGetProperty("status", out var status) ||
                !string.Equals(status.GetString(), "Passed", StringComparison.OrdinalIgnoreCase));
            if (failedProperty.ValueKind != JsonValueKind.Undefined)
            {
                issues.Add("title_block_report properties must all have status Passed.");
            }
        }
        catch (JsonException ex)
        {
            issues.Add($"title_block_report.json is invalid: {ex.Message}.");
        }
    }

    private static ReviewReport Report(IReadOnlyList<string> issues) =>
        new(
            $"solidworks-artifact-validation-{Guid.NewGuid():N}",
            "solidworks-artifact-validator",
            issues.Count == 0,
            issues.Count == 0 ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: issues.Any(issue =>
                issue.Contains("real_cad_executed", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("execution_mode", StringComparison.OrdinalIgnoreCase)));

    private static bool IsUnderOutputSolidWorksSegment(string fullPath)
    {
        var directory = new FileInfo(fullPath).Directory;
        while (directory is not null)
        {
            if (directory.Name.Equals("solidworks", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("output", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private bool IsUnderRealArtifactRoot(string fullPath)
    {
        if (_configuredOutputRoot is not null)
        {
            var configuredRoot = _configuredOutputRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var realRoot = Path.GetFileName(configuredRoot).Equals("real", StringComparison.OrdinalIgnoreCase)
                ? NormalizeDirectory(configuredRoot)
                : NormalizeDirectory(Path.Combine(configuredRoot, "real"));

            return IsUnderRoot(fullPath, realRoot);
        }

        return IsUnderOutputSolidWorksRealSegment(fullPath);
    }

    private static bool IsUnderOutputSolidWorksRealSegment(string fullPath)
    {
        var directory = new FileInfo(fullPath).Directory;
        while (directory is not null)
        {
            if (directory.Name.Equals("real", StringComparison.OrdinalIgnoreCase) &&
                directory.Parent?.Name.Equals("solidworks", StringComparison.OrdinalIgnoreCase) == true &&
                directory.Parent.Parent?.Name.Equals("output", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        var normalizedPath = Path.GetFullPath(fullPath);
        return normalizedPath.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
        Path.DirectorySeparatorChar;
}
