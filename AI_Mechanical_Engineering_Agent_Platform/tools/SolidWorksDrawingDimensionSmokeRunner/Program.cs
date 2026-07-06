using SolidWorksDrawingDimensionSmokeRunner;

var options = DrawingDimensionRunnerOptions.Parse(args);
var runner = new SolidWorksDrawingDimensionDiagnosticRunner();
var report = await runner.RunAsync(options, CancellationToken.None);
var reportPath = Path.Combine(report.OutputDirectory, "dimension_report.json");

Console.WriteLine($"dimension_report={reportPath}");
Console.WriteLine($"final_status={report.FinalStatus}");
Console.WriteLine($"failure_stage={report.FailureStage}");

return report.FinalStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
