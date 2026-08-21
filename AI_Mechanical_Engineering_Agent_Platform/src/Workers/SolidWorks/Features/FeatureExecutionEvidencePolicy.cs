using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// Fail-closed authorization for real generic-feature execution.
/// The evidence revision is recomputed from every source boundary that can
/// authorize, execute, validate, or report a Feature Handler result.
/// </summary>
public static partial class FeatureExecutionEvidencePolicy
{
    private const string RevisionPrefix = "feature-execution-source-sha256:";

    private static readonly string[] BoundSourcePaths =
    [
        "src/Workers/SolidWorks/Features/FeatureHandlerContracts.cs",
        "src/Workers/SolidWorks/Features/FeatureExecutionEvidencePolicy.cs",
        "src/Workers/SolidWorks/Features/FeatureHandlerRegistry.cs",
        "src/Workers/SolidWorks/Features/RealSolidWorksFeatureAdapter.cs",
        "src/Workers/SolidWorks/Features/SolidWorksFeatureGraphPartFamilyBuilder.cs",
        "src/Workers/SolidWorks/Features/Sketch/SketchHandler.cs",
        "src/Workers/SolidWorks/Features/Extrude/ExtrudeBossHandler.cs",
        "src/Workers/SolidWorks/Features/Cut/ExtrudeCutHandler.cs",
        "src/Workers/SolidWorks/Features/Hole/HoleHandler.cs",
        "src/Workers/SolidWorks/SolidWorksPartFamilyRealBuilders.cs",
        "src/Modules/CADModeling/validators/SolidWorksArtifactValidator.cs",
        "tools/SolidWorksFeatureExecutionSmokeRunner/FeatureExecutionSmokeRunner.cs"
    ];

