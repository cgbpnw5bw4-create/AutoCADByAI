using DomainSchemas;

namespace PlatformCore;

public sealed class SequentialWorkflowEngine
{
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        IEnumerable<WorkflowStep> steps,
        WorkflowContext context)
    {
        var results = new List<WorkflowStepResult>();
        var finalStatus = "Passed";

        foreach (var step in steps)
        {
            var result = await step.ExecuteAsync(context);
            results.Add(result);

            if (result.GateDecision is null || result.GateDecision.Result == GateDecisionResult.Passed)
            {
                continue;
            }

            finalStatus = result.GateDecision.Result.ToString();
            break;
        }

        return new WorkflowExecutionResult(context.TaskId, finalStatus, results);
    }
}
