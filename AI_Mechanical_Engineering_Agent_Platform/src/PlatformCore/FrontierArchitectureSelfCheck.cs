using AgentContracts;
using DomainSchemas;
using ModelRuntime;
using QualityGate;

namespace PlatformCore;

// 无网络、无 COM 的边界行为检查；不替代测试运行或真实工程能力验收。
public static class FrontierArchitectureSelfCheck
{
    public static async Task<IReadOnlyDictionary<string, bool>> RunAsync(string projectRoot)
    {
        var checks = new Dictionary<string, bool>();
        var request = new ModelRequest(EngineeringModelPurpose.DesignPlanning, "工程规划", "分析需求");
        checks["model_provider_contract_executed"] =
            await new ModelRuntime.ModelRuntime(new ProbeProvider("甲")).GenerateAsync(request) == "甲" &&
            await new ModelRuntime.ModelRuntime(new ProbeProvider("乙")).GenerateAsync(request) == "乙";

        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        var input = new AgentInput("self-check", "local", "frontier", "self-check", "分析工程需求", [],
            new Dictionary<string, string>());
        var context = new AgentContext("frontier-self-check", input, new Dictionary<string, object?>(), DateTimeOffset.UtcNow);
        var chief = platform.AgentRegistry.GetById("chief-engineer")!;
        var output = await chief.ExecuteAsync(context);
        var empty = await chief.ExecuteAsync(context with { TaskId = "frontier-empty", Input = input with { Message = " " } });
        checks["default_planning_route_consolidated"] = output.Status == AgentOutputStatus.Completed &&
            output.InternalCollaborationReport?.CalledAgents.Count == 0 &&
            output.InternalCollaborationReport.StepResults is { Count: 1 } steps &&
            steps[0].StepId == EngineeringPlanValidationStep.Id && steps[0].GateDecision?.Result == GateDecisionResult.Passed &&
            !platform.AuditLog.GetEntries().Any(entry => entry.Action == "internal_agent_invoked") &&
            empty.Status == AgentOutputStatus.Failed;

        var gate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder(), [new ProbeGate()]);
        var evaluation = gate.Evaluate(new ReviewReport("frontier", "self-check", true, 1, [], RequiresHumanApproval: true, HasFatalError: false));
        checks["quality_gate_extension_blocks_approval_bypass"] = evaluation.Decision.Result == GateDecisionResult.Rejected &&
            evaluation.Checks.Count == 2 && evaluation.RejectReport is not null;

        IWorkflowEngine engine = new SequentialWorkflowEngine(new DefaultRetryPolicy(0));
        var downstreamExecuted = false;
        var workflow = await engine.ExecuteAsync([
            new WorkflowStep("领域验收", _ => Task.FromResult(new WorkflowStepResult("gate", "领域验收",
                WorkflowStepStatus.Rejected, "缺证据", GateDecision: evaluation.Decision)), "gate"),
            new WorkflowStep("下游", _ => { downstreamExecuted = true; return Task.FromResult(new WorkflowStepResult(
                "downstream", "下游", WorkflowStepStatus.Passed, "执行")); }, "downstream")
        ], new WorkflowContext("frontier-gate", new Dictionary<string, object?>()));
        checks["workflow_contract_preserves_gate_blocking"] = workflow.Status == WorkflowStatus.Rejected &&
            !downstreamExecuted && !engine.TryGetPendingHumanApproval("frontier-gate", out _);
        return checks;
    }

    private sealed class ProbeProvider(string response) : IModelProvider
    {
        public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default) => Task.FromResult(response);
    }

    private sealed class ProbeGate : IQualityGate
    {
        public string Name => "self-check-evidence-rejection";
        public QualityGateDomain Domain => QualityGateDomain.APIEvidence;
        public GateDecision Evaluate(ReviewReport report) => new("frontier-evidence", GateDecisionResult.Rejected, "独立检查未通过");
    }
}
