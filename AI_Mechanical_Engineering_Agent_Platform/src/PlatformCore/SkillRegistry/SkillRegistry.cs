using SkillContracts;

namespace PlatformCore;

public sealed class SkillRegistry
{
    private readonly Dictionary<string, ISkill> _skills = new(StringComparer.OrdinalIgnoreCase);

    public void Register(ISkill skill)
    {
        _skills[skill.Name] = skill;
    }

    public ISkill? GetByName(string name) =>
        _skills.TryGetValue(name, out var skill) ? skill : null;

    public IReadOnlyList<ISkill> GetAll() =>
        _skills.Values.OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
