using DomainSchemas;
using PlatformCore.Modules.CADModeling.Reviewers;
using PlatformCore.Modules.CADModeling.Skills;
using PlatformCore.Modules.CADModeling.Validators;
using QualityGate;
using SkillContracts;
using System.Reflection;
using WorkerContracts;

namespace PlatformCore;

public enum SolidWorksMainWorkflowStage
{
    BuildPlate,
    BuildPartFamily,
    CreateDrawing,
    AddDrawingDimensions,
    ApplyDrawingTitleBlock
}

public enum SolidWorksMainWorkflowOperation
{
    BuildPlate,
    BuildCompleteDrawingPackage,
    BuildPartFamilyReleasePackage
}

public sealed record SolidWorksMainWorkflowRequest(
    string RequestId,
    string TaskId,
    string ProjectRoot,
    string OutputDirectory,
    bool DryRun = true,
    bool AllowRealCadExecution = false,
    CADModelSpec? ModelSpec = null,
    SolidWorksMainWorkflowStage Stage = SolidWorksMainWorkflowStage.BuildPlate,
    string? SourcePartPath = null,
    string? SourceDrawingPath = null,
    string? SourceDimensionedDrawingPath = null,
    SolidWorksMainWorkflowOperation Operation = SolidWorksMainWorkflowOperation.BuildPlate,
    bool GenerateDrawing = false,
    bool GenerateDimensions = false,
    bool GenerateTitleBlock = false,
    bool GenerateReleasePackage = false,
    bool StructuredInputReceived = false,
    bool ChiefEngineerInvoked = false,
    bool GatewayInvoked = false,
    bool SolidWorksRouterTriggered = false);

public sealed record SolidWorksMainWorkflowResult(
    string RequestId,
    string WorkflowId,
    string Status,
    string WorkerName,
    string ExecutionMode,
    bool RequestFlagEnabled,
    bool EnvironmentFlagEnabled,
    bool MainWorkflowEnvironmentFlagEnabled,
    bool RealCadRequested,
    bool RealCadExecuted,
    bool RealCadConnected,
    bool QualityGatePassed,
    string QualityGateDecision,
    string OutputDirectory,
    IReadOnlyList<ArtifactInfo> Artifacts,
    IReadOnlyList<string> ArtifactPaths,
    IReadOnlyList<string> Logs,
    IReadOnlyList<string> Issues,
    string? FailureStage,
    WorkflowExecutionResult WorkflowResult);

public sealed partial class SolidWorksMainWorkflowRunner
{
    private const string FakeWorkerName = "FakeSolidWorksWorker";
    private const string RealWorkerTypeName = "SolidWorksWorker.RealSolidWorksWorker, SolidWorksWorker";

    private readonly SkillRegistry _skillRegistry;
    private readonly WorkerRegistry _workerRegistry;
    private readonly InMemoryAuditLog _auditLog;
    private readonly SequentialWorkflowEngine _workflowEngine;
    private readonly Func<SolidWorksRuntimeOptions> _runtimeOptionsProvider;

    public SolidWorksMainWorkflowRunner(
        SkillRegistry skillRegistry,
        WorkerRegistry workerRegistry,
        InMemoryAuditLog auditLog,
        SequentialWorkflowEngine? workflowEngine = null,
        Func<SolidWorksRuntimeOptions>? runtimeOptionsProvider = null)
    {
        _skillRegistry = skillRegistry;
        _workerRegistry = workerRegistry;
        _auditLog = auditLog;
        _workflowEngine = workflowEngine ?? new SequentialWorkflowEngine(SequentialWorkflowEngine.CreateDefaultRetryPolicy(), auditLog);
        _runtimeOptionsProvider = runtimeOptionsProvider ?? (() => SolidWorksRuntimeOptions.FromEnvironment());
    }

    public Task<SolidWorksMainWorkflowResult> ExecuteAsync(
        SolidWorksMainWorkflowRequest request,
        CancellationToken cancellationToken = default) =>
        request.Operation == SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage
            ? ExecuteCompleteDrawingPackageAsync(request, cancellationToken)
            : request.Operation == SolidWorksMainWorkflowOperation.BuildPartFamilyReleasePackage
                ? ExecutePartFamilyReleasePackageAsync(request, cancellationToken)
                : ExecuteSingleStageAsync(request, cancellationToken);

