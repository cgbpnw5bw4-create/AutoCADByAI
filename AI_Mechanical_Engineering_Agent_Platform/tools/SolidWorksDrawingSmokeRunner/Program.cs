using SolidWorksDrawingSmokeRunner;

var options = DrawingRunnerOptions.Parse(args);
var runner = new SolidWorksDrawingDiagnosticRunner();
var report = await runner.RunAsync(options, CancellationToken.None);
var reportPath = Path.Combine(report.OutputDirectory, "drawing_report.json");

Console.WriteLine($"drawing_report={reportPath}");
Console.WriteLine($"final_status={report.FinalStatus}");
Console.WriteLine($"failure_stage={report.FailureStage}");

return report.FinalStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
