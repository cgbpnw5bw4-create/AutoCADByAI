using DomainSchemas;
using QualityGate;

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

        if (result.RealCadExecuted)
        {
            issues.Add("real_cad_executed must remain false.");
        }

        if (!string.Equals(result.ExecutionMode, "Fake", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add("execution_mode must be Fake.");
        }

        if (result.GeneratedArtifacts.Count == 0)
        {
            issues.Add("generated_artifacts must not be empty.");
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

        return Report(issues);
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