    private async Task<SolidWorksMainWorkflowResult> ExecuteSingleStageAsync(
        SolidWorksMainWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SolidWorksBuildPlan? plan = null;
        SolidWorksWorkerRequest? workerRequest = null;
        SolidWorksWorkerResult? workerResult = null;
        ReviewReport? buildPlanValidation = null;
        ReviewReport? buildPlanReview = null;
        ReviewReport? artifactValidation = null;
        GateEvaluationResult? finalGate = null;
        var logs = new List<string>();
        var issues = new List<string>();
        var runtimeOptions = _runtimeOptionsProvider();
        var requestAllowsReal = request.AllowRealCadExecution && !request.DryRun;
        var realExecutionFlagsSatisfied = requestAllowsReal &&
            runtimeOptions.EnableRealExecution &&
            runtimeOptions.MainWorkflowExecutionEnabled;
        var partType = request.ModelSpec?.PartType ?? PlateBasic4HolesDefinition.Type;
        var localAuthorizationRequired = realExecutionFlagsSatisfied &&
            !string.Equals(partType, PlateBasic4HolesDefinition.Type, StringComparison.OrdinalIgnoreCase);
        var localAuthorization = localAuthorizationRequired
            ? SolidWorksLocalExecutionProfile.Load(request.ProjectRoot)
            : null;
        var realExecutionAllowed = realExecutionFlagsSatisfied && localAuthorization?.IsAuthorized != false;
        var workerName = realExecutionFlagsSatisfied ? "RealSolidWorksWorker" : FakeWorkerName;
        var workflowId = $"solidworks-main-workflow-{request.TaskId}";
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);

        var steps = new List<WorkflowStep>();
        if (localAuthorizationRequired)
        {
            steps.Add(new WorkflowStep(
                "SolidWorks local execution authorization",
                _ =>
                {
                    if (localAuthorization?.IsAuthorized == true)
                    {
                        return Task.FromResult(StepPassed(
                            "solidworks-local-execution-authorization",
                            "LocalDevelopmentProfile authorization passed for non-plate real execution."));
                    }

                    var authorizationIssues = (localAuthorization?.Issues ?? Array.Empty<string>())
                        .Append($"{PartFamilyFailureStages.LocalExecutionAuthorizationMissing}: LocalDevelopmentProfile authorization is required for non-plate real builds.")
                        .ToArray();
                    issues.AddRange(authorizationIssues);
                    return Task.FromResult(StepFailed(
                        "solidworks-local-execution-authorization",
                        "Non-plate real execution was rejected before worker invocation.",
                        authorizationIssues,
                        fatal: true));
                },
                "solidworks-local-execution-authorization"));
        }