    public static string ComputeCurrentSourceRevision()
    {
        var projectRoot = FindProjectRoot();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relativePath in BoundSourcePaths.Order(StringComparer.Ordinal))
        {
            var fullPath = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Feature evidence source is missing: {relativePath}.",
                    fullPath);
            }

            Append(hash, relativePath.Replace('\\', '/'));
            Append(hash, "\n");
            Append(hash, NormalizeVolatileEvidenceMetadata(File.ReadAllText(fullPath)));
            Append(hash, "\n");
        }

        return RevisionPrefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static FeatureHandlerValidationResult ValidateEvidence(
        IFeatureHandler handler,
        FeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(feature);
        var evidence = handler.ApiEvidence;
        var issues = new List<string>();

        if (!evidence.AllowsRealExecution)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: " +
                $"{feature.FeatureId}/{handler.FeatureType} has incomplete or unverified API evidence.");
        }

        if (!string.Equals(
                evidence.HandlerVersion,
                handler.HandlerVersion,
                StringComparison.Ordinal))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: evidence handler_version=" +
                $"{evidence.HandlerVersion ?? "missing"} does not match {handler.HandlerVersion}.");
        }

        string? currentRevision = null;
        try
        {
            currentRevision = ComputeCurrentSourceRevision();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: current feature execution " +
                $"source revision cannot be computed: {ex.Message}");
        }

        if (currentRevision is not null &&
            !string.Equals(evidence.SourceRevision, currentRevision, StringComparison.Ordinal))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: stale source_revision; " +
                $"expected {currentRevision}, evidence has {evidence.SourceRevision ?? "missing"}.");
        }

        ValidateDiagnostic(handler, evidence, issues);
        return issues.Count == 0
            ? FeatureHandlerValidationResult.Passed()
            : FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.FeatureApiUnverified,
                issues.ToArray());
    }

    public static FeatureHandlerValidationResult ValidateRuntimeVersion(
        IFeatureHandler handler,
        string? actualSolidWorksVersion)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (!string.IsNullOrWhiteSpace(actualSolidWorksVersion) &&
            string.Equals(
                handler.ApiEvidence.SolidWorksVersion,
                actualSolidWorksVersion,
                StringComparison.OrdinalIgnoreCase))
        {
            return FeatureHandlerValidationResult.Passed();
        }

        return FeatureHandlerValidationResult.Failed(
            PartFamilyFailureStages.FeatureApiUnverified,
            $"{PartFamilyFailureStages.FeatureApiUnverified}: " +
            $"{handler.HandlerId}@{handler.HandlerVersion} evidence requires SolidWorks " +
            $"{handler.ApiEvidence.SolidWorksVersion ?? "missing"}, connected runtime is " +
            $"{actualSolidWorksVersion ?? "missing"}.");
    }

    public static string ResolveEvidencePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(
            FindProjectRoot(),
            path.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void ValidateDiagnostic(
        IFeatureHandler handler,
        FeatureApiEvidence evidence,
        List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(evidence.DiagnosticRunPath))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic_run_path is missing.");
            return;
        }

        string diagnosticPath;
        try
        {
            diagnosticPath = ResolveEvidencePath(evidence.DiagnosticRunPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic_run_path is invalid: {ex.Message}");
            return;
        }

        if (!File.Exists(diagnosticPath))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic report does not exist: {diagnosticPath}.");
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(diagnosticPath));
            var root = document.RootElement;
            RequireBoolean(root, "candidate_only", true, issues);
            RequireBoolean(root, "main_workflow_accepted", false, issues);
            RequireBoolean(root, "quality_gate_passed", false, issues);
            RequireBoolean(root, "solid_works_connected", true, issues);
            RequireBoolean(root, "real_cad_executed", true, issues);
            RequireString(root, "final_status", "CandidatePassed", issues);
            RequireString(root, "deliverable_status", "NotDeliverable", issues);
            RequireString(
                root,
                "solid_works_version",
                evidence.SolidWorksVersion ?? string.Empty,
                issues);
            RequirePassedArtifact(root, "model", requireStepContent: false, issues);
            RequirePassedArtifact(root, "step", requireStepContent: true, issues);

            if (!root.TryGetProperty("feature_handler_reports", out var reports) ||
                reports.ValueKind != JsonValueKind.Array)
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic feature_handler_reports is missing.");
                return;
            }

            var matching = reports.EnumerateArray()
                .Where(report =>
                    TryGetString(report, "feature_type", out var featureType) &&
                    string.Equals(featureType, handler.FeatureType, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matching.Length == 0)
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic contains no " +
                    $"{handler.FeatureType} result.");
                return;
            }

            foreach (var report in matching)
            {
                RequireBoolean(report, "result_object_validated", true, issues);
                RequireBoolean(report, "rebuild_passed", true, issues);
                RequireBoolean(report, "geometry_change_validated", true, issues);
                RequireString(
                    report,
                    "adapter_id",
                    RealSolidWorksFeatureAdapter.AdapterIdentifier,
                    issues);
                RequireString(
                    report,
                    "adapter_version",
                    RealSolidWorksFeatureAdapter.CurrentAdapterVersion,
                    issues);
                RequireString(
                    report,
                    "evidence_solid_works_version",
                    evidence.SolidWorksVersion ?? string.Empty,
                    issues);
                RequireString(
                    report,
                    "evidence_source_revision",
                    evidence.SourceRevision ?? string.Empty,
                    issues);
                if (!TryGetString(report, "handler_name", out var handlerName) ||
                    handlerName is null ||
                    !handlerName.Contains(
                        $"{handler.HandlerId}@{handler.HandlerVersion}",
                        StringComparison.Ordinal))
                {
                    issues.Add(
                        $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic handler binding " +
                        $"does not match {handler.HandlerId}@{handler.HandlerVersion}.");
                }

                if (report.TryGetProperty("failure_stage", out var failureStage) &&
                    failureStage.ValueKind != JsonValueKind.Null &&
                    !string.IsNullOrWhiteSpace(failureStage.GetString()))
                {
                    issues.Add(
                        $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic feature contains " +
                        $"failure_stage={failureStage.GetString()}.");
                }

                if (report.TryGetProperty("issues", out var featureIssues) &&
                    featureIssues.ValueKind == JsonValueKind.Array &&
                    featureIssues.GetArrayLength() != 0)
                {
                    issues.Add(
                        $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic feature contains issues.");
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic report cannot be validated: {ex.Message}");
        }
    }

    private static void RequirePassedArtifact(
        JsonElement root,
        string propertyName,
        bool requireStepContent,
        List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var artifact) ||
            artifact.ValueKind != JsonValueKind.Object ||
            !artifact.TryGetProperty("exists", out var exists) ||
            exists.ValueKind != JsonValueKind.True ||
            !artifact.TryGetProperty("size_bytes", out var size) ||
            !size.TryGetInt64(out var sizeBytes) ||
            sizeBytes <= 0 ||
            !TryGetString(artifact, "status", out var status) ||
            !string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase) ||
            !TryGetString(artifact, "file_path", out var path))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic {propertyName} artifact is not Passed and non-empty.");
            return;
        }

        try
        {
            var artifactPath = Path.GetFullPath(path!);
            if (!File.Exists(artifactPath) || new FileInfo(artifactPath).Length != sizeBytes)
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic {propertyName} artifact does not physically match its report.");
            }
            else if (requireStepContent &&
                     !CadArtifactContentValidator.TryValidateStepFile(artifactPath, out var contentIssue))
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic STEP artifact content is invalid: {contentIssue}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic {propertyName} artifact cannot be verified: {exception.Message}");
        }
    }

    private static void RequireBoolean(
        JsonElement root,
        string propertyName,
        bool expected,
        List<string> issues)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != (expected ? JsonValueKind.True : JsonValueKind.False))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic {propertyName} must be {expected.ToString().ToLowerInvariant()}.");
        }
    }

    private static void RequireString(
        JsonElement root,
        string propertyName,
        string expected,
        List<string> issues)
    {
        if (!TryGetString(root, propertyName, out var value) ||
            !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(
                $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic {propertyName} " +
                $"must be {expected}, actual={value ?? "missing"}.");
        }
    }

    private static bool TryGetString(
        JsonElement root,
        string propertyName,
        out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string FindProjectRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AI_Mechanical_Engineering_Agent_Platform.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException(
            "AI_Mechanical_Engineering_Agent_Platform.sln could not be located for evidence verification.");
    }

    private static string NormalizeVolatileEvidenceMetadata(string source)
    {
        var normalized = VolatileNamedArgumentRegex().Replace(
            source,
            match => $"{match.Groups["name"].Value}: \"<evidence-metadata>\"");
        return VolatileAssignmentRegex().Replace(
            normalized,
            match => $"{match.Groups["name"].Value} = \"<evidence-metadata>\"");
    }

    private static void Append(IncrementalHash hash, string text) =>
        hash.AppendData(Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n", StringComparison.Ordinal)));

    [GeneratedRegex(
        "(?<name>EvidenceId|SolidWorksVersion|DiagnosticRunPath|SourceRevision)\\s*:\\s*\"[^\"]*\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex VolatileNamedArgumentRegex();

    [GeneratedRegex(
        "(?<name>EvidenceId|SolidWorksVersion|DiagnosticRunPath|SourceRevision)\\s*=\\s*\"[^\"]*\"",
        RegexOptions.CultureInvariant)]
    private static partial Regex VolatileAssignmentRegex();
}
