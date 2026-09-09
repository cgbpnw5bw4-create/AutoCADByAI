using System.Globalization;
using System.Text.Json;
using AgentGatewayHost;
using AgentRuntime.Microsoft;
using DomainSchemas;
using PlatformCore;
using PlatformCore.Modules.CADModeling;

if (args.Length > 0 && string.Equals(args[0], "self-check", StringComparison.OrdinalIgnoreCase))
{
    var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
    string outputRoot;
    try { outputRoot = SelfCheckCliOptions.ResolveOutputRoot(args, projectRoot); }
    catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
    { Console.Error.WriteLine($"self_check_invalid_options: {exception.Message}"); return 2; }
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
    Console.WriteLine($"Hole self-check group: {(report.HoleSelfCheckGroupPassed ? "Passed" : "Failed")}");
    foreach (var issue in report.HoleSelfCheckIssues) Console.Error.WriteLine(issue);
    return report.FinalStatus == "Passed" ? 0 : 2;
}

if (CadDryRunCliContract.IsInvocation(args))
{
    var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
    try
    {
        var input = JsonSerializer.Deserialize<CadWorkflowInput>(await File.ReadAllTextAsync(Path.GetFullPath(args[2])), JsonOptions());
        var modelSpec = ResolveModelSpec(input);
        if (input is null || modelSpec is null) { Console.Error.WriteLine("invalid_cad_model_spec: 缺少模型。"); return 2; }
        var runId = $"cad-dry-run-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}-{Guid.NewGuid():N}";
        var outputDirectory = Path.Combine(projectRoot, "output", "solidworks", "dry-run", runId);
        var context = CadDryRunCliContract.ForceDryRun(ToGatewayContext(input, modelSpec, projectRoot, outputDirectory, runId));
        var response = await new AgentMessageDispatcher(RuntimePlatformFactory.CreateDefault(projectRoot)).DispatchAsync(
            "chief-engineer", new GatewayMessageRequest("CliHost", "cli", runId, Environment.UserName,
                "验证通用 CAD FeatureGraph 模拟工作流。", [], context));
        Directory.CreateDirectory(outputDirectory);
        var passed = response?.Status.Equals("completed", StringComparison.OrdinalIgnoreCase) == true;
        var reportPath = Path.Combine(outputDirectory, "dry_run_report.json");
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
        {
            run_id = runId, model_id = modelSpec.ModelId, dry_run = true, real_cad_executed = false,
            simulation_status = passed ? "Passed" : "Failed", deliverable_status = "NotDeliverable",
            gateway_status = response?.Status, quality_gate = response?.GateDecision, issues = response?.Issues,
            artifacts = response?.Artifacts
        }, JsonOptions()));
        Console.WriteLine($"simulation_status={(passed ? "Passed" : "Failed")}");
        Console.WriteLine("real_cad_executed=false");
        Console.WriteLine("deliverable_status=NotDeliverable");
        Console.WriteLine($"dry_run_report={reportPath}");
        if (!passed) foreach (var issue in response?.Issues ?? []) Console.Error.WriteLine(issue);
        return passed ? 0 : 2;
    }
    catch (Exception ex) when (ex is IOException or JsonException or ArgumentException)
    { Console.Error.WriteLine($"invalid_cad_model_spec: {ex.Message}"); return 2; }
}

