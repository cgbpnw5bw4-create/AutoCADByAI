using DomainSchemas;
using SkillContracts;

namespace PlatformCore.Modules.CADModeling.Skills;

public sealed class SolidWorksBuildPlanSkill : ISkill
{
    private readonly PartTypeRegistry _partTypeRegistry;
    private readonly CADModelSpecValidator _specValidator;

    public SolidWorksBuildPlanSkill(PartTypeRegistry? partTypeRegistry = null)
    {
        _partTypeRegistry = partTypeRegistry ?? PartTypeRegistry.CreateDefault();
        _specValidator = new CADModelSpecValidator(_partTypeRegistry);
    }

    public string Name => "solidworks-build-plan-skill";

    public string Description => "Generate a registry-dispatched SolidWorksBuildPlan without calling SolidWorks, COM or Worker.";

    public Task<SkillOutput> ExecuteAsync(SkillInput input)
    {
        if (input.Payload is not CADModelSpec spec)
        {
            return Task.FromResult(Failed(
                SkillOutputStatus.Rejected,
                PartFamilyFailureStages.MissingRequiredParameter,
                "missing_required_parameter: payload must be CADModelSpec."));
        }

        var validation = _specValidator.Validate(spec);
        if (!validation.IsValid)
        {
            return Task.FromResult(new SkillOutput(
                SkillOutputStatus.Rejected,
                null,
                validation.Issues,
                ["CADModelSpec was rejected before Worker dispatch."]));
        }

        if (!_partTypeRegistry.TryGetDefinition(spec.PartType, out var definition))
        {
            return Task.FromResult(Failed(
                SkillOutputStatus.Failed,
                PartFamilyFailureStages.PartFamilyDefinitionMissing,
                $"part_family_definition_missing: no definition is registered for {spec.PartType}."));
        }

        PartFamilyBuildPlanResult generated;
        try
        {
            generated = definition.GenerateBuildPlan(input.TaskId, spec);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Failed(
                SkillOutputStatus.Failed,
                PartFamilyFailureStages.BuildPlanGenerationFailed,
                $"build_plan_generation_failed: {ex.Message}"));
        }

        if (!generated.IsSuccess)
        {
            return Task.FromResult(new SkillOutput(
                SkillOutputStatus.Rejected,
                null,
                generated.Issues.Count > 0
                    ? generated.Issues
                    : [$"{generated.FailureStage ?? PartFamilyFailureStages.BuildPlanGenerationFailed}: BuildPlan generation failed."],
                ["No Worker call was made."]));
        }

        return Task.FromResult(new SkillOutput(
            SkillOutputStatus.Completed,
            generated.BuildPlan,
            Array.Empty<string>(),
            [
                $"Generated {spec.PartType} SolidWorksBuildPlan through PartTypeRegistry.",
                $"part_family_api_evidence: {definition.ApiEvidence}",
                "No SolidWorks, COM, SldWorks.Application or Worker call was executed."
            ]));
    }

    private static SkillOutput Failed(SkillOutputStatus status, string stage, string issue) =>
        new(status, null, [$"{stage}: {StripDuplicateStage(stage, issue)}"], ["No Worker call was made."]);

    private static string StripDuplicateStage(string stage, string issue) =>
        issue.StartsWith($"{stage}:", StringComparison.OrdinalIgnoreCase)
            ? issue[(stage.Length + 1)..].TrimStart()
            : issue;
}
