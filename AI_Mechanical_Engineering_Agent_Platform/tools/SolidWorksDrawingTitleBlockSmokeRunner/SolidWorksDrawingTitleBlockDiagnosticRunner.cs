using DomainSchemas;
using SolidWorksWorker;

namespace SolidWorksDrawingTitleBlockSmokeRunner;

public sealed class SolidWorksDrawingTitleBlockDiagnosticRunner
{
    public async Task<SolidWorksDrawingTitleBlockReport> RunAsync(
        DrawingTitleBlockRunnerOptions runnerOptions,
        CancellationToken cancellationToken)
    {
        var outputDirectory = ResolveOutputDirectory(runnerOptions.OutputRoot);
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "title_block_report.json");
        var report = new SolidWorksDrawingTitleBlockReport
        {
            OutputDirectory = outputDirectory,
            SourceDimensionedDrawingPath = runnerOptions.SourceDrawingPath,
            SlddrwPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.SLDDRW"),
            PdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_title_block.pdf")
        };

        if (string.IsNullOrWhiteSpace(runnerOptions.SourceDrawingPath) || !File.Exists(runnerOptions.SourceDrawingPath))
        {
            report.FailureStage = "source_dimensioned_drawing_missing";
            report.Errors.Add($"source_dimensioned_drawing_missing: {runnerOptions.SourceDrawingPath ?? "<null>"}");
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingTitleBlockReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }

        var sessionManager = new SolidWorksSessionManager();
        var options = SolidWorksRuntimeOptions.FromEnvironment() with
        {
            EnableRealExecution = true,
            Visible = runnerOptions.Visible,
            OutputDirectory = Path.GetFullPath(runnerOptions.OutputRoot)
        };

        var connection = await sessionManager.ConnectAsync(options, cancellationToken);
        report.SolidWorksConnected = connection.Connected;
        report.SolidWorksVersion = connection.SolidWorksVersion;
        if (!connection.Connected)
        {
            report.FailureStage = "source_drawing_open_failed";
            report.Errors.AddRange(connection.Issues);
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingTitleBlockReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }

        try
        {
            var request = new SolidWorksWorkerRequest(
                $"drawing-title-block-runner-{Guid.NewGuid():N}",
                CreatePlan(),
                outputDirectory,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingTitleBlockSmokeTestOnly: true,
                SourceDimensionedDrawingPath: Path.GetFullPath(runnerOptions.SourceDrawingPath));

            var result = await sessionManager.ExecuteWithApplicationAsync(
                (application, token) => new LateBoundSolidWorksDrawingTitleBlockBuilder().ApplyTitleBlockAsync(
                    application,
                    request,
                    options,
                    connection.SolidWorksVersion,
                    token),
                cancellationToken);

            var generatedReport = result.GeneratedArtifacts.FirstOrDefault(artifact =>
                artifact.FilePath.EndsWith("title_block_report.json", StringComparison.OrdinalIgnoreCase));
            if (generatedReport is not null && File.Exists(generatedReport.FilePath))
            {
                return System.Text.Json.JsonSerializer.Deserialize<SolidWorksDrawingTitleBlockReport>(
                           await File.ReadAllTextAsync(generatedReport.FilePath, cancellationToken),
                           new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower })
                       ?? report;
            }

            report.FailureStage = "title_block_report_write_failed";
            report.Errors.AddRange(result.Issues.DefaultIfEmpty("title_block_report_write_failed: builder did not return title_block_report.json."));
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingTitleBlockReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }
        finally
        {
            await sessionManager.DisconnectAsync(cancellationToken);
        }
    }

    private static string ResolveOutputDirectory(string outputRoot)
    {
        var root = Path.GetFullPath(string.IsNullOrWhiteSpace(outputRoot)
            ? Path.Combine("output", "solidworks", "real", "plate_basic_4holes_title_block")
            : outputRoot);
        return Path.Combine(root, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

    private static SolidWorksBuildPlan CreatePlan() =>
        new(
            $"drawing-title-block-plan-{Guid.NewGuid():N}",
            "cad-model-spec-plate-basic-4holes",
            "SolidWorks",
            "plate_basic_4holes",
            "mm",
            new[]
            {
                new SolidWorksOperation(
                    "op-title-block-metadata",
                    "UpdateDrawingTitleBlockProperties",
                    "drawing-sheet",
                    new Dictionary<string, string>
                    {
                        ["part_name"] = "plate_basic_4holes",
                        ["drawing_number"] = "PLATE-BASIC-4HOLES",
                        ["material"] = "Q235",
                        ["revision"] = "A"
                    },
                    Array.Empty<string>(),
                    "Update minimal drawing custom properties for the title block.")
            },
            new[]
            {
                new SolidWorksArtifact(
                    "expected-title-block-drawing",
                    "Drawing",
                    "plate_basic_4holes_title_block.SLDDRW",
                    ".SLDDRW",
                    false,
                    0,
                    "Expected SolidWorks drawing with title block metadata."),
                new SolidWorksArtifact(
                    "expected-title-block-pdf",
                    "Pdf",
                    "plate_basic_4holes_title_block.pdf",
                    ".pdf",
                    false,
                    0,
                    "Expected title block drawing PDF."),
                new SolidWorksArtifact(
                    "expected-title-block-report",
                    "TitleBlockReport",
                    "title_block_report.json",
                    ".json",
                    false,
                    0,
                    "Expected title block diagnostic report.")
            },
            new[] { "drawing title block runner source dimensioned drawing must already exist" },
            new[] { "V1.3 validates only minimal drawing title block metadata and does not create BOM, complex templates, tolerances, or V1.4 content." });
}
