using System.Text.Json;
using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Validators;

/// <summary>
/// QualityGate-facing report validator. It never trusts a successful COM call
/// or a non-empty file: the V2.0-D geometry report itself must be complete and
/// report a passed result before an artifact can be released.
/// </summary>
public sealed class GeometryValidationArtifactValidator
{
    public GeometryValidationArtifactValidationResult Validate(SolidWorksWorkerResult workerResult)
    {
        ArgumentNullException.ThrowIfNull(workerResult);
        var reportArtifact = workerResult.GeneratedArtifacts.FirstOrDefault(artifact =>
            artifact.ArtifactType.Equals("GeometryValidationReport", StringComparison.OrdinalIgnoreCase) ||
            artifact.FilePath.EndsWith("geometry_validation_report.json", StringComparison.OrdinalIgnoreCase));
        if (reportArtifact is null)
        {
            return Failed(
                PartFamilyFailureStages.GeometryReportFailed,
                "geometry_report_failed: geometry_validation_report.json was not returned by the controlled Worker.");
        }

        if (!reportArtifact.Exists || reportArtifact.SizeBytes <= 0 || !File.Exists(reportArtifact.FilePath))
        {
            return Failed(
                PartFamilyFailureStages.GeometryReportFailed,
                "geometry_report_failed: geometry_validation_report.json is missing or empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
            var root = document.RootElement;
            var required = new[]
            {
                "model_id",
                "input_parameters",
                "measured_geometry",
                "expected_geometry",
                "deviations",
                "passed_checks",
                "failed_checks",
                "failure_stage",
                "final_status"
            };
            var missing = required.Where(name => !root.TryGetProperty(name, out _)).ToArray();
            if (missing.Length > 0)
            {
                return Failed(
                    PartFamilyFailureStages.GeometryReportFailed,
                    $"geometry_report_failed: required fields are missing: {string.Join(", ", missing)}.");
            }

            var status = ReadString(root, "final_status");
            var failureStage = ReadString(root, "failure_stage");
            var failedChecks = root.GetProperty("failed_checks");
            var passedChecks = root.GetProperty("passed_checks");
            var measured = root.GetProperty("measured_geometry");
            if (!string.Equals(status, "Passed", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(failureStage) ||
                failedChecks.ValueKind != JsonValueKind.Array ||
                failedChecks.GetArrayLength() != 0 ||
                passedChecks.ValueKind != JsonValueKind.Array ||
                passedChecks.GetArrayLength() == 0 ||
                measured.ValueKind != JsonValueKind.Object)
            {
                return Failed(
                    string.IsNullOrWhiteSpace(failureStage)
                        ? PartFamilyFailureStages.GeometryReportFailed
                        : failureStage,
                    "geometry_validation_report.json does not contain a completed passed geometry validation result.",
                    reportArtifact.FilePath);
            }

            return new GeometryValidationArtifactValidationResult(
                new ReviewReport(
                    $"geometry-validation-artifact-review-{Guid.NewGuid():N}",
                    "geometry-validation-artifact-validator",
                    true,
                    0.98,
                    Array.Empty<string>(),
                    RequiresHumanApproval: false,
                    HasFatalError: false),
                null,
                reportArtifact.FilePath);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Failed(
                PartFamilyFailureStages.GeometryReportFailed,
                $"geometry_report_failed: geometry_validation_report.json cannot be parsed: {exception.GetBaseException().Message}",
                reportArtifact.FilePath);
        }
    }

    private static GeometryValidationArtifactValidationResult Failed(
        string failureStage,
        string issue,
        string? reportPath = null) =>
        new(
            new ReviewReport(
                $"geometry-validation-artifact-review-{Guid.NewGuid():N}",
                "geometry-validation-artifact-validator",
                false,
                0.0,
                [issue],
                RequiresHumanApproval: false,
                HasFatalError: true),
            failureStage,
            reportPath);

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}

public sealed record GeometryValidationArtifactValidationResult(
    ReviewReport ReviewReport,
    string? FailureStage,
    string? ReportPath)
{
    public bool IsPassed => ReviewReport.IsPassed && string.IsNullOrWhiteSpace(FailureStage);
}
