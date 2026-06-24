using SkillContracts;

namespace PlatformCore;

public sealed class PlaceholderSkill : ISkill
{
    public PlaceholderSkill(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }

    public string Description { get; }

    public Task<SkillOutput> ExecuteAsync(SkillInput input)
    {
        var output = new SkillOutput(
            SkillOutputStatus.Completed,
            new { input.TaskId, input.PayloadType, handled_by = Name },
            Array.Empty<string>(),
            new[] { $"{Name} executed in placeholder mode." });

        return Task.FromResult(output);
    }
}
