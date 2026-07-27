using SolidWorksPartFamilySmokeRunner;

var options = PartFamilySmokeRunnerOptions.Parse(args);
var report = await new PartFamilyApiSmokeRunner().RunAsync(options, CancellationToken.None);
Console.WriteLine($"evidence_report={report.EvidenceReportPath}");
Console.WriteLine($"final_status={report.FinalStatus}");
Console.WriteLine($"failure_stage={report.FailureStage}");

return report.FinalStatus.Equals("Failed", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
