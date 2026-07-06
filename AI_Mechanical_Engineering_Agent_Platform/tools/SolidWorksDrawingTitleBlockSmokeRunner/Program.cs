using SolidWorksDrawingTitleBlockSmokeRunner;

var options = DrawingTitleBlockRunnerOptions.Parse(args);
var runner = new SolidWorksDrawingTitleBlockDiagnosticRunner();
var report = await runner.RunAsync(options, CancellationToken.None);
var reportPath = Path.Combine(report.OutputDirectory, "title_block_report.json");

Console.WriteLine($"title_block_report={reportPath}");
Console.WriteLine($"final_status={report.FinalStatus}");
Console.WriteLine($"failure_stage={report.FailureStage}");

return report.FinalStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