if (SolidWorksE2eCliContract.IsInvocation(args))
{
    var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
    var localProfile = SolidWorksLocalExecutionProfile.Load(projectRoot);
    localProfile.ApplyToCurrentProcess();
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

    var modelSpec = ResolveModelSpec(input);
    var validationIssues = Validate(input, modelSpec);
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
    var partType = modelSpec!.PartType;
    var e2eOutputDirectory = Path.Combine(projectRoot, "output", "solidworks", "e2e", ToSafePathSegment(partType), runId);
    var context = ToGatewayContext(input!, modelSpec, projectRoot, e2eOutputDirectory, runId);
    var platform = RuntimePlatformFactory.CreateDefault(projectRoot);
    var dispatcher = new AgentMessageDispatcher(platform);
    var response = await dispatcher.DispatchAsync("chief-engineer", new GatewayMessageRequest(
        "CliHost",
        "cli",
        runId,
        Environment.UserName,
        $"Execute controlled {input!.Operation} for {partType} from structured CADModelSpec input.",
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
    var runtimeOptions = SolidWorksRuntimeOptions.FromEnvironment();
    Console.WriteLine("SolidWorks controlled workflow finished; inspect final status before treating it as accepted.");
    Console.WriteLine($"Real execution effective: {runtimeOptions.EnableRealExecution}");
    Console.WriteLine($"Visible mode: {runtimeOptions.Visible}");
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
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- self-check [--output <directory>]");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_plate_request.json");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_flange_request.json");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_shaft_request.json");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_jacket_request.json");
Console.WriteLine("  dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/parameter_update_plate.json");
return 1;

static IReadOnlyList<string> Validate(CadWorkflowInput? input, CADModelSpec? modelSpec)
{
    var issues = new List<string>();
    if (input is null)
    {
        issues.Add("input must be a JSON object.");
        return issues;
    }

    if (!SolidWorksE2eCliContract.IsSupportedOperation(input.Operation))
    {
        issues.Add($"operation must be {SolidWorksE2eCliContract.CompleteDrawingPackageOperation}, {SolidWorksE2eCliContract.PartFamilyReleasePackageOperation}, or {SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation}.");
    }

    if (modelSpec is null || string.IsNullOrWhiteSpace(modelSpec.PartType))
    {
        issues.Add("cad_model_spec.part_type is required.");
        return issues;
    }

    var generateDrawing = ResolveBooleanOption(input.GenerateDrawing, modelSpec.DrawingRequirements, "generate_drawing");
    var generateDimensions = ResolveBooleanOption(input.GenerateDimensions, modelSpec.DrawingRequirements, "generate_dimensions");
    var generateTitleBlock = ResolveBooleanOption(input.GenerateTitleBlock, modelSpec.DrawingRequirements, "generate_title_block");
    var generateReleasePackage = ResolveBooleanOption(input.GenerateReleasePackage, modelSpec.DrawingRequirements, "generate_release_package");

    if (string.Equals(input.Operation, SolidWorksE2eCliContract.CompleteDrawingPackageOperation, StringComparison.OrdinalIgnoreCase))
    {
        if (!string.Equals(modelSpec.PartType, PlateBasic4HolesDefinition.Type, StringComparison.OrdinalIgnoreCase))
            issues.Add("build_complete_drawing_package remains restricted to plate_basic_4holes.");
        if (!HasExactPlateRegressionDimensions(modelSpec))
            issues.Add("the controlled plate regression requires 160x80x12 mm with four 10 mm holes.");
        if (!generateDrawing || !generateDimensions || !generateTitleBlock || !generateReleasePackage)
            issues.Add("the complete plate package requires all generate_* flags to be true.");
    }
    else if (SolidWorksE2eCliContract.IsPartFamilyReleasePackage(input.Operation))
    {
        if (generateDrawing || generateDimensions || generateTitleBlock)
            issues.Add("build_part_family_release_package is build-only; drawing, dimension and title-block flags must be false.");
        if (!generateReleasePackage)
            issues.Add("build_part_family_release_package requires generate_release_package=true.");
    }
    else if (SolidWorksE2eCliContract.IsModelUpdateReleasePackage(input.Operation))
    {
        if (!string.Equals(modelSpec.PartType, PlateBasic4HolesDefinition.Type, StringComparison.OrdinalIgnoreCase))
            issues.Add("rebuild_parameter_update remains restricted to plate_basic_4holes.");
        if (!HasExactPlateRegressionDimensions(modelSpec))
            issues.Add("the controlled V2.0-D baseline requires 160x80x12 mm with four 10 mm holes.");
        if (modelSpec.Sketches.Count == 0 || modelSpec.Features.Count == 0)
            issues.Add("rebuild_parameter_update requires an explicit FeatureGraph so execution cannot bypass FeatureHandler.");
        if (input.ParameterUpdate is null || input.ParameterUpdate.NewParameters.Count == 0 || input.ParameterUpdate.OldParameters is null)
            issues.Add("rebuild_parameter_update requires explicit old_parameters and new_parameters.");
        else if (!HasParameterValue(input.ParameterUpdate.NewParameters, "length_mm", "200") ||
                 !HasParameterValue(input.ParameterUpdate.NewParameters, "width_mm", "100") ||
                 !HasParameterValue(input.ParameterUpdate.NewParameters, "thickness_mm", "15"))
            issues.Add("the controlled V2.0-D update requires 200x100x15 mm.");
        if (!modelSpec.OutputRequirements.Contains("geometry_validation_report.json", StringComparer.OrdinalIgnoreCase) ||
            !modelSpec.OutputRequirements.Contains("rebuild_report.json", StringComparer.OrdinalIgnoreCase))
            issues.Add("rebuild_parameter_update requires geometry_validation_report.json and rebuild_report.json outputs.");
        if (generateDrawing || generateDimensions || generateTitleBlock || !generateReleasePackage)
            issues.Add("rebuild_parameter_update is build-only and requires generate_release_package=true.");
    }

    return issues;
}

static IReadOnlyDictionary<string, string> ToGatewayContext(
    CadWorkflowInput input,
    CADModelSpec modelSpec,
    string projectRoot,
    string outputDirectory,
    string runId) =>
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["request_id"] = runId,
        ["operation"] = input.Operation!,
        ["part_type"] = modelSpec.PartType,
        ["cad_model_spec_json"] = JsonSerializer.Serialize(modelSpec, JsonOptions()),
        ["dry_run"] = ResolveBooleanOption(input.DryRun, modelSpec.ExecutionOptions, "dry_run", defaultValue: false) ? "true" : "false",
        ["generate_drawing"] = ResolveBooleanOption(input.GenerateDrawing, modelSpec.DrawingRequirements, "generate_drawing") ? "true" : "false",
        ["generate_dimensions"] = ResolveBooleanOption(input.GenerateDimensions, modelSpec.DrawingRequirements, "generate_dimensions") ? "true" : "false",
        ["generate_title_block"] = ResolveBooleanOption(input.GenerateTitleBlock, modelSpec.DrawingRequirements, "generate_title_block") ? "true" : "false",
        ["generate_release_package"] = ResolveBooleanOption(input.GenerateReleasePackage, modelSpec.DrawingRequirements, "generate_release_package") ? "true" : "false",
        ["structured_input_received"] = "true",
        ["gateway_invoked"] = "true",
        ["project_root"] = projectRoot,
        ["solidworks_output_directory"] = outputDirectory,
        ["parameter_update_json"] = input.ParameterUpdate is null
            ? string.Empty
            : JsonSerializer.Serialize(input.ParameterUpdate, JsonOptions())
    };

