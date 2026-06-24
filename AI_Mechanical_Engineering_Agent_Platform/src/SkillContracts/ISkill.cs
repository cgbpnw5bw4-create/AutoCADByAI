namespace SkillContracts;

public interface ISkill
{
    string Name { get; }

    string Description { get; }

    Task<SkillOutput> ExecuteAsync(SkillInput input);
}
