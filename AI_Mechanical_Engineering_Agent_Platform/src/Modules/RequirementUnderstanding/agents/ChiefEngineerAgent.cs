using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.RequirementUnderstanding.Agents;

public sealed class ChiefEngineerAgent : IAgent, IHumanApprovalAgent
{
    private readonly ChiefEngineerOrchestrator _orchestrator;

    public ChiefEngineerAgent(ChiefEngineerOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    public string Id => "chief-engineer";

    public string Name => "机械总工程师";

    public AgentRole Role { get; } = new(
        "chief-engineer",
        "机械总工程师",
        "总调度、任务拆解、内部 Agent 协作、质量裁决");

    public string Description => "Public entry point for external gateways. Delegates to internal engineering agents.";

    public AgentVisibility Visibility => AgentVisibility.Public;

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        _orchestrator.ExecuteAsync(context, Id, Name);

    public Task<AgentApprovalResult> ResumeHumanApprovalAsync(AgentContext context, WorkflowApprovalSubmission submission,
        CancellationToken cancellationToken = default) =>
        _orchestrator.ResumeHumanApprovalAsync(context.TaskId, Id, Name, submission, cancellationToken);
}
