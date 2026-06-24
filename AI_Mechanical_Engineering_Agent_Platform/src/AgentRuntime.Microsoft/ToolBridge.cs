using SkillContracts;
using PlatformCore;
using WorkerContracts;

namespace AgentRuntime.Microsoft;

public sealed record RuntimeToolCall(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record RuntimeToolMapping(
    string Kind,
    string TargetName,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record RuntimeAgentMessage(
    string Role,
    string Content);

public sealed class ToolBridge
{
    private readonly SkillRegistry _skillRegistry;
    private readonly WorkerRegistry _workerRegistry;

    public ToolBridge(SkillRegistry skillRegistry, WorkerRegistry workerRegistry)
    {
        _skillRegistry = skillRegistry;
        _workerRegistry = workerRegistry;
    }

    public RuntimeToolMapping MapToolCallToSkill(RuntimeToolCall toolCall)
    {
        var target = toolCall.Name.StartsWith("skill:", StringComparison.OrdinalIgnoreCase)
            ? toolCall.Name["skill:".Length..]
            : toolCall.Name;

        return new RuntimeToolMapping("skill", target, toolCall.Arguments);
    }

    public RuntimeToolMapping MapToolCallToWorker(RuntimeToolCall toolCall)
    {
        var target = toolCall.Name.StartsWith("worker:", StringComparison.OrdinalIgnoreCase)
            ? toolCall.Name["worker:".Length..]
            : toolCall.Name;

        return new RuntimeToolMapping("worker", target, toolCall.Arguments);
    }

    public RuntimeAgentMessage ConvertSkillOutputToAgentMessage(SkillOutput output) =>
        new("tool", $"Skill completed with status {output.Status}: {output.Result}");

    public RuntimeAgentMessage ConvertWorkerOutputToAgentMessage(WorkerOutput output) =>
        new("tool", $"Worker completed with status {output.Status}: {output.ExecutionLog}");

    public Task<SkillOutput> InvokeSkillAsync(string skillName, SkillInput input)
    {
        var skill = _skillRegistry.GetByName(skillName)
            ?? throw new InvalidOperationException($"Skill '{skillName}' is not registered.");

        return skill.ExecuteAsync(input);
    }

    public Task<WorkerOutput> InvokeWorkerAsync(string workerName, WorkerInput input)
    {
        var worker = _workerRegistry.GetByName(workerName)
            ?? throw new InvalidOperationException($"Worker '{workerName}' is not registered.");

        return worker.ExecuteAsync(input);
    }
}
