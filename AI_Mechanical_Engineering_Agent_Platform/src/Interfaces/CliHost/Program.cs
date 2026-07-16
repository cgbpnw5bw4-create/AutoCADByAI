using System.Globalization;
using System.Text.Json;
using AgentGatewayHost;
using AgentRuntime.Microsoft;
using PlatformCore;

if (args.Length > 0 && string.Equals(args[0], "self-check", StringComparison.OrdinalIgnoreCase))
{
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
}

if (SolidWorksE2eCliContract.IsInvocation(args))
{
    var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
    var localAuthorization = SolidWorksLocalExecutionProfile.Load(projectRoot);
    localAuthorization.ApplyToCurrentProcess();
    var inputPath = Path.GetFullPath(args[2]);
    if (!File.Exists(inputPath))
    {
        Console.Error.WriteLine($"Input file does not exist: {inputPath}");
        return 2;
    }

    CadWorkflowInput? input;
    try
    {
        input = JsonSerializer.Deserialize<CadWorkflowInput>(
            await File.ReadAllTextAsync(inputPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }
    catch (JsonException ex)
    {
        Console.Error.WriteLine($"Structured input is invalid JSON: {ex.Message}");
        return 2;
    }

    var validationIssues = Validate(input);
    if (validationIssues.Count > 0)
    {
        Console.Error.WriteLine("Structured input was rejected:");
        foreach (var issue in validationIssues)
        {
            Console.Error.WriteLine($"- {issue}");
        }

        return 2;
    }

    var runId = $"cad-e2e-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}-{Guid.NewGuid():N}";
    var e2eOutputDirectory = Path.Combine(projectRoot, "output", "solidworks", "e2e", "plate_basic_4holes", runId);
    var context = ToGatewayContext(input!, localAuthorization, projectRoot, e2eOutputDirectory, runId);
    var platform = RuntimePlatformFactory.CreateDefault(projectRoot);
    var dispatcher = new AgentMessageDispatcher(platform);
    var response = await dispatcher.DispatchAsync("chief-engineer", new GatewayMessageRequest(
        "CliHost",
        "cli",
        runId,
        Environment.UserName,
        "Execute controlled build_complete_drawing_package for plate_basic_4holes from structured input.",
        Array.Empty<string>(),
        context));

    if (response is null)
    {
        Console.Error.WriteLine("Gateway rejected chief-engineer invocation.");
        return 2;
    }

    var reportPath = response.Artifacts
        .FirstOrDefault(artifact => string.Equals(artifact.Kind, "E2eExecutionReport", StringComparison.OrdinalIgnoreCase))
        ?.Path ?? Path.Combine(e2eOutputDirectory, "reports", "e2e_execution_report.json");
    Console.WriteLine("SolidWorks V1.7 end-to-end workflow finished; inspect final status before treating it as accepted.");
    Console.WriteLine($"Real execution authorized: {localAuthorization.IsAuthorized}");
    Console.WriteLine($"Authorization source: {localAuthorization.ExecutionAuthorizationSource}");
    Console.WriteLine($"Gateway status: {response.Status}");
    Console.WriteLine($"Output directory: {e2eOutputDirectory}");
    Console.WriteLine($"E2E report: {reportPath}");
    Console.WriteLine($"Release manifest: {Path.Combine(e2eOutputDirectory, "release_manifest.json")}");
    Console.WriteLine($"Package quality report: {Path.Combine(e2eOutputDirectory, "reports", "package_quality_report.json")}");

    if (File.Exists(reportPath))
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        var root = document.RootElement;
        Console.WriteLine($"Final status: {ReadString(root, "final_status") ?? "Unknown"}");
        Console.WriteLine($"all_source_reports_passed: {ReadString(root, "all_source_reports_passed") ?? "false"}");
        Console.WriteLine($"deliverable_status: {ReadString(root, "deliverable_status") ?? "NotDeliverable"}");
        Console.WriteLine($"failure_stage: {ReadString(root, "failure_stage") ?? "none"}");
        return string.Equals(ReadString(root, "final_status"), "Passed", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
    }

    Console.Error.WriteLine("E2E report was not produced; the workflow must be treated as failed.");
    return 2;
}

Console.WriteLine("Usage:");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- self-check");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json");
return 1;

static IReadOnlyList<string> Validate(CadWorkflowInput? input)
{
    var issues = new List<string>();
    if (input is null)
    {
        issues.Add("input must be a JSON object.");
        return issues;
    }

    if (!string.Equals(input.Operation, "build_complete_drawing_package", StringComparison.OrdinalIgnoreCase)) issues.Add("operation must be build_complete_drawing_package.");
    if (!string.Equals(input.PartType, "plate_basic_4holes", StringComparison.OrdinalIgnoreCase)) issues.Add("part_type must be plate_basic_4holes.");
    if (input.LengthMm != 160 || input.WidthMm != 80 || input.ThicknessMm != 12 || input.HoleCount != 4 || input.HoleDiameterMm != 10)
        issues.Add("V1.7 only accepts the controlled 160x80x12 mm plate with four 10 mm holes.");
    if (input.GenerateDrawing != true || input.GenerateDimensions != true || input.GenerateTitleBlock != true || input.GenerateReleasePackage != true)
        issues.Add("all generate_drawing, generate_dimensions, generate_title_block and generate_release_package flags must be true.");
    return issues;
}

static IReadOnlyDictionary<string, string> ToGatewayContext(
    CadWorkflowInput input,
    SolidWorksLocalExecutionProfile localAuthorization,
    string projectRoot,
    string outputDirectory,
    string runId) =>
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["request_id"] = runId,
        ["operation"] = input.Operation!,
        ["part_type"] = input.PartType!,
        ["length_mm"] = input.LengthMm!.Value.ToString(CultureInfo.InvariantCulture),
        ["width_mm"] = input.WidthMm!.Value.ToString(CultureInfo.InvariantCulture),
        ["thickness_mm"] = input.ThicknessMm!.Value.ToString(CultureInfo.InvariantCulture),
        ["hole_count"] = input.HoleCount!.Value.ToString(CultureInfo.InvariantCulture),
        ["hole_diameter_mm"] = input.HoleDiameterMm!.Value.ToString(CultureInfo.InvariantCulture),
        ["allow_real_cad_execution"] = input.AllowRealCadExecution == true ? "true" : "false",
        ["dry_run"] = input.DryRun == false ? "false" : "true",
        ["generate_drawing"] = "true",
        ["generate_dimensions"] = "true",
        ["generate_title_block"] = "true",
        ["generate_release_package"] = "true",
        ["structured_input_received"] = "true",
        ["gateway_invoked"] = "true",
        ["real_execution_authorized"] = localAuthorization.IsAuthorized ? "true" : "false",
        ["execution_authorization_source"] = localAuthorization.ExecutionAuthorizationSource,
        ["project_root"] = projectRoot,
        ["solidworks_output_directory"] = outputDirectory
    };

static string? ReadString(JsonElement root, string propertyName) =>
    root.TryGetProperty(propertyName, out var value) ? value.ToString() : null;

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

internal sealed class CadWorkflowInput
{
    public string? Operation { get; init; }
    public string? PartType { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? WidthMm { get; init; }
    public decimal? ThicknessMm { get; init; }
    public int? HoleCount { get; init; }
    public decimal? HoleDiameterMm { get; init; }
    public bool? AllowRealCadExecution { get; init; }
    public bool? DryRun { get; init; }
    public bool? GenerateDrawing { get; init; }
    public bool? GenerateDimensions { get; init; }
    public bool? GenerateTitleBlock { get; init; }
    public bool? GenerateReleasePackage { get; init; }
}