        steps.AddRange(
        [
            new WorkflowStep(
                "SolidWorks build plan generation",
                async _ =>
                {
                    var skill = ResolveBuildPlanSkill();
                    var skillOutput = await skill.ExecuteAsync(new SkillInput(
                        request.TaskId,
                        nameof(CADModelSpec),
                        request.ModelSpec ?? CreateDefaultPlateSpec(),
                        new Dictionary<string, string>
                        {
                            ["workflow"] = "solidworks-main-workflow",
                            ["part_name"] = request.ModelSpec?.PartType ?? PlateBasic4HolesDefinition.Type
                        }));

                    logs.AddRange(skillOutput.Logs);
                    issues.AddRange(skillOutput.Issues);
                    plan = skillOutput.Result as SolidWorksBuildPlan;
                    if (skillOutput.Status != SkillOutputStatus.Completed || plan is null)
                    {
                        var stepIssues = issues.Append("solidworks_build_plan_generation_failed").ToArray();
                        return StepFailed(
                            "solidworks-build-plan-generation",
                            "SolidWorks build plan generation failed.",
                            stepIssues,
                            fatal: true);
                    }

                    return StepPassed(
                        "solidworks-build-plan-generation",
                        "SolidWorks build plan generated.",
                        logs: skillOutput.Logs);
                },
                "solidworks-build-plan-generation"),
            new WorkflowStep(
                "SolidWorks build plan validation and review",
                _ =>
                {
                    if (plan is null)
                    {
                        return Task.FromResult(StepFailed(
                            "solidworks-build-plan-validation",
                            "SolidWorks build plan is missing.",
                            new[] { "solidworks_build_plan_missing" },
                            fatal: true));
                    }

                    workerRequest = CreateWorkerRequest(request, plan, outputDirectory, realExecutionAllowed);

                    buildPlanValidation = new SolidWorksBuildPlanValidator().Validate(workerRequest);
                    buildPlanReview = new SolidWorksBuildPlanReviewer().Review(plan);
                    var stepIssues = buildPlanValidation.Issues
                        .Concat(buildPlanReview.Issues)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    issues.AddRange(stepIssues);

                    var passed = buildPlanValidation.IsPassed && buildPlanReview.IsPassed;
                    return Task.FromResult(passed
                        ? StepPassed(
                            "solidworks-build-plan-validation",
                            "SolidWorks build plan validation and review passed.",
                            reviewReport: buildPlanReview)
                        : StepRejected(
                            "solidworks-build-plan-validation",
                            "SolidWorks build plan validation or review failed.",
                            stepIssues,
                            buildPlanReview));
                },
                "solidworks-build-plan-validation"),
            new WorkflowStep(
                "SolidWorks worker execution",
                async _ =>
                {
                    if (workerRequest is null)
                    {
                        return StepFailed(
                            "solidworks-worker-execution",
                            "SolidWorks worker request is missing.",
                            new[] { "solidworks_worker_request_missing" },
                            fatal: true);
                    }

                    if (!realExecutionAllowed && requestAllowsReal)
                    {
                        logs.Add("Real CAD request-level switch was present, but SW_ENABLE_REAL_EXECUTION=true and SW_REAL_MAIN_WORKFLOW_TEST=true were not both present; main workflow stayed on FakeSolidWorksWorker.");
                    }
                    else if (!realExecutionAllowed)
                    {
                        logs.Add("Main workflow used FakeSolidWorksWorker because real CAD execution was not requested.");
                    }

                    workerResult = realExecutionAllowed
                        ? await InvokeRealWorkerAsync(workerRequest, runtimeOptions, cancellationToken)
                        : await InvokeRegisteredFakeWorkerAsync(workerRequest, cancellationToken);
                    logs.AddRange(workerResult.Logs);
                    issues.AddRange(workerResult.Issues);

                    var completed = string.Equals(workerResult.Status, "Completed", StringComparison.OrdinalIgnoreCase);
                    return completed
                        ? StepPassed(
                            "solidworks-worker-execution",
                            $"SolidWorks worker execution completed in {workerResult.ExecutionMode} mode.",
                            logs: workerResult.Logs)
                        : StepFailed(
                            "solidworks-worker-execution",
                            $"SolidWorks worker execution ended with {workerResult.Status}.",
                            workerResult.Issues,
                            fatal: true);
                },
                "solidworks-worker-execution"),
            new WorkflowStep(
                "SolidWorks artifact validation and quality gate",
                _ =>
                {
                    if (plan is null || workerResult is null)
                    {
                        return Task.FromResult(StepFailed(
                            "solidworks-artifact-quality-gate",
                            "SolidWorks artifact validation cannot run without worker result.",
                            new[] { "solidworks_worker_result_missing" },
                            fatal: true));
                    }

                    artifactValidation = new SolidWorksArtifactValidator(Path.Combine(request.ProjectRoot, "output", "solidworks"))
                        .Validate(workerResult);
                    var finalReview = BuildFinalReview(
                        buildPlanValidation,
                        buildPlanReview,
                        artifactValidation,
                        workerResult);
                    finalGate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(finalReview);
                    var stepIssues = finalReview.Issues.ToArray();
                    issues.AddRange(stepIssues);
                    _auditLog.Record(
                        "quality-gate",
                        "solidworks-main-workflow",
                        "quality_gate_after_solidworks_main_workflow",
                        $"QualityGate evaluated SolidWorks main workflow as {finalGate.Decision.Result}.");

                    return Task.FromResult(new WorkflowStepResult(
                        "solidworks-artifact-quality-gate",
                        "SolidWorks artifact validation and quality gate",
                        ToWorkflowStepStatus(finalGate.Decision.Result),
                        $"SolidWorks main workflow QualityGate decision: {finalGate.Decision.Result}.",
                        ReviewReport: finalReview,
                        GateDecision: finalGate.Decision,
                        RejectReport: finalGate.RejectReport,
                        Logs: new[] { $"QualityGate evaluated SolidWorks main workflow as {finalGate.Decision.Result}." },
                        Issues: stepIssues));
                },
                "solidworks-artifact-quality-gate")
        ]);

