using AgentContracts;
using DomainSchemas;
using PlatformCore;
using System.Collections.Concurrent;

namespace PlatformCore.Modules.RequirementUnderstanding.Agents;

public sealed class ChiefEngineerOrchestrator
{
    private readonly InternalAgentRouter _router;
    private readonly AgentRegistry _agentRegistry;
    private readonly InMemoryAuditLog _auditLog;
    private readonly SequentialWorkflowEngine _workflowEngine;
    private readonly SolidWorksMainWorkflowRunner? _solidWorksMainWorkflowRunner;
    private readonly SolidWorksWorkflowRouter _solidWorksWorkflowRouter;
    private readonly IReadOnlyList<string> _internalRoute;
    private readonly ConcurrentDictionary<string, AgentContext> _pendingContexts = new(StringComparer.Ordinal);

    public ChiefEngineerOrchestrator(
        InternalAgentRouter router,
        AgentRegistry agentRegistry,
        InMemoryAuditLog auditLog,
        SequentialWorkflowEngine? workflowEngine = null,
        SolidWorksMainWorkflowRunner? solidWorksMainWorkflowRunner = null,
        SolidWorksWorkflowRouter? solidWorksWorkflowRouter = null,
        InternalWorkflowRoute? internalWorkflowRoute = null)
    {
        _router = router;
        _agentRegistry = agentRegistry;
        _auditLog = auditLog;
        _workflowEngine = workflowEngine ?? new SequentialWorkflowEngine(SequentialWorkflowEngine.CreateDefaultRetryPolicy(), auditLog);
        _solidWorksMainWorkflowRunner = solidWorksMainWorkflowRunner;
        _solidWorksWorkflowRouter = solidWorksWorkflowRouter ?? new SolidWorksWorkflowRouter();
        var route = internalWorkflowRoute ?? InternalWorkflowRoute.EngineeringDefault;
        var routeIssues = route.Validate();
        if (route.AgentIds.Count == 0 || routeIssues.Count > 0)
        {
            throw new ArgumentException(
                routeIssues.Count == 0
                    ? "internal_workflow_route must contain at least one internal agent."
                    : string.Join(" ", routeIssues),
                nameof(internalWorkflowRoute));
        }

        _internalRoute = route.AgentIds.ToArray();
    }

    public async Task<AgentOutput> ExecuteAsync(AgentContext context, string rootAgentId, string rootAgentName)
    {
        var inputIssues = SolidWorksWorkflowRouter.ValidateStructuredInput(context);
        if (inputIssues.Count > 0)
        {
            _auditLog.Record("workflow", context.TaskId, "structured_cad_input_rejected", string.Join(" ", inputIssues));
            return new AgentOutput(AgentOutputStatus.Failed, "结构化 CAD 输入无效，已停止执行。",
                [], inputIssues, ["structured_cad_input_rejected_before_workflow"], "error-diagnosis",
                ReviewReport: new ReviewReport($"input-{context.TaskId}", "cad-input-validator", false, 0,
                    inputIssues, RequiresHumanApproval: false, HasFatalError: true));
        }

        var workflowId = $"internal-collaboration-{context.TaskId}";
        var workflowSteps = _internalRoute
            .Select(agentId => new InternalAgentWorkflowStep(agentId, context, _router, _agentRegistry, _auditLog).ToWorkflowStep())
            .ToArray();
        var workflowResult = await _workflowEngine.ExecuteAsync(
            workflowSteps,
            new WorkflowContext(workflowId, new Dictionary<string, object?>
            {
                ["root_agent_id"] = rootAgentId,
                ["conversation_id"] = context.Input.ConversationId
            }));

        return await CompleteWorkflowAsync(context, rootAgentId, rootAgentName, workflowResult);
    }

    public async Task<AgentApprovalResult> ResumeHumanApprovalAsync(string taskId, string rootAgentId, string rootAgentName,
        WorkflowApprovalSubmission submission, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_pendingContexts.TryGetValue(taskId, out var context) ||
            !string.Equals(submission.WorkflowId, $"internal-collaboration-{taskId}", StringComparison.Ordinal))
            return new(false, null, "chief_approval_context_not_pending");

