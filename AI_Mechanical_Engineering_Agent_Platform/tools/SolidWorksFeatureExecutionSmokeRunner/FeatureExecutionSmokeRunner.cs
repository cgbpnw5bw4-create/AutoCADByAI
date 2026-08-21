using System.Text.Json;
using DomainSchemas;
using PlatformCore.Modules.CADModeling;
using SolidWorksWorker;
using SolidWorksWorker.Features;

namespace SolidWorksFeatureExecutionSmokeRunner;

public sealed record FeatureExecutionSmokeRunnerOptions(
    string? InputPath,
    string OutputRoot,
    bool Enabled,
    bool Visible)
{
    public static FeatureExecutionSmokeRunnerOptions Parse(
        string[] args,
        IReadOnlyDictionary<string, string?>? environment = null)
    {
        string? Get(string name) => environment is null
            ? Environment.GetEnvironmentVariable(name)
            : environment.GetValueOrDefault(name);

        var inputPath = Get("SW_FEATURE_EXECUTION_SMOKE_INPUT");
        var outputRoot = Get("SW_FEATURE_EXECUTION_SMOKE_OUTPUT") ??
                         Path.Combine("output", "solidworks", "features");
        var visible = ParseBool(Get("SW_VISIBLE"), defaultValue: true);

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index].Equals("--input", StringComparison.OrdinalIgnoreCase) &&
                index + 1 < args.Length)
            {
                inputPath = args[++index];
            }
            else if (args[index].Equals("--output", StringComparison.OrdinalIgnoreCase) &&
                     index + 1 < args.Length)
            {
                outputRoot = args[++index];
            }
            else if (args[index].Equals("--hidden", StringComparison.OrdinalIgnoreCase))
            {
                visible = false;
            }
            else if (args[index].Equals("--visible", StringComparison.OrdinalIgnoreCase))
            {
                visible = true;
            }
        }

        return new FeatureExecutionSmokeRunnerOptions(
            inputPath,
            outputRoot,
            ParseBool(Get("SW_FEATURE_EXECUTION_SMOKE_TEST")),
            visible);
    }

    private static bool ParseBool(string? value, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record FeatureExecutionArtifactEvidence(
    string FilePath,
    bool Exists,
    long SizeBytes,
    string Status);

public sealed record FeatureExecutionReport(
    string RunId,
    string ExecutionMode,
    bool CandidateOnly,
    bool DedicatedSmokeFlagEnabled,
    bool RealExecutionAllowed,
    bool SolidWorksConnected,
    bool RealCadExecuted,
    string? SolidWorksVersion,
    string? InputPath,
    string? BuildPlanId,
    FeatureExecutionArtifactEvidence Model,
    FeatureExecutionArtifactEvidence Step,
    IReadOnlyList<FeatureHandlerReport> FeatureHandlerReports,
    bool MainWorkflowAccepted,
    bool QualityGatePassed,
    string DeliverableStatus,
    string? FailureStage,
    string FinalStatus,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string ReportPath,
    DateTimeOffset GeneratedAt);

public sealed record FeatureExecutionSmokeInvocation(
    CADModelSpec ModelSpec,
    SolidWorksBuildPlan BuildPlan,
    FeatureHandlerRegistry HandlerRegistry,
    SolidWorksRuntimeOptions RuntimeOptions,
    string WorkingDirectory);

public sealed record FeatureExecutionSmokeBuildOutcome(
    bool SolidWorksConnected,
    string? SolidWorksVersion,
    PartFamilyBuildResult? BuildResult,
    string? FailureStage,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues);

public sealed class FeatureExecutionSmokeRunner
{
    private const string ExecutionMode = "RealBuildGenericFeatureGraph";
    private const string CandidateEvidenceStatus = "diagnostic_candidate";

    private static readonly IReadOnlySet<string> DiagnosticFeatureTypes =
        new HashSet<string>(
            [
                FeatureHandlerTypes.Sketch,
                FeatureTypes.ExtrudeBoss,
                FeatureTypes.ExtrudeCut,
                FeatureTypes.Hole
            ],
            StringComparer.OrdinalIgnoreCase);

    private readonly Func<FeatureExecutionSmokeInvocation, CancellationToken, Task<FeatureExecutionSmokeBuildOutcome>>
        _execute;
    private readonly Func<SolidWorksRuntimeOptions> _runtimeOptionsProvider;

    public FeatureExecutionSmokeRunner(
        Func<FeatureExecutionSmokeInvocation, CancellationToken, Task<FeatureExecutionSmokeBuildOutcome>>? execute = null,
        Func<SolidWorksRuntimeOptions>? runtimeOptionsProvider = null)
    {
        _execute = execute ?? ExecuteProductionAsync;
        _runtimeOptionsProvider = runtimeOptionsProvider ?? (() => SolidWorksRuntimeOptions.FromEnvironment());
    }

    public async Task<FeatureExecutionReport> RunAsync(
        FeatureExecutionSmokeRunnerOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var runId = $"feature-execution-{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fffffff}";
        var outputDirectory = CreateRunDirectory(options.OutputRoot);
        var reportPath = Path.Combine(outputDirectory, "feature_execution_report.json");
        var modelPath = Path.Combine(outputDirectory, "model.SLDPRT");
        var stepPath = Path.Combine(outputDirectory, "model.STEP");
        var runtime = _runtimeOptionsProvider() with
        {
            Visible = options.Visible
        };

        if (!options.Enabled || !runtime.ShouldUseRealWorker(dryRun: false))
        {
            var disabledReason = !options.Enabled
                ? "SW_FEATURE_EXECUTION_SMOKE_TEST is not enabled."
                : "The SolidWorks runtime configuration does not allow real execution.";
            return await WriteAsync(
                CreateReport(
                    runId,
                    options,
                    runtime,
                    reportPath,
                    modelPath,
                    stepPath,
                    connected: false,
                    realCadExecuted: false,
                    solidWorksVersion: null,
                    inputPath: options.InputPath,
                    buildPlanId: null,
                    featureReports: [],
                    failureStage: null,
                    finalStatus: "Skipped",
                    logs: [$"feature_execution_smoke_skipped: {disabledReason}"],
                    issues: []),
                cancellationToken);
        }

        string? workingDirectory = null;
        try
        {
            var inputPath = ResolveInputPath(options.InputPath);
            var modelSpec = NormalizeModelSpecForBuilder(
                await ReadModelSpecAsync(inputPath, cancellationToken));
            var planResult = GenerateBuildPlan(modelSpec);
            if (!planResult.IsSuccess || planResult.BuildPlan is null)
            {
                return await WriteAsync(
                    CreateReport(
                        runId,
                        options,
                        runtime,
                        reportPath,
                        modelPath,
                        stepPath,
                        false,
                        false,
                        null,
                        inputPath,
                        null,
                        [],
                        planResult.FailureStage ?? PartFamilyFailureStages.BuildPlanGenerationFailed,
                        "Failed",
                        [],
                        planResult.Issues),
                    cancellationToken);
            }

            var plan = planResult.BuildPlan;
            if (!plan.ExecutionStrategy.Equals(
                    SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                    StringComparison.OrdinalIgnoreCase))
            {
                return await WriteAsync(
                    CreateReport(
                        runId,
                        options,
                        runtime,
                        reportPath,
                        modelPath,
                        stepPath,
                        false,
                        false,
                        null,
                        inputPath,
                        plan.PlanId,
                        [],
                        PartFamilyFailureStages.FeatureAdapterMissing,
                        "Failed",
                        [],
                        ["feature_adapter_missing: the diagnostic input must compile to feature_handler_graph."]),
                    cancellationToken);
            }

            var handlerRegistry = CreateDiagnosticRegistry(reportPath);
            var handlerPreflight = handlerRegistry.ValidateForRealExecution(plan);
            if (!handlerPreflight.IsPassed)
            {
                return await WriteAsync(
                    CreateReport(
                        runId,
                        options,
                        runtime,
                        reportPath,
                        modelPath,
                        stepPath,
                        false,
                        false,
                        null,
                        inputPath,
                        plan.PlanId,
                        [],
                        handlerPreflight.FailureStage ?? PartFamilyFailureStages.FeatureApiUnverified,
                        "Failed",
                        [],
                        handlerPreflight.Issues),
                    cancellationToken);
            }

            workingDirectory = Path.Combine(
                Path.GetTempPath(),
                $"solidworks-feature-execution-{Guid.NewGuid():N}");
            Directory.CreateDirectory(workingDirectory);
            var outcome = await _execute(
                new FeatureExecutionSmokeInvocation(
                    modelSpec,
                    plan,
                    handlerRegistry,
                    runtime with { OutputDirectory = workingDirectory },
                    workingDirectory),
                cancellationToken);

            var issues = outcome.Issues.ToList();
            var logs = outcome.Logs.ToList();
            var buildResult = outcome.BuildResult;
            if (buildResult is not null)
            {
                logs.AddRange(buildResult.Logs);
                issues.AddRange(buildResult.Issues);
            }

            NormalizeArtifact(buildResult, ".SLDPRT", modelPath, issues);
            NormalizeArtifact(buildResult, ".STEP", stepPath, issues);
            if (!CadArtifactContentValidator.TryValidateStepFile(stepPath, out var stepContentIssue))
            {
                issues.Add(
                    $"{PartFamilyFailureStages.StepExportFailed}: " +
                    $"normalized STEP artifact failed content validation: {stepContentIssue}");
            }
            var featureReports = ReadFeatureReports(buildResult, issues)
                .Select(report => report with
                {
                    EvidenceSolidWorksVersion =
                        outcome.SolidWorksVersion ?? report.EvidenceSolidWorksVersion,
                    EvidenceDiagnosticRunPath = reportPath
                })
                .ToArray();
            var modelEvidence = Evidence(modelPath);
            var stepEvidence = Evidence(stepPath);
            var featureResultsPassed =
                featureReports.Length > 0 &&
                featureReports.All(report =>
                    string.IsNullOrWhiteSpace(report.FailureStage) &&
                    report.ResultObjectValidated &&
                    report.RebuildPassed &&
                    report.GeometryChangeValidated);
            if (!featureResultsPassed)
            {
                issues.Add(
                    $"{PartFamilyFailureStages.FeatureResultInvalid}: every executed feature must report a validated result object, successful rebuild, and validated geometry change.");
            }

            var candidatePassed =
                outcome.SolidWorksConnected &&
                buildResult is
                {
                    Status: "Completed",
                    RealCadExecuted: true,
                    ExecutionMode: ExecutionMode
                } &&
                modelEvidence.Status == "Passed" &&
                stepEvidence.Status == "Passed" &&
                featureResultsPassed &&
                issues.Count == 0;
            var failureStage = candidatePassed
                ? null
                : outcome.FailureStage ??
                  buildResult?.FailureStage ??
                  (stepEvidence.Status != "Passed" || issues.Any(issue =>
                          issue.Contains(PartFamilyFailureStages.StepExportFailed, StringComparison.OrdinalIgnoreCase))
                      ? PartFamilyFailureStages.StepExportFailed
                      : modelEvidence.Status != "Passed"
                          ? PartFamilyFailureStages.ArtifactValidationFailed
                      : PartFamilyFailureStages.FeatureResultInvalid);

            return await WriteAsync(
                CreateReport(
                    runId,
                    options,
                    runtime,
                    reportPath,
                    modelPath,
                    stepPath,
                    outcome.SolidWorksConnected,
                    buildResult?.RealCadExecuted == true,
                    outcome.SolidWorksVersion,
                    inputPath,
                    plan.PlanId,
                    featureReports,
                    failureStage,
                    candidatePassed ? "CandidatePassed" : "Failed",
                    logs,
                    issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return await WriteAsync(
                CreateReport(
                    runId,
                    options,
                    runtime,
                    reportPath,
                    modelPath,
                    stepPath,
                    false,
                    false,
                    null,
                    options.InputPath,
                    null,
                    [],
                    ResolveFailureStage(ex),
                    "Failed",
                    [],
                    [ex.GetBaseException().Message]),
                cancellationToken);
        }
        finally
        {
            TryDeleteWorkingDirectory(workingDirectory);
        }
    }

    private static async Task<FeatureExecutionSmokeBuildOutcome> ExecuteProductionAsync(
        FeatureExecutionSmokeInvocation invocation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invocation.RuntimeOptions.TemplatePartPath) ||
            !File.Exists(invocation.RuntimeOptions.TemplatePartPath))
        {
            return new(
                false,
                null,
                null,
                "preflight_failed",
                [],
                ["template_part_path_required_for_real_build: a valid SolidWorks part template is required."]);
        }

        var lease = await RealSolidWorksExecutionCoordinator.TryAcquireAsync(
            invocation.RuntimeOptions.ExecutionTimeoutSeconds,
            cancellationToken);
        if (lease is null)
        {
            return new(
                false,
                null,
                null,
                "real_cad_execution_coordination_timeout",
                [],
                ["real_cad_execution_coordination_timeout: the process-wide SolidWorks execution gate timed out."]);
        }

        using (lease)
        using (var session = new SolidWorksSessionManager())
        {
            var logs = new List<string>();
            var issues = new List<string>();
            var connection = await session.ConnectAsync(invocation.RuntimeOptions, cancellationToken);
            logs.AddRange(connection.Logs);
            issues.AddRange(connection.Issues);
            if (!connection.Connected)
            {
                return new(
                    false,
                    connection.SolidWorksVersion,
                    null,
                    "solidworks_connection_failed",
                    logs,
                    issues);
            }

            try
            {
                var runtimeEvidence = invocation.HandlerRegistry.ValidateRuntimeForRealExecution(
                    invocation.BuildPlan,
                    connection.SolidWorksVersion);
                if (!runtimeEvidence.IsPassed)
                {
                    issues.AddRange(runtimeEvidence.Issues);
                    return new(
                        true,
                        connection.SolidWorksVersion,
                        null,
                        runtimeEvidence.FailureStage ??
                        PartFamilyFailureStages.FeatureApiUnverified,
                        logs,
                        issues);
                }

                var request = new SolidWorksWorkerRequest(
                    $"feature-execution-smoke-{Guid.NewGuid():N}",
                    invocation.BuildPlan,
                    invocation.WorkingDirectory,
                    DryRun: false,
                    AllowRealCadExecution: true);
                var builder = new SolidWorksFeatureGraphPartFamilyBuilder(
                    invocation.ModelSpec.PartType,
                    invocation.HandlerRegistry);
                var buildResult = await session.ExecuteWithApplicationAsync(
                    (application, token) => builder.BuildAsync(
                        new PartFamilyBuildContext(
                            application,
                            request,
                            invocation.RuntimeOptions,
                            connection.SolidWorksVersion,
                            RealCadConnected: true),
                        token),
                    cancellationToken,
                    invocation.RuntimeOptions.ExecutionTimeoutSeconds);
                return new(
                    true,
                    connection.SolidWorksVersion,
                    buildResult,
                    buildResult.FailureStage,
                    logs,
                    issues);
            }
            catch (TimeoutException ex)
            {
                issues.Add(ex.Message);
                return new(
                    true,
                    connection.SolidWorksVersion,
                    null,
                    "solidworks_execution_timeout",
                    logs,
                    issues);
            }
            finally
            {
                await session.DisconnectAsync(CancellationToken.None);
            }
        }
    }

    private static PartFamilyBuildPlanResult GenerateBuildPlan(CADModelSpec modelSpec)
    {
        var registry = PartTypeRegistry.CreateDefault();
        if (!registry.TryGetDefinition(modelSpec.PartType, out var definition))
        {
            return new(
                null,
                PartFamilyFailureStages.UnsupportedPartType,
                [$"{PartFamilyFailureStages.UnsupportedPartType}: {modelSpec.PartType} is not registered."]);
        }

        return definition.GenerateBuildPlan(
            $"feature-execution-smoke-{Guid.NewGuid():N}",
            modelSpec);
    }

    private static CADModelSpec NormalizeModelSpecForBuilder(CADModelSpec modelSpec) =>
        modelSpec with
        {
            OutputRequirements = modelSpec.OutputRequirements
                .Where(requirement =>
                    !requirement.Equals(
                        "feature_execution_report.json",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray()
        };

    private static FeatureHandlerRegistry CreateDiagnosticRegistry(string reportPath)
    {
        var handlers = FeatureHandlerRegistry.CreateDefault()
            .GetAll()
            .Where(handler => DiagnosticFeatureTypes.Contains(handler.FeatureType))
            .Select(handler => new DiagnosticCandidateHandler(handler, reportPath))
            .ToArray();
        return new FeatureHandlerRegistry(handlers);
    }

    private static async Task<CADModelSpec> ReadModelSpecAsync(
        string inputPath,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(inputPath, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var modelElement = document.RootElement.TryGetProperty("cad_model_spec", out var nested)
            ? nested
            : document.RootElement;
        var modelSpec = JsonSerializer.Deserialize<CADModelSpec>(
                   modelElement.GetRawText(),
                   new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new JsonException("cad_model_spec is missing or invalid.");
        if (!document.RootElement.TryGetProperty("parameter_update", out var updateElement))
        {
            return modelSpec;
        }

        var request = JsonSerializer.Deserialize<ModelParameterUpdateRequest>(
                          updateElement.GetRawText(),
                          new JsonSerializerOptions
                          {
                              PropertyNameCaseInsensitive = true,
                              PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                          })
                      ?? throw new JsonException("parameter_update is invalid.");
        var update = new ModelUpdateService().Prepare(
            $"feature-execution-smoke-{Guid.NewGuid():N}",
            modelSpec,
            request);
        if (!update.IsSuccess || update.UpdatedModelSpec is null)
        {
            throw new JsonException(
                "parameter_update preparation failed: " +
                string.Join(" ", update.Issues));
        }

        return update.UpdatedModelSpec;
    }

    private static string ResolveInputPath(string? requestedPath)
    {
        var path = string.IsNullOrWhiteSpace(requestedPath)
            ? Path.Combine(
                FindProjectRoot(Directory.GetCurrentDirectory()),
                "examples",
                "feature_pipeline_plate.json")
            : requestedPath;
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Feature execution smoke input does not exist.", fullPath);
        }

        return fullPath;
    }

    private static string CreateRunDirectory(string outputRoot)
    {
        var root = Path.GetFullPath(outputRoot);
        Directory.CreateDirectory(root);
        var directory = Path.Combine(root, DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fffffff"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void NormalizeArtifact(
        PartFamilyBuildResult? result,
        string extension,
        string destinationPath,
        List<string> issues)
    {
        var source = result?.Artifacts.FirstOrDefault(artifact =>
            artifact.ExpectedExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) ||
            artifact.FilePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        if (source is null || !File.Exists(source.FilePath) || new FileInfo(source.FilePath).Length <= 0)
        {
            issues.Add($"{PartFamilyFailureStages.ArtifactValidationFailed}: {extension} source artifact is missing or empty.");
            return;
        }

        File.Copy(source.FilePath, destinationPath, overwrite: true);
        if (!File.Exists(destinationPath) || new FileInfo(destinationPath).Length <= 0)
        {
            issues.Add($"{PartFamilyFailureStages.ArtifactValidationFailed}: normalized {extension} artifact is missing or empty.");
        }
    }

    private static IReadOnlyList<FeatureHandlerReport> ReadFeatureReports(
        PartFamilyBuildResult? result,
        List<string> issues)
    {
        var reportArtifact = result?.Artifacts.FirstOrDefault(artifact =>
            Path.GetFileName(artifact.FilePath).Equals("build_report.json", StringComparison.OrdinalIgnoreCase));
        if (reportArtifact is null || !File.Exists(reportArtifact.FilePath))
        {
            issues.Add($"{PartFamilyFailureStages.FeatureResultInvalid}: source build_report.json is missing.");
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportArtifact.FilePath));
            if (!document.RootElement.TryGetProperty("feature_handler_reports", out var reports) ||
                reports.ValueKind != JsonValueKind.Array)
            {
                issues.Add($"{PartFamilyFailureStages.FeatureResultInvalid}: feature_handler_reports is missing.");
                return [];
            }

            return JsonSerializer.Deserialize<FeatureHandlerReport[]>(
                       reports.GetRawText(),
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true,
                           PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                       })
                   ?? [];
        }
        catch (JsonException ex)
        {
            issues.Add($"{PartFamilyFailureStages.FeatureResultInvalid}: feature handler report JSON is invalid: {ex.Message}");
            return [];
        }
    }

    private static FeatureExecutionReport CreateReport(
        string runId,
        FeatureExecutionSmokeRunnerOptions options,
        SolidWorksRuntimeOptions runtime,
        string reportPath,
        string modelPath,
        string stepPath,
        bool connected,
        bool realCadExecuted,
        string? solidWorksVersion,
        string? inputPath,
        string? buildPlanId,
        IReadOnlyList<FeatureHandlerReport> featureReports,
        string? failureStage,
        string finalStatus,
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues) =>
        new(
            runId,
            ExecutionMode,
            CandidateOnly: true,
            options.Enabled,
            runtime.ShouldUseRealWorker(dryRun: false),
            connected,
            realCadExecuted,
            solidWorksVersion,
            inputPath,
            buildPlanId,
            Evidence(modelPath),
            Evidence(stepPath),
            featureReports,
            MainWorkflowAccepted: false,
            QualityGatePassed: false,
            DeliverableStatus: "NotDeliverable",
            failureStage,
            finalStatus,
            logs,
            issues,
            reportPath,
            DateTimeOffset.UtcNow);

    private static FeatureExecutionArtifactEvidence Evidence(string path)
    {
        var info = new FileInfo(path);
        var valid = info.Exists && info.Length > 0;
        var status = valid ? "Passed" : "NotGenerated";
        if (valid &&
            path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase) &&
            !CadArtifactContentValidator.TryValidateStepFile(path, out _))
        {
            valid = false;
            status = "InvalidContent";
        }

        return new(
            path,
            info.Exists,
            info.Exists ? info.Length : 0,
            status);
    }

    private static async Task<FeatureExecutionReport> WriteAsync(
        FeatureExecutionReport report,
        CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(
            report.ReportPath,
            JsonSerializer.Serialize(
                report,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                }),
            cancellationToken);
        return report;
    }

    private static string ResolveFailureStage(Exception exception) =>
        exception switch
        {
            FileNotFoundException => "feature_execution_input_missing",
            JsonException => PartFamilyFailureStages.InvalidCadModelSpec,
            IOException or UnauthorizedAccessException => PartFamilyFailureStages.ArtifactValidationFailed,
            _ => "feature_execution_smoke_failed"
        };

    private static string FindProjectRoot(string startDirectory)
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

    private static void TryDeleteWorkingDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // The diagnostic report already contains normalized artifacts. A late
            // SolidWorks file handle must not turn CandidatePassed into main-flow success.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only; never broaden the diagnostic verdict.
        }
    }

    private sealed class DiagnosticCandidateHandler : IFeatureHandler
    {
        private readonly IFeatureHandler _inner;

        public DiagnosticCandidateHandler(IFeatureHandler inner, string reportPath)
        {
            _inner = inner;
            ApiEvidence = inner.ApiEvidence with
            {
                Status = FeatureApiEvidenceStatuses.Verified,
                EvidenceId = "v2.0-c-diagnostic-candidate-only",
                HandlerVersion = inner.HandlerVersion,
                ParameterProfile = "exact feature_execution_smoke input only",
                SolidWorksVersion = "diagnostic-runtime-pending",
                DiagnosticRunPath = reportPath,
                SourceRevision = FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision(),
                ProjectEvidence = inner.ApiEvidence.ProjectEvidence
                    .Append("Temporary diagnostic authorization; does not modify production Handler evidence.")
                    .ToArray()
            };
        }

        public string FeatureType => _inner.FeatureType;
        public string HandlerId => _inner.HandlerId;
        public string HandlerVersion => _inner.HandlerVersion;
        public string FailureStage => _inner.FailureStage;
        public IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => _inner.ParameterSchema;
        public FeatureApiEvidence ApiEvidence { get; }

        public bool CanHandle(FeatureDefinition feature) => _inner.CanHandle(feature);

        public FeatureHandlerValidationResult Validate(FeatureDefinition feature) =>
            _inner.Validate(feature);

        public FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(
            FeatureDefinition feature) =>
            _inner.ValidateParameterProfileForRealExecution(feature);

        public FeatureHandlerValidationResult ValidateEvidenceForRealExecution(
            FeatureDefinition feature) =>
            _inner.ValidateParameterProfileForRealExecution(feature);

        public FeatureHandlerValidationResult ValidateRuntimeForRealExecution(
            string? actualSolidWorksVersion) =>
            string.IsNullOrWhiteSpace(actualSolidWorksVersion)
                ? FeatureHandlerValidationResult.Failed(
                    PartFamilyFailureStages.FeatureApiUnverified,
                    $"{PartFamilyFailureStages.FeatureApiUnverified}: diagnostic runtime version is missing.")
                : FeatureHandlerValidationResult.Passed();

        public FeatureHandlerBuildPlanResult BuildPlan(
            FeatureDefinition feature,
            FeatureHandlerBuildPlanContext context) =>
            _inner.BuildPlan(feature, context);

        public Task<FeatureHandlerExecutionResult> ExecuteAsync(
            FeatureHandlerExecutionContext context,
            CancellationToken cancellationToken = default) =>
            _inner.ExecuteAsync(context, cancellationToken);

        public FeatureHandlerReport GenerateReport(
            FeatureDefinition feature,
            FeatureHandlerExecutionResult result)
        {
            var report = _inner.GenerateReport(feature, result);
            return report with
            {
                HandlerName = $"{report.HandlerName}/diagnostic-candidate",
                ApiEvidenceStatus = CandidateEvidenceStatus,
                EvidenceId = ApiEvidence.EvidenceId,
                EvidenceHandlerVersion = ApiEvidence.HandlerVersion,
                EvidenceParameterProfile = ApiEvidence.ParameterProfile,
                EvidenceSolidWorksVersion = ApiEvidence.SolidWorksVersion,
                EvidenceDiagnosticRunPath = ApiEvidence.DiagnosticRunPath,
                EvidenceSourceRevision = ApiEvidence.SourceRevision
            };
        }
    }
}
