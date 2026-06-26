using DomainSchemas;
using SkillContracts;

namespace PlatformCore.Modules.CADModeling.Skills;

public sealed class SolidWorksBuildPlanSkill : ISkill
{
    public string Name => "solidworks-build-plan-skill";

    public string Description => "Generate a dry-run SolidWorksBuildPlan without calling SolidWorks, COM or Worker.";

    public Task<SkillOutput> ExecuteAsync(SkillInput input)
    {
        var spec = input.Payload as CADModelSpec;
        var plan = CreatePlateBasicFourHolesPlan(input.TaskId, spec);

        return Task.FromResult(new SkillOutput(
            SkillOutputStatus.Completed,
            plan,
            Array.Empty<string>(),
            new[]
            {
                "Generated SolidWorksBuildPlan only.",
                "No SolidWorks, COM, SldWorks.Application or Worker call was executed."
            }));
    }

    private static SolidWorksBuildPlan CreatePlateBasicFourHolesPlan(string taskId, CADModelSpec? spec)
    {
        var length = Get(spec, "length_mm", "160");
        var width = Get(spec, "width_mm", "80");
        var thickness = Get(spec, "thickness_mm", "12");
        var holeDiameter = Get(spec, "hole_diameter_mm", "10");
        var holeCount = Get(spec, "hole_count", "4");
        var sourceId = string.IsNullOrWhiteSpace(spec?.Id)
            ? "cad-model-spec-plate-basic-4holes"
            : spec.Id;

        return new SolidWorksBuildPlan(
            $"solidworks-build-plan-{taskId}",
            sourceId,
            "SolidWorks",
            "plate_basic_4holes",
            "mm",
            new[]
            {
                new SolidWorksOperation(
                    "op-001",
                    "CreateSketch",
                    "TopPlane",
                    new Dictionary<string, string>
                    {
                        ["profile"] = "center_rectangle",
                        ["length_mm"] = length,
                        ["width_mm"] = width
                    },
                    Array.Empty<string>(),
                    "Base plate sketch created."),
                new SolidWorksOperation(
                    "op-002",
                    "ExtrudeBoss",
                    "TopPlane",
                    new Dictionary<string, string>
                    {
                        ["depth_mm"] = thickness,
                        ["direction"] = "mid_plane"
                    },
                    new[] { "op-001" },
                    "Plate solid body created."),
                new SolidWorksOperation(
                    "op-003",
                    "CreateSketch",
                    "TopFace",
                    new Dictionary<string, string>
                    {
                        ["pattern"] = "rectangular",
                        ["hole_count"] = holeCount,
                        ["margin_x_mm"] = "20",
                        ["margin_y_mm"] = "20"
                    },
                    new[] { "op-002" },
                    "Hole sketch points created."),
                new SolidWorksOperation(
                    "op-004",
                    "CutExtrude",
                    "TopFace",
                    new Dictionary<string, string>
                    {
                        ["hole_diameter_mm"] = holeDiameter,
                        ["through_all"] = "true",
                        ["hole_count"] = holeCount
                    },
                    new[] { "op-003" },
                    "Four through holes cut through the plate."),
                new SolidWorksOperation(
                    "op-005",
                    "SavePart",
                    string.Empty,
                    new Dictionary<string, string>
                    {
                        ["file_name"] = "fake_plate_basic_4holes.SLDPRT.txt"
                    },
                    new[] { "op-004" },
                    "Dry-run part artifact path planned."),
                new SolidWorksOperation(
                    "op-006",
                    "ExportStep",
                    string.Empty,
                    new Dictionary<string, string>
                    {
                        ["file_name"] = "fake_plate_basic_4holes.STEP.txt"
                    },
                    new[] { "op-005" },
                    "Dry-run STEP artifact path planned.")
            },
            new[]
            {
                new SolidWorksArtifact(
                    "expected-part",
                    "Part",
                    "output/solidworks/artifacts/fake_plate_basic_4holes.SLDPRT.txt",
                    ".SLDPRT",
                    false,
                    0,
                    "Dry-run placeholder for a SolidWorks part file."),
                new SolidWorksArtifact(
                    "expected-step",
                    "Step",
                    "output/solidworks/artifacts/fake_plate_basic_4holes.STEP.txt",
                    ".STEP",
                    false,
                    0,
                    "Dry-run placeholder for a STEP export."),
                new SolidWorksArtifact(
                    "expected-build-report",
                    "BuildReport",
                    "output/solidworks/reports/build_report.json",
                    ".json",
                    false,
                    0,
                    "Dry-run build report.")
            },
            new[]
            {
                "target_cad_system must be SolidWorks",
                "dry_run must remain true",
                "allow_real_cad_execution must remain false"
            },
            new[]
            {
                "This is a dry-run build plan only.",
                "Real CAD execution is explicitly disabled."
            });
    }

    private static string Get(CADModelSpec? spec, string key, string fallback) =>
        spec is not null &&
        spec.Parameters.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : fallback;
}
