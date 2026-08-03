using SolidWorksFeatureExecutionSmokeRunner;

var options = FeatureExecutionSmokeRunnerOptions.Parse(args);
var report = await new FeatureExecutionSmokeRunner().RunAsync(options, CancellationToken.None);

Console.WriteLine($"feature_execution_report={report.ReportPath}");
Console.WriteLine($"final_status={report.FinalStatus}");
Console.WriteLine($"failure_stage={report.FailureStage ?? "none"}");
Console.WriteLine("candidate_only=true");

return report.FinalStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
