using AgentRuntime.Microsoft;
using PlatformCore;

if (args.Length == 0 || !string.Equals(args[0], "self-check", StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: dotnet run --project src/Interfaces/CliHost -- self-check");
    return 1;
}

var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
var outputRoot = Path.Combine(projectRoot, "output");
var platform = RuntimePlatformFactory.CreateDefault(projectRoot);
var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, projectRoot);
var reportPath = Path.Combine(outputRoot, "reports", "platform_self_check_report.json");

Console.WriteLine("AI Mechanical Engineering Agent Platform self-check");
Console.WriteLine($"Final status: {report.FinalStatus}");
Console.WriteLine($"Registered modules: {report.RegisteredModules.Count}");
Console.WriteLine($"Registered agents: {report.RegisteredAgents.Count}");
Console.WriteLine($"Public agents: {string.Join(", ", report.PublicAgents.Select(agent => agent.Id))}");
Console.WriteLine($"Internal agents: {string.Join(", ", report.InternalAgents.Select(agent => agent.Id))}");
Console.WriteLine($"Registered workers: {string.Join(", ", report.RegisteredWorkers.Select(worker => worker.Name))}");
Console.WriteLine($"Gateway visible agents: {string.Join(", ", report.GatewayVisibleAgents.Select(agent => agent.Id))}");
Console.WriteLine($"Report: {reportPath}");

return report.FinalStatus == "Passed" ? 0 : 2;

static string FindProjectRoot(string startDirectory)
{
    var directory = new DirectoryInfo(startDirectory);
    while (directory is not null)
    {
        if (directory.GetFiles("AI_Mechanical_Engineering_Agent_Platform.sln").Length > 0)
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return startDirectory;
}
