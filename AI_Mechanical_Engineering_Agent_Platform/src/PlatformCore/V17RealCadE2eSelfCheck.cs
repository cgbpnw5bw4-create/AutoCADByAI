using System.Text.Json;
using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

internal sealed record V17RealCadE2eSelfCheckResult(
    bool StructuredInputSupported,
    bool ChiefEngineerInvoked,
    bool WorkflowEngineInvoked,
    bool SolidWorksRouterTriggered,
    bool QualityGateRejectsIncompleteExecution,
    bool DefaultExecutionDisabled,
    bool RequestConfirmationRequired,
    bool EnvironmentConfirmationRequired,
    bool ReportSupported,
    bool DeliverableSemanticsSupported,
    bool LocalAuthorizationProfileSupported,
    bool LocalAuthorizationDefaultDisabled);

internal static class V17RealCadE2eSelfCheck
{
    public static async Task<V17RealCadE2eSelfCheckResult> RunAsync(
        string projectRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "ai_me_v17_self_check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var output = Path.Combine(root, "output", "solidworks", "e2e", "plate_basic_4holes", "self-check");
            var platform = PlatformBootstrapper.CreateDefault(root);
            var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")
                ?? throw new InvalidOperationException("chief-engineer is not registered.");
            var chiefOutput = await chiefEngineer.ExecuteAsync(CreateControlledContext(root, output));
            var reportPath = Path.Combine(output, "reports", "e2e_execution_report.json");

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, cancellationToken));
            var reportRoot = report.RootElement;
            var failedWithoutCom =
                string.Equals(ReadString(reportRoot, "final_status"), "Failed", StringComparison.OrdinalIgnoreCase) &&
                !ReadBoolean(reportRoot, "solidworks_launch_attempted") &&
                !ReadBoolean(reportRoot, "real_worker_invoked") &&
                !ReadBoolean(reportRoot, "real_cad_executed");

            var authorizationRoot = Path.Combine(root, "authorized-profile");
            Directory.CreateDirectory(Path.Combine(authorizationRoot, "config"));
            await File.WriteAllTextAsync(
                Path.Combine(authorizationRoot, SolidWorksLocalExecutionProfile.ConfigurationRelativePath),
                "{\"real_execution_authorized\":true,\"execution_authorization_source\":\"LocalDevelopmentProfile\",\"visible\":false}",
                cancellationToken);
            var authorization = SolidWorksLocalExecutionProfile.Load(authorizationRoot);
            var disabledOptions = new SolidWorksRuntimeOptions(false, false, null, authorizationRoot, 1, 1, null, false);
            var confirmationRunner = new SolidWorksMainWorkflowRunner(
                platform.SkillRegistry,
                platform.WorkerRegistry,
                platform.AuditLog,
                platform.WorkflowEngine,
                () => disabledOptions);

            var requestConfirmationResult = await confirmationRunner.ExecuteAsync(
                CreateRequest(authorizationRoot, Path.Combine(root, "request-confirmation"), allowRealCadExecution: false),
                cancellationToken);
            var environmentConfirmationResult = await confirmationRunner.ExecuteAsync(
                CreateRequest(authorizationRoot, Path.Combine(root, "environment-confirmation"), allowRealCadExecution: true),
                cancellationToken);
            var requestIssues = await ReadReportErrorsAsync(requestConfirmationResult.OutputDirectory, cancellationToken);
            var environmentIssues = await ReadReportErrorsAsync(environmentConfirmationResult.OutputDirectory, cancellationToken);

            return new V17RealCadE2eSelfCheckResult(
                StructuredInputSupported: ExampleRequestIsControlled(projectRoot) && ReadBoolean(reportRoot, "structured_input_received"),
                ChiefEngineerInvoked: chiefOutput.InternalCollaborationReport is not null && ReadBoolean(reportRoot, "chief_engineer_invoked"),
                WorkflowEngineInvoked: ReadBoolean(reportRoot, "workflow_engine_invoked"),
                SolidWorksRouterTriggered: ReadBoolean(reportRoot, "solidworks_router_triggered"),
                QualityGateRejectsIncompleteExecution: string.Equals(ReadString(reportRoot, "quality_gate_decision"), "Failed", StringComparison.OrdinalIgnoreCase) && failedWithoutCom,
                DefaultExecutionDisabled: failedWithoutCom,
                RequestConfirmationRequired: requestIssues.Any(issue => issue.Contains("allow_real_cad_execution=true", StringComparison.Ordinal)),
                EnvironmentConfirmationRequired:
                    environmentIssues.Any(issue => issue.Contains("SW_ENABLE_REAL_EXECUTION=true", StringComparison.Ordinal)) &&
                    environmentIssues.Any(issue => issue.Contains("SW_REAL_MAIN_WORKFLOW_TEST=true", StringComparison.Ordinal)),
                ReportSupported:
                    File.Exists(reportPath) && new FileInfo(reportPath).Length > 0 &&
                    File.Exists(Path.Combine(output, "latest_real_outputs.md")) &&
                    new FileInfo(Path.Combine(output, "latest_real_outputs.md")).Length > 0,
                DeliverableSemanticsSupported:
                    !ReadBoolean(reportRoot, "all_source_reports_passed") &&
                    string.Equals(ReadString(reportRoot, "deliverable_status"), "NotDeliverable", StringComparison.OrdinalIgnoreCase),
                LocalAuthorizationProfileSupported:
                    File.Exists(Path.Combine(projectRoot, "config", "solidworks.local.example.json")) && authorization.IsAuthorized,
                LocalAuthorizationDefaultDisabled: !SolidWorksLocalExecutionProfile.Load(Path.Combine(root, "no-local-profile")).IsAuthorized);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static AgentContext CreateControlledContext(string projectRoot, string outputDirectory) =>
        new(
            $"v17-self-check-{Guid.NewGuid():N}",
            new AgentInput(
                "self-check",
                "self-check",
                "v17-self-check",
                "self-check",
                "Run the controlled SolidWorks E2E workflow.",
                Array.Empty<string>(),
                new Dictionary<string, string>
                {
                    ["operation"] = "build_complete_drawing_package",
                    ["part_type"] = "plate_basic_4holes",
                    ["allow_real_cad_execution"] = "true",
                    ["dry_run"] = "false",
                    ["generate_drawing"] = "true",
                    ["generate_dimensions"] = "true",
                    ["generate_title_block"] = "true",
                    ["generate_release_package"] = "true",
                    ["structured_input_received"] = "true",
                    ["gateway_invoked"] = "true",
                    ["project_root"] = projectRoot,
                    ["solidworks_output_directory"] = outputDirectory
                }),
            new Dictionary<string, object?>(),
            DateTimeOffset.UtcNow);

    private static SolidWorksMainWorkflowRequest CreateRequest(
        string projectRoot,
        string outputDirectory,
        bool allowRealCadExecution) =>
        new(
            $"v17-confirmation-{Guid.NewGuid():N}",
            $"v17-confirmation-task-{Guid.NewGuid():N}",
            projectRoot,
            outputDirectory,
            DryRun: false,
            AllowRealCadExecution: allowRealCadExecution,
            ModelSpec: SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec(),
            Operation: SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage,
            GenerateDrawing: true,
            GenerateDimensions: true,
            GenerateTitleBlock: true,
            GenerateReleasePackage: true,
            StructuredInputReceived: true,
            ChiefEngineerInvoked: true,
            GatewayInvoked: true,
            SolidWorksRouterTriggered: true);

    private static bool ExampleRequestIsControlled(string projectRoot)
    {
        var path = Path.Combine(projectRoot, "examples", "real_cad_plate_request.json");
        if (!File.Exists(path))
        {
            return false;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        return string.Equals(ReadString(root, "operation"), "build_complete_drawing_package", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ReadString(root, "part_type"), "plate_basic_4holes", StringComparison.OrdinalIgnoreCase) &&
               ReadBoolean(root, "allow_real_cad_execution") &&
               !ReadBoolean(root, "dry_run");
    }

    private static async Task<IReadOnlyList<string>> ReadReportErrorsAsync(string outputDirectory, CancellationToken cancellationToken)
    {
        var reportPath = Path.Combine(outputDirectory, "reports", "e2e_execution_report.json");
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath, cancellationToken));
        return report.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }

    private static bool ReadBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) ? property.GetString() : null;
}
