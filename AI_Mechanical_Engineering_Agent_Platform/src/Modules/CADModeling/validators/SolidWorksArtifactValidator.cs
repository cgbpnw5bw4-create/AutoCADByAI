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

        if (!isFakeMode && !isRealPlateBuildMode)
        {
            issues.Add("execution_mode must be Fake or RealBuildPlateBasic4Holes.");
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
