using System.Text.Json;

namespace SolidWorksSmokeRunner;

internal static class Program
{
    private static int Main(string[] args)
    {
        var options = ParseArgs(args);
        var runner = new SolidWorksDiagnosticRunner();
        var report = runner.Run(options);

        Console.WriteLine($"diagnostic_report={Path.Combine(report.OutputDirectory, "diagnostic_report.json")}");
        Console.WriteLine($"final_status={report.FinalStatus}");
        Console.WriteLine($"failure_stage={report.FailureStage}");

        return report.FinalStatus.Equals("Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
    }

    private static SolidWorksDiagnosticOptions ParseArgs(string[] args)
    {
        var output = Path.Combine("output", "solidworks", "diagnostics");
        string? template = Environment.GetEnvironmentVariable("SW_TEMPLATE_PART_PATH");
        var visible = true;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--template" when index + 1 < args.Length:
                    template = args[++index];
                    break;
                case "--hidden":
                    visible = false;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run --project tools/SolidWorksSmokeRunner -- --output output/solidworks/diagnostics [--template C:\\Path\\Part.prtdot] [--hidden]");
                    Environment.Exit(0);
                    break;
            }
        }

        return new SolidWorksDiagnosticOptions(output, template, visible);
    }
}
