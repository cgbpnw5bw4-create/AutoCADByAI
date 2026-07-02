using DomainSchemas;
using SolidWorksWorker;

namespace SolidWorksDrawingSmokeRunner;

public sealed class SolidWorksDrawingDiagnosticRunner
{
    public async Task<SolidWorksDrawingReport> RunAsync(
        DrawingRunnerOptions runnerOptions,
        CancellationToken cancellationToken)
    {
        var outputDirectory = ResolveOutputDirectory(runnerOptions.OutputRoot);
        Directory.CreateDirectory(outputDirectory);
        var reportPath = Path.Combine(outputDirectory, "drawing_report.json");
        var report = new SolidWorksDrawingReport
        {
            OutputDirectory = outputDirectory,
            SourcePartPath = runnerOptions.SourcePartPath,
            SlddrwPath = Path.Combine(outputDirectory, "plate_basic_4holes.SLDDRW"),
            PdfPath = Path.Combine(outputDirectory, "plate_basic_4holes.pdf")
        };

        if (string.IsNullOrWhiteSpace(runnerOptions.SourcePartPath) || !File.Exists(runnerOptions.SourcePartPath))
        {
            report.FailureStage = "source_part_missing";
            report.Errors.Add($"source_part_missing: {runnerOptions.SourcePartPath ?? "<null>"}");
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }

        var sessionManager = new SolidWorksSessionManager();
        var options = SolidWorksRuntimeOptions.FromEnvironment() with
        {
            EnableRealExecution = true,
            Visible = runnerOptions.Visible,
            OutputDirectory = Path.GetFullPath(runnerOptions.OutputRoot),
            DrawingTemplatePath = runnerOptions.DrawingTemplatePath
        };

        var connection = await sessionManager.ConnectAsync(options, cancellationToken);
        report.SolidWorksConnected = connection.Connected;
        report.SolidWorksVersion = connection.SolidWorksVersion;
        if (!connection.Connected)
        {
            report.FailureStage = "solidworks_connection_failed";
            report.Errors.AddRange(connection.Issues);
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingReportWriter.WriteAsync(reportPath, report, cancellationToken);
            return report;
        }

        try
        {
            var request = new SolidWorksWorkerRequest(
                $"drawing-runner-{Guid.NewGuid():N}",
                CreatePlan(),
                outputDirectory,
                DryRun: false,
                AllowRealCadExecution: true,
                DrawingSmokeTestOnly: true,
                SourcePartPath: Path.GetFullPath(runnerOptions.SourcePartPath),
                DrawingTemplatePath: runnerOptions.DrawingTemplatePath);

            var result = await sessionManager.ExecuteWithApplicationAsync(
                (application, token) => new LateBoundSolidWorksDrawingBuilder().CreateBasicViewsDrawingAsync(
                    application,
                    request,
                    options,
                    connection.SolidWorksVersion,
                    token),
                cancellationToken);

            var generatedReport = result.GeneratedArtifacts.FirstOrDefault(artifact =>
                artifact.FilePath.EndsWith("drawing_report.json", StringComparison.OrdinalIgnoreCase));
            if (generatedReport is not null && File.Exists(generatedReport.FilePath))
            {
                return System.Text.Json.JsonSerializer.Deserialize<SolidWorksDrawingReport>(
                           await File.ReadAllTextAsync(generatedReport.FilePath, cancellationToken),
                           new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower })
                       ?? report;
            }

            report.FailureStage = "drawing_report_write_failed";
            report.Errors.AddRange(result.Issues.DefaultIfEmpty("drawing_report_write_failed: builder did not return drawing_report.json."));
            report.CompletedAt = DateTimeOffset.UtcNow;
            await SolidWorksDrawingReportWriter.WriteAsync(reportPath, report, cancellationToken);
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
            ? Path.Combine("output", "solidworks", "real", "plate_basic_4holes_drawing")
            : outputRoot);
        return Path.Combine(root, $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
    }

    private static SolidWorksBuildPlan CreatePlan() =>
        new(
            $"drawing-plan-{Guid.NewGuid():N}",
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
                    "expected-drawing",
                    "Drawing",
                    "plate_basic_4holes.SLDDRW",
                    ".SLDDRW",
                    false,
                    0,
                    "预期真实工程图文件。"),
                new SolidWorksArtifact(
                    "expected-pdf",
                    "Pdf",
                    "plate_basic_4holes.pdf",
                    ".pdf",
                    false,
                    0,
                    "预期工程图 PDF。"),
                new SolidWorksArtifact(
                    "expected-drawing-report",
                    "DrawingReport",
                    "drawing_report.json",
                    ".json",
                    false,
                    0,
                    "预期工程图诊断报告。")
            },
            new[] { "drawing smoke runner source part must already exist" },
            new[] { "V1.1 只验证基础视图，不做尺寸、标题栏或 BOM。" });
}