static CADModelSpec? ResolveModelSpec(CadWorkflowInput? input)
{
    if (input?.CadModelSpec is not null)
    {
        return input.CadModelSpec;
    }

    if (input is null || string.IsNullOrWhiteSpace(input.PartType))
    {
        return null;
    }

    var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    AddNumber(dimensions, "length_mm", input.LengthMm);
    AddNumber(dimensions, "width_mm", input.WidthMm);
    AddNumber(dimensions, "thickness_mm", input.ThicknessMm);
    AddNumber(dimensions, "hole_count", input.HoleCount);
    AddNumber(dimensions, "hole_diameter_mm", input.HoleDiameterMm);
    return new CADModelSpec(
        $"cad-model-spec-{input.PartType}-{Guid.NewGuid():N}",
        input.PartType,
        dimensions,
        material: "Q235",
        outputRequirements: ["SLDPRT", "STEP", "build_report.json"]);
}

static void AddNumber<T>(IDictionary<string, string> values, string name, T? value) where T : struct, IFormattable
{
    if (value.HasValue)
    {
        values[name] = value.Value.ToString(null, CultureInfo.InvariantCulture);
    }
}

static bool ResolveBooleanOption(
    bool? explicitValue,
    IReadOnlyDictionary<string, string> options,
    string name,
    bool defaultValue = false)
{
    if (explicitValue.HasValue)
    {
        return explicitValue.Value;
    }

    return options.TryGetValue(name, out var value)
        ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1"
        : defaultValue;
}

static bool HasExactPlateRegressionDimensions(CADModelSpec spec) =>
    HasValue(spec, "length_mm", "160") &&
    HasValue(spec, "width_mm", "80") &&
    HasValue(spec, "thickness_mm", "12") &&
    HasValue(spec, "hole_count", "4") &&
    HasValue(spec, "hole_diameter_mm", "10");

static bool HasValue(CADModelSpec spec, string name, string expected) =>
    spec.TryGetParameter(name, out var value) &&
    decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var actual) &&
    decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var target) &&
    actual == target;

static bool HasParameterValue(IReadOnlyDictionary<string, string> values, string name, string expected) =>
    values.TryGetValue(name, out var actual) &&
    decimal.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedActual) &&
    decimal.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExpected) &&
    parsedActual == parsedExpected;

static JsonSerializerOptions JsonOptions() => new()
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
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

static string ToSafePathSegment(string value) =>
    string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

internal sealed class CadWorkflowInput
{
    public string? Operation { get; init; }
    public CADModelSpec? CadModelSpec { get; init; }
    public string? PartType { get; init; }
    public decimal? LengthMm { get; init; }
    public decimal? WidthMm { get; init; }
    public decimal? ThicknessMm { get; init; }
    public int? HoleCount { get; init; }
    public decimal? HoleDiameterMm { get; init; }
    public bool? DryRun { get; init; }
    public bool? GenerateDrawing { get; init; }
    public bool? GenerateDimensions { get; init; }
    public bool? GenerateTitleBlock { get; init; }
    public bool? GenerateReleasePackage { get; init; }
    public ModelParameterUpdateRequest? ParameterUpdate { get; init; }
}