        var resumed = await _workflowEngine.SubmitHumanApprovalAsync(submission, cancellationToken);
        if (!resumed.Accepted || resumed.WorkflowResult is null) return new(false, null, resumed.FailureReason);
        return new(true, await CompleteWorkflowAsync(context, rootAgentId, rootAgentName, resumed.WorkflowResult));
    }

    private async Task<AgentOutput> CompleteWorkflowAsync(AgentContext context, string rootAgentId, string rootAgentName,
        WorkflowExecutionResult workflowResult)
    {
        if (workflowResult.Status == WorkflowStatus.WaitingForHumanApproval) _pendingContexts[context.TaskId] = context;
        else _pendingContexts.TryRemove(context.TaskId, out _);

        var report = BuildReport(context, rootAgentId, workflowResult);
        var solidWorksMainWorkflowResult = await TryRunSolidWorksMainWorkflowAsync(context, workflowResult);
        if (solidWorksMainWorkflowResult is not null)
        {
            report = EnrichReportWithSolidWorksMainWorkflow(report, solidWorksMainWorkflowResult);
        }
        _auditLog.Record("agent", rootAgentId, "collaboration_report_created", $"Internal workflow-backed collaboration report created for {context.Input.ConversationId}.");

        var artifact = new ArtifactInfo(
            $"collaboration-{Guid.NewGuid():N}",
            "internal-collaboration-report",
            "internal-collaboration-report",
            $"memory://internal-collaboration/{context.Input.ConversationId}",
            "application/json");

        var status = workflowResult.Status switch
        {
            WorkflowStatus.Passed => AgentOutputStatus.Completed,
            WorkflowStatus.WaitingForHumanApproval => AgentOutputStatus.NeedsHumanApproval,
            WorkflowStatus.Failed => AgentOutputStatus.Failed,
            WorkflowStatus.Rejected => AgentOutputStatus.Rejected,
            _ => AgentOutputStatus.Failed
        };
        if (solidWorksMainWorkflowResult is not null && !solidWorksMainWorkflowResult.QualityGatePassed)
        {
            status = AgentOutputStatus.Failed;
        }

        return new AgentOutput(
            status,
            BuildOutputMessage(rootAgentName, workflowResult, solidWorksMainWorkflowResult),
            new[] { artifact }.Concat(report.Artifacts).ToArray(),
            report.Issues,
            new[] { "Chief engineer orchestrated internal agents through SequentialWorkflowEngine and QualityGate." }
                .Concat(solidWorksMainWorkflowResult?.Logs ?? Array.Empty<string>())
                .ToArray(),
            report.CalledAgents.LastOrDefault()?.NextRecommendedAgentId,
            report,
            ResolveFinalReviewReport(workflowResult));
    }

    private InternalCollaborationReport BuildReport(
        AgentContext context,
        string rootAgentId,
        WorkflowExecutionResult workflowResult)
    {
        var finalStepByAgent = workflowResult.Steps
            .Where(step => step.AgentOutput is not null)
            .GroupBy(step => AgentIdFromStepId(step.StepId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var calledAgents = new List<CalledAgentSummary>();
        var snapshots = new List<AgentOutputSnapshot>();
        var artifacts = new List<ArtifactInfo>();
        var issues = new List<string>();

        foreach (var agentId in _internalRoute.Where(finalStepByAgent.ContainsKey))
        {
            var agent = _agentRegistry.GetById(agentId)
                ?? throw new InvalidOperationException($"Internal agent '{agentId}' was not found while building collaboration report.");
            var step = finalStepByAgent[agentId];
            var output = EffectiveOutput(step)!;

            calledAgents.Add(new CalledAgentSummary(
                agent.Id,
                agent.Name,
                agent.Role.Description,
                output.Status.ToString(),
                output.Message,
                output.NextRecommendedAgentId));

            snapshots.Add(new AgentOutputSnapshot(
                agent.Id,
                output.Status.ToString(),
                output.Message,
                output.Artifacts,
                output.Issues,
                output.NextRecommendedAgentId,
                output.ReviewReport ?? step.ReviewReport));

            artifacts.AddRange(output.Artifacts);
            issues.AddRange(EffectiveIssues(step));
        }

        if (workflowResult.FailureReport is not null)
        {
            issues.Add(workflowResult.FailureReport.FailureReason);
        }

        if (workflowResult.HumanApprovalRequest is not null)
        {
            issues.Add(workflowResult.HumanApprovalRequest.Reason);
        }

        var stepResults = workflowResult.Steps
            .Select(step => new InternalWorkflowStepSummary(
                step.StepId,
                step.StepName,
                AgentIdFromStepId(step.StepId),
                step.Status.ToString(),
                step.GateDecision,
                step.RetryCount,
                step.MaxRetries,
                EffectiveIssues(step),
                step.Logs,
                step.ApprovalResolution))
            .ToArray();
        var retryStepIds = workflowResult.Steps
            .Where(step => step.Status == WorkflowStepStatus.Retrying)
            .Select(step => step.StepId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var summary = workflowResult.Status switch
        {
            WorkflowStatus.Passed => "Internal agents completed the workflow-backed mechanical engineering planning route.",
            WorkflowStatus.WaitingForHumanApproval => $"Internal workflow is waiting for human approval at {workflowResult.HumanApprovalRequest?.StepId}.",
            WorkflowStatus.Failed => $"Internal workflow failed at {workflowResult.FailureReport?.FailedStepId}.",
            WorkflowStatus.Rejected => $"Internal workflow was rejected at {workflowResult.Steps.LastOrDefault()?.StepId}.",
            _ => $"Internal workflow ended with status {workflowResult.Status}."
        };
        var recommendation = workflowResult.Status switch
        {
            WorkflowStatus.Passed => "Proceed through the platform workflow boundary before any CAD Worker execution.",
            WorkflowStatus.WaitingForHumanApproval => "Pause automatic execution until human approval is recorded.",
            WorkflowStatus.Failed => "Route to error-diagnosis before any downstream work.",
            WorkflowStatus.Rejected => "Review RejectReport and retry policy before continuing.",
            _ => "Inspect internal workflow result."
        };

        return new InternalCollaborationReport(
            context.Input.ConversationId,
            rootAgentId,
            calledAgents,
            snapshots,
            issues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            artifacts,
            summary,
            recommendation,
            workflowResult.WorkflowId,
            workflowResult.Status.ToString(),
            stepResults,
            workflowResult.FinalGateDecision,
            new RetrySummary(retryStepIds.Length, retryStepIds),
            workflowResult.FailureReport,
            workflowResult.HumanApprovalRequest);
    }

    private static ReviewReport? ResolveFinalReviewReport(WorkflowExecutionResult workflowResult)
    {
        var step = workflowResult.Steps.LastOrDefault(step => step.ReviewReport is not null);
        return step is null ? null : EffectiveOutput(step)?.ReviewReport ?? step.ReviewReport;
    }

    private static bool IsApproved(WorkflowStepResult step) =>
        step is { Status: WorkflowStepStatus.Passed, GateDecision.Result: GateDecisionResult.Passed,
            ApprovalResolution.Decision: "Approve" };

    private static ReviewReport ApprovedReview(WorkflowStepResult step) =>
        new($"human-approved-{step.ApprovalResolution!.ApprovalRequestId}", step.ApprovalResolution.SubmittedBy,
            true, 1.0, [], false, false);

    private static IReadOnlyList<string> EffectiveIssues(WorkflowStepResult step) =>
        IsApproved(step) ? [] : IsDeclined(step) ? [$"human_approval_rejected: {step.GateDecision!.Reason}"] : step.Issues;

    private static bool IsDeclined(WorkflowStepResult step) =>
        step is { Status: WorkflowStepStatus.Rejected, GateDecision.Result: GateDecisionResult.Rejected,
            ApprovalResolution.Decision: "Reject" or "RequestRevision" };

    private static AgentOutput? EffectiveOutput(WorkflowStepResult step)
    {
        if (step.AgentOutput is null) return null;
        if (IsApproved(step)) return step.AgentOutput with { Status = AgentOutputStatus.Completed, Issues = [], ReviewReport = ApprovedReview(step) };
        if (IsDeclined(step)) return step.AgentOutput with
        {
            Status = AgentOutputStatus.Rejected, Issues = EffectiveIssues(step),
            ReviewReport = new ReviewReport($"human-rejected-{step.ApprovalResolution!.ApprovalRequestId}",
                step.ApprovalResolution.SubmittedBy, false, 0.4, EffectiveIssues(step), false, false)
        };
        return step.AgentOutput;
    }

    private static string AgentIdFromStepId(string stepId) =>
        stepId.StartsWith("internal-agent:", StringComparison.OrdinalIgnoreCase)
            ? stepId["internal-agent:".Length..]
            : stepId;

    private async Task<SolidWorksMainWorkflowResult?> TryRunSolidWorksMainWorkflowAsync(
        AgentContext context,
        WorkflowExecutionResult internalWorkflowResult)
    {
        if (_solidWorksMainWorkflowRunner is null ||
            internalWorkflowResult.Status != WorkflowStatus.Passed)
        {
            return null;
        }

        var request = _solidWorksWorkflowRouter.TryBuildRequest(context);
        if (request is null)
        {
            return null;
        }

        var result = await _solidWorksMainWorkflowRunner.ExecuteAsync(request);

        _auditLog.Record(
            "workflow",
            "solidworks-main-workflow",
            "solidworks_main_workflow_completed",
            $"SolidWorks main workflow completed with status {result.Status}, real_cad_executed={result.RealCadExecuted}, quality_gate_passed={result.QualityGatePassed}.");
        return result;
    }

    private static InternalCollaborationReport EnrichReportWithSolidWorksMainWorkflow(
        InternalCollaborationReport report,
        SolidWorksMainWorkflowResult result)
    {
        var summary = $"{report.Summary} SolidWorks main CAD workflow status: {result.Status}.";
        var recommendation = result.QualityGatePassed
            ? $"{report.FinalRecommendation} SolidWorks artifacts passed the main workflow QualityGate."
            : $"{report.FinalRecommendation} Stop before delivery because the SolidWorks main workflow QualityGate did not pass.";

        return report with
        {
            Issues = report.Issues
                .Concat(result.Issues)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Artifacts = report.Artifacts
                .Concat(result.Artifacts)
                .ToArray(),
            Summary = summary,
            FinalRecommendation = recommendation
        };
    }

    private static string BuildOutputMessage(
        string rootAgentName,
        WorkflowExecutionResult workflowResult,
        SolidWorksMainWorkflowResult? solidWorksResult)
    {
        var message = $"{rootAgentName} completed workflow-backed internal multi-agent routing with status {workflowResult.Status}.";
        if (solidWorksResult is null)
        {
            return message;
        }

        return $"{message} SolidWorks main workflow status {solidWorksResult.Status}; real_cad_executed={solidWorksResult.RealCadExecuted}; quality_gate_passed={solidWorksResult.QualityGatePassed}.";
    }

}
