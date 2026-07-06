using DomainSchemas;
using SolidWorksWorker;

namespace SolidWorksDrawingDimensionSmokeRunner;

public sealed class SolidWorksDrawingDimensionDiagnosticRunner
{
    public async Task<SolidWorksDrawingDimensionReport> RunAsync(
        DrawingDimensionRunnerOptions runnerOptions,
        CancellationToken cancellationToken)
    {
        var outputDirectory = ResolveOutputDirectory(runnerOptions.OutputRoot);
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "dimension_report.json");
        var report = new SolidWorksDrawingDimensionReport
        {
            OutputDirectory = outputDirectory,
            SourceDrawingPath = runnerOptions.SourceDrawingPath,
            SlddrwPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.SLDDRW"),
            PdfPath = Path.Combine(outputDirectory, "plate_basic_4holes_dimensioned.pdf")
        };

        if (string.IsNullOrWhiteSpace(runnerOptions.SourceDrawingPath) || !File.Exists(runnerOptions.SourceDrawingPath))
        {
            report.FailureStage = "source_drawing_missing";
            report.Errors.Add($"source_drawing_missing: {runnerOptions.SourceDrawingPath ?? "<null>"}");
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingDimensionReportWriter.WriteAsync(reportPath, report, cancellationToken);
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
            report.FailureStage = "solidworks_connection_failed";
            report.Errors.AddRange(connection.Issues);
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingDimensionReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }

        try
        {
            var request = new SolidWorksWorkerRequest(
                $"drawing-dimension-runner-{Guid.NewGuid():N}",
                CreatePlan(),
                outputDirectory,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingDimensionSmokeTestOnly: true,
                SourceDrawingPath: Path.GetFullPath(runnerOptions.SourceDrawingPath));

            var result = await sessionManager.ExecuteWithApplicationAsync(
                (application, token) => new LateBoundSolidWorksDrawingDimensionBuilder().CreateDimensionedDrawingAsync(
                    application,
                    request,
                    options,
                    connection.SolidWorksVersion,
                    token),
                cancellationToken);

            var generatedReport = result.GeneratedArtifacts.FirstOrDefault(artifact =>
                artifact.FilePath.EndsWith("dimension_report.json", StringComparison.OrdinalIgnoreCase));
            if (generatedReport is not null && File.Exists(generatedReport.FilePath))
            {
                return System.Text.Json.JsonSerializer.Deserialize<SolidWorksDrawingDimensionReport>(
                           await File.ReadAllTextAsync(generatedReport.FilePath, cancellationToken),
                           new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower })
                       ?? report;
            }

            report.FailureStage = "dimension_report_write_failed";
            report.Errors.AddRange(result.Issues.DefaultIfEmpty("dimension_report_write_failed: builder did not return dimension_report.json."));
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingDimensionReportWriter.WriteAsync(reportPath, report, cancellationToken);
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
            ? Path.Combine("output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions")
            : outputRoot);
        return Path.Combine(root, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

    private static SolidWorksBuildPlan CreatePlan() =>
        new(
            $"drawing-dimension-plan-{Guid.NewGuid():N}",
            "cad-model-spec-plate-basic-4holes",
            "SolidWorks",
            "plate_basic_4holes",
            "mm",
            new[]
            {
                new SolidWorksOperation(
                    "op-create-sketch-base",
                    "CreateSketch",
                    "standard-plane",
                    new Dictionary<string, string>
                    {
                        ["length_mm"] = "160",
                        ["width_mm"] = "80"
                    },
                    Array.Empty<string>(),
                    "创建 160 x 80 mm 板件草图。"),
                new SolidWorksOperation(
                    "op-extrude-base",
                    "ExtrudeBoss",
                    "standard-plane",
                    new Dictionary<string, string> { ["depth_mm"] = "12" },
                    new[] { "op-create-sketch-base" },
                    "拉伸 12 mm 板厚。"),
                new SolidWorksOperation(
                    "op-cut-holes",
                    "CutExtrude",
                    "standard-plane",
                    new Dictionary<string, string>
                    {
                        ["hole_diameter_mm"] = "10",
                        ["hole_count"] = "4"
                    },
                    new[] { "op-extrude-base" },
                    "切除四个直径 10 mm 通孔。")
            },
            new[]
            {
                new SolidWorksArtifact(
                    "expected-dimensioned-drawing",
                    "Drawing",
                    "plate_basic_4holes_dimensioned.SLDDRW",
                    ".SLDDRW",
                    false,
                    0,
                    "预期带尺寸真实工程图文件。"),
                new SolidWorksArtifact(
                    "expected-dimensioned-pdf",
                    "Pdf",
                    "plate_basic_4holes_dimensioned.pdf",
                    ".pdf",
                    false,
                    0,
                    "预期带尺寸工程图 PDF。"),
                new SolidWorksArtifact(
                    "expected-dimension-report",
                    "DimensionReport",
                    "dimension_report.json",
                    ".json",
                    false,
                    0,
                    "预期基础尺寸诊断报告。")
            },
            new[] { "drawing dimension runner source drawing must already exist" },
            new[] { "V1.2 只验证基础尺寸标注，不做 BOM、标题栏、复杂公差或 V1.3 内容。" });
}
