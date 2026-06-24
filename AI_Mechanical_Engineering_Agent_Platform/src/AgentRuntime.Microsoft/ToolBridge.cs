using PlatformCore;
using SkillContracts;
using WorkerContracts;

namespace AgentRuntime.Microsoft;

public sealed class ToolBridge
{
    private readonly SkillRegistry _skillRegistry;
    private readonly WorkerRegistry _workerRegistry;

    public ToolBridge(SkillRegistry skillRegistry, WorkerRegistry workerRegistry)
    {
        _skillRegistry = skillRegistry;
        _workerRegistry = workerRegistry;
    }

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
