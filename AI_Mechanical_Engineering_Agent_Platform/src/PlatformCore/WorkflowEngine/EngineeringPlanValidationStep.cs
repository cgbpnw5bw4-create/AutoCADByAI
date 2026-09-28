using AgentContracts;
using DomainSchemas;
using QualityGate;

namespace PlatformCore;

// 仅校验规划输入；工程计划生成、特征校验与 CAD 执行仍由后续既有链路负责。
public sealed class EngineeringPlanValidationStep(
    AgentContext inputContext,
    SolidWorksWorkflowRouter cadRouter,
    InMemoryAuditLog auditLog)
{
    public const string Id = "engineering-plan-validation";

    public WorkflowStep ToWorkflowStep() =>
        new("工程规划输入校验", ExecuteAsync, Id, MaxRetries: 0);

    private Task<WorkflowStepResult> ExecuteAsync(WorkflowContext workflowContext)
    {
        var issues = SolidWorksWorkflowRouter.ValidateStructuredInput(inputContext).ToList();
        if (string.IsNullOrWhiteSpace(inputContext.Input.Message) &&
            !cadRouter.ShouldRun(inputContext with { Input = inputContext.Input with { Message = string.Empty } }))
        {
            issues.Add("engineering_requirement_missing: 必须提供非空工程需求或明确的 CAD 请求。");
        }

        var review = new ReviewReport(
            $"engineering-input-{workflowContext.TaskId}", Id,
            issues.Count == 0, issues.Count == 0 ? 1 : 0, issues,
            RequiresHumanApproval: false, HasFatalError: issues.Count > 0);
        var gate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(review);
        auditLog.Record("quality-gate", Id, "quality_gate_after_plan_validation",
            $"工程规划输入校验结果：{gate.Decision.Result}；此步骤不生成工程计划或执行 CAD。");

        return Task.FromResult(new WorkflowStepResult(
            Id, "工程规划输入校验",
            gate.Decision.Result == GateDecisionResult.Passed ? WorkflowStepStatus.Passed : WorkflowStepStatus.Failed,
            review.IsPassed
                ? "工程规划输入已校验；本步骤未生成工程计划，后续 CAD 计划仍需通过既有 Skill 与 Validator。"
                : "工程规划输入无效，已停止后续执行。",
            ReviewReport: review, GateDecision: gate.Decision, RejectReport: gate.RejectReport,
            Logs: ["确定性规则校验已进入 QualityGate；未调用模型、内部 Agent、Worker 或 CAD API。"],
            Issues: issues));
    }
}