        var workflowResult = await _workflowEngine.ExecuteAsync(
            steps,
            new WorkflowContext(workflowId, new Dictionary<string, object?>
            {
                ["request_id"] = request.RequestId,
                ["part_name"] = request.ModelSpec?.PartType ?? PlateBasic4HolesDefinition.Type,
                ["stage"] = request.Stage.ToString(),
                ["real_cad_requested"] = requestAllowsReal,
                ["real_cad_env_enabled"] = runtimeOptions.EnableRealExecution,
                ["real_cad_main_workflow_env_enabled"] = runtimeOptions.MainWorkflowExecutionEnabled
            }),
            cancellationToken);

        var artifacts = workerResult?.GeneratedArtifacts
            .Select(ToArtifactInfo)
            .Append(BuildWorkflowReportArtifact(
                request,
                workflowResult,
                workerName,
                workerResult,
                finalGate,
                runtimeOptions.EnableRealExecution,
                requestAllowsReal,
                outputDirectory))
            .ToArray() ??
            new[] { BuildWorkflowReportArtifact(request, workflowResult, workerName, null, finalGate, runtimeOptions.EnableRealExecution, requestAllowsReal, outputDirectory) };

        var finalDecision = finalGate?.Decision ?? workflowResult.FinalGateDecision;
        var qualityGatePassed = finalDecision?.Result == GateDecisionResult.Passed && workflowResult.Status == WorkflowStatus.Passed;
        var distinctIssues = issues
            .Concat(workflowResult.Steps.SelectMany(step => step.Issues))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var distinctLogs = logs
            .Concat(workflowResult.Steps.SelectMany(step => step.Logs))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SolidWorksMainWorkflowResult(
            request.RequestId,
            workflowId,
            workflowResult.Status == WorkflowStatus.Passed ? "Completed" : workflowResult.Status.ToString(),
            workerName,
            workerResult?.ExecutionMode ?? (realExecutionAllowed ? "RealNotStarted" : "FakeNotStarted"),
            requestAllowsReal,
            runtimeOptions.EnableRealExecution,
            runtimeOptions.MainWorkflowExecutionEnabled,
            requestAllowsReal,
            workerResult?.RealCadExecuted == true,
            workerResult?.RealCadConnected == true,
            qualityGatePassed,
            finalDecision?.Result.ToString() ?? "Unknown",
            outputDirectory,
            artifacts,
            artifacts
                .Where(artifact => !artifact.Path.StartsWith("memory://", StringComparison.OrdinalIgnoreCase))
                .Select(artifact => artifact.Path)
                .ToArray(),
            distinctLogs,
            distinctIssues,
            ResolveFailureStage(workflowResult, workerResult, artifactValidation),
            workflowResult);
    }

    private ISkill ResolveBuildPlanSkill() =>
        _skillRegistry.GetByName("solidworks-build-plan-skill") ?? new SolidWorksBuildPlanSkill();

    private static SolidWorksWorkerRequest CreateWorkerRequest(
        SolidWorksMainWorkflowRequest request,
        SolidWorksBuildPlan plan,
        string outputDirectory,
        bool realExecutionAllowed) =>
        new(
            request.RequestId,
            plan,
            outputDirectory,
            DryRun: !realExecutionAllowed,
            AllowRealCadExecution: realExecutionAllowed,
            DrawingSmokeTestOnly: request.Stage == SolidWorksMainWorkflowStage.CreateDrawing,
            SourcePartPath: request.SourcePartPath,
            DrawingDimensionSmokeTestOnly: request.Stage == SolidWorksMainWorkflowStage.AddDrawingDimensions,
            SourceDrawingPath: request.SourceDrawingPath,
            DrawingTitleBlockSmokeTestOnly: request.Stage == SolidWorksMainWorkflowStage.ApplyDrawingTitleBlock,
            SourceDimensionedDrawingPath: request.SourceDimensionedDrawingPath);

    private async Task<SolidWorksWorkerResult> InvokeRegisteredFakeWorkerAsync(
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken)
    {
        var worker = _workerRegistry.GetByName(FakeWorkerName)
            ?? throw new InvalidOperationException("FakeSolidWorksWorker is not registered.");
        return await InvokeTypedWorkerAsync(worker, request, cancellationToken);
    }

    private static async Task<SolidWorksWorkerResult> InvokeRealWorkerAsync(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions runtimeOptions,
        CancellationToken cancellationToken)
    {
        var type = Type.GetType(RealWorkerTypeName, throwOnError: false)
            ?? throw new TypeLoadException("RealSolidWorksWorker was not found.");
        var worker = Activator.CreateInstance(type, new object?[] { null, runtimeOptions })
            ?? throw new InvalidOperationException("Could not create RealSolidWorksWorker.");
        return await InvokeTypedWorkerAsync(worker, request, cancellationToken);
    }

    private static async Task<SolidWorksWorkerResult> InvokeTypedWorkerAsync(
        object worker,
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken)
    {
        var method = worker.GetType().GetMethods()
            .SingleOrDefault(method =>
                method.Name == "ExecuteAsync" &&
                method.GetParameters().Length == 2 &&
                method.GetParameters()[0].ParameterType == typeof(SolidWorksWorkerRequest) &&
                method.GetParameters()[1].ParameterType == typeof(CancellationToken))
            ?? throw new MissingMethodException(worker.GetType().FullName, "ExecuteAsync(SolidWorksWorkerRequest, CancellationToken)");

        var task = method.Invoke(worker, new object?[] { request, cancellationToken }) as Task<SolidWorksWorkerResult>
            ?? throw new InvalidOperationException($"{worker.GetType().Name}.ExecuteAsync did not return Task<SolidWorksWorkerResult>.");
        return await task;
    }

    private static ReviewReport BuildFinalReview(
        ReviewReport? buildPlanValidation,
        ReviewReport? buildPlanReview,
        ReviewReport? artifactValidation,
        SolidWorksWorkerResult workerResult)
    {
        var issues = new[]
            {
                buildPlanValidation,
                buildPlanReview,
                artifactValidation
            }
            .Where(report => report is not null)
            .SelectMany(report => report!.Issues)
            .Concat(workerResult.Issues)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var passed =
            string.Equals(workerResult.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
            buildPlanValidation?.IsPassed == true &&
            buildPlanReview?.IsPassed == true &&
            artifactValidation?.IsPassed == true &&
            issues.Length == 0;
        var fatal =
            buildPlanValidation?.HasFatalError == true ||
            artifactValidation?.HasFatalError == true ||
            string.Equals(workerResult.Status, "Failed", StringComparison.OrdinalIgnoreCase);

        return new ReviewReport(
            $"solidworks-main-workflow-review-{Guid.NewGuid():N}",
            "solidworks-main-workflow-reviewer",
            passed,
            passed ? 0.95 : 0.2,
            issues,
            RequiresHumanApproval: false,
            HasFatalError: fatal);
    }

    private static WorkflowStepResult StepPassed(
        string stepId,
        string message,
        ReviewReport? reviewReport = null,
        IReadOnlyList<string>? logs = null) =>
        new(
            stepId,
            stepId,
            WorkflowStepStatus.Passed,
            message,
            ReviewReport: reviewReport,
            GateDecision: new GateDecision($"gate-{stepId}-{Guid.NewGuid():N}", GateDecisionResult.Passed, message),
            Logs: logs ?? Array.Empty<string>());

    private static WorkflowStepResult StepRejected(
        string stepId,
        string message,
        IReadOnlyList<string> issues,
        ReviewReport? reviewReport) =>
        new(
            stepId,
            stepId,
            WorkflowStepStatus.Rejected,
            message,
            ReviewReport: reviewReport,
            GateDecision: new GateDecision($"gate-{stepId}-{Guid.NewGuid():N}", GateDecisionResult.Rejected, message),
            Issues: issues);

    private static WorkflowStepResult StepFailed(
        string stepId,
        string message,
        IReadOnlyList<string> issues,
        bool fatal) =>
        new(
            stepId,
            stepId,
            WorkflowStepStatus.Failed,
            message,
            ReviewReport: new ReviewReport(
                $"review-{stepId}-{Guid.NewGuid():N}",
                "solidworks-main-workflow-runner",
                false,
                0.0,
                issues,
                RequiresHumanApproval: false,
                HasFatalError: fatal),
            GateDecision: new GateDecision($"gate-{stepId}-{Guid.NewGuid():N}", GateDecisionResult.Failed, message),
            Issues: issues);

    private static WorkflowStepStatus ToWorkflowStepStatus(GateDecisionResult result) =>
        result switch
        {
            GateDecisionResult.Passed => WorkflowStepStatus.Passed,
            GateDecisionResult.Rejected => WorkflowStepStatus.Rejected,
            GateDecisionResult.Failed => WorkflowStepStatus.Failed,
            GateDecisionResult.NeedsHumanApproval => WorkflowStepStatus.WaitingForHumanApproval,
            _ => WorkflowStepStatus.Failed
        };

    private static CADModelSpec CreateDefaultPlateSpec() =>
        SolidWorksWorkflowRouter.CreatePlateBasicFourHolesSpec();

    private static ArtifactInfo ToArtifactInfo(SolidWorksArtifact artifact) =>
        new(
            artifact.ArtifactId,
            artifact.ArtifactType,
            artifact.Description,
            artifact.FilePath,
            InferMediaType(artifact.ExpectedExtension),
            new Dictionary<string, string>
            {
                ["exists"] = artifact.Exists.ToString(),
                ["size_bytes"] = artifact.SizeBytes.ToString()
            });

    private static ArtifactInfo BuildWorkflowReportArtifact(
        SolidWorksMainWorkflowRequest request,
        WorkflowExecutionResult workflowResult,
        string workerName,
        SolidWorksWorkerResult? workerResult,
        GateEvaluationResult? gate,
        bool environmentFlagEnabled,
        bool requestFlagEnabled,
        string outputDirectory) =>
        new(
            $"solidworks-main-workflow-report-{Guid.NewGuid():N}",
            "solidworks-main-workflow-report",
            "solidworks-main-workflow-report",
            $"memory://solidworks-main-workflow/{request.RequestId}",
            "application/json",
            new Dictionary<string, string>
            {
                ["workflow_id"] = workflowResult.WorkflowId,
                ["worker_name"] = workerName,
                ["stage"] = request.Stage.ToString(),
                ["execution_mode"] = workerResult?.ExecutionMode ?? "NotStarted",
                ["real_cad_requested"] = requestFlagEnabled.ToString(),
                ["sw_enable_real_execution"] = environmentFlagEnabled.ToString(),
                ["real_cad_executed"] = (workerResult?.RealCadExecuted == true).ToString(),
                ["quality_gate_passed"] = (gate?.Decision.Result == GateDecisionResult.Passed && workflowResult.Status == WorkflowStatus.Passed).ToString(),
                ["output_directory"] = outputDirectory
            });

    private static string? ResolveFailureStage(
        WorkflowExecutionResult workflowResult,
        SolidWorksWorkerResult? workerResult,
        ReviewReport? artifactValidation)
    {
        if (workflowResult.Status == WorkflowStatus.Passed)
        {
            return null;
        }

        if (workerResult?.PreflightReport?.FinalStatus == "Failed")
        {
            return "preflight_failed";
        }

        if (!string.IsNullOrWhiteSpace(workerResult?.FailureStage))
        {
            return workerResult.FailureStage;
        }

        var preciseStage = workflowResult.Steps
            .SelectMany(step => step.Issues)
            .Select(TryReadPartFamilyFailureStage)
            .FirstOrDefault(stage => stage is not null);
        if (preciseStage is not null)
        {
            return preciseStage;
        }

        if (artifactValidation is { IsPassed: false })
        {
            return PartFamilyFailureStages.ArtifactValidationFailed;
        }

        if (workflowResult.FailureReport is not null)
        {
            return workflowResult.FailureReport.FailedStepId switch
            {
                "solidworks-build-plan-generation" => "build_plan_generation_failed",
                "solidworks-build-plan-validation" => "build_plan_validation_failed",
                "solidworks-worker-execution" => "worker_execution_failed",
                "solidworks-artifact-quality-gate" => "quality_gate_failed",
                _ => "main_workflow_failed"
            };
        }

        return "main_workflow_failed";
    }

    private static string? TryReadPartFamilyFailureStage(string issue)
    {
        var stages = new[]
        {
            PartFamilyFailureStages.UnsupportedPartType,
            PartFamilyFailureStages.MissingRequiredParameter,
            PartFamilyFailureStages.InvalidParameterValue,
            PartFamilyFailureStages.PartFamilyDefinitionMissing,
            PartFamilyFailureStages.BuildPlanGenerationFailed,
            PartFamilyFailureStages.PartFamilyBuilderMissing,
            PartFamilyFailureStages.FlangeBuildFailed,
            PartFamilyFailureStages.ShaftBuildFailed,
            PartFamilyFailureStages.LocalExecutionAuthorizationMissing,
            PartFamilyFailureStages.ArtifactValidationFailed
        };

        return stages.FirstOrDefault(stage =>
            issue.Equals(stage, StringComparison.OrdinalIgnoreCase) ||
            issue.StartsWith($"{stage}:", StringComparison.OrdinalIgnoreCase));
    }

    private static string InferMediaType(string extension) =>
        extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
                ? "application/pdf"
                : "text/plain";
}
