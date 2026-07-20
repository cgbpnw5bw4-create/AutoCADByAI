using System.Globalization;

namespace DomainSchemas;

public static class PartFamilyFailureStages
{
    public const string UnsupportedPartType = "unsupported_part_type";
    public const string MissingRequiredParameter = "missing_required_parameter";
    public const string InvalidParameterValue = "invalid_parameter_value";
    public const string PartFamilyDefinitionMissing = "part_family_definition_missing";
    public const string BuildPlanGenerationFailed = "build_plan_generation_failed";
    public const string PartFamilyBuilderMissing = "part_family_builder_missing";
    public const string FlangeBuildFailed = "flange_build_failed";
    public const string ShaftBuildFailed = "shaft_build_failed";
    public const string ArtifactValidationFailed = "artifact_validation_failed";
}

public enum PartParameterValueKind
{
    Number,
    Integer,
    NumberList,
    Text
}

public sealed record PartParameterSchema(
    string Name,
    PartParameterValueKind ValueKind,
    bool Required,
    string Description);

public sealed record PartFamilyValidationResult(
    bool IsValid,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public static PartFamilyValidationResult Passed() => new(true, null, Array.Empty<string>());

    public static PartFamilyValidationResult Failed(string stage, params string[] issues) =>
        new(false, stage, issues);
}

public sealed record PartFamilyBuildPlanResult(
    SolidWorksBuildPlan? BuildPlan,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess => BuildPlan is not null && string.IsNullOrWhiteSpace(FailureStage);
}

public interface IPartFamilyValidator
{
    PartFamilyValidationResult Validate(CADModelSpec spec);
}

public interface IPartFamilyDefinition
{
    string PartType { get; }

    IReadOnlyList<PartParameterSchema> ParameterSchema { get; }

    IPartFamilyValidator Validator { get; }

    string BuilderFailureStage { get; }

    string ApiEvidence { get; }

    PartFamilyBuildPlanResult GenerateBuildPlan(string taskId, CADModelSpec spec);

    IReadOnlyList<string> ReviewBuildPlan(SolidWorksBuildPlan plan);
}

public sealed class PartTypeRegistry
{
    private readonly Dictionary<string, IPartFamilyDefinition> _definitions =
        new(StringComparer.OrdinalIgnoreCase);

    public PartTypeRegistry(IEnumerable<IPartFamilyDefinition>? definitions = null)
    {
        foreach (var definition in definitions ?? Array.Empty<IPartFamilyDefinition>())
        {
            Register(definition);
        }
    }

    public static PartTypeRegistry CreateDefault() =>
        new([
            new PlateBasic4HolesDefinition(),
            new FlangeBasicDefinition(),
            new ShaftBasicDefinition()
        ]);

    public void Register(IPartFamilyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.PartType))
        {
            throw new ArgumentException("Part family definition must expose a non-empty part_type.", nameof(definition));
        }

        if (!_definitions.TryAdd(definition.PartType, definition))
        {
            throw new InvalidOperationException($"A part family definition is already registered for {definition.PartType}.");
        }
    }

    public bool TryGetDefinition(string? partType, out IPartFamilyDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(partType) && _definitions.TryGetValue(partType, out definition!))
        {
            return true;
        }

        definition = null!;
        return false;
    }

    public IPartFamilyDefinition? GetDefinition(string? partType) =>
        TryGetDefinition(partType, out var definition) ? definition : null;

    public IReadOnlyList<IPartFamilyDefinition> GetAll() =>
        _definitions.Values.OrderBy(definition => definition.PartType, StringComparer.OrdinalIgnoreCase).ToArray();
}

public sealed class CADModelSpecValidator
{
    private readonly PartTypeRegistry _registry;

    public CADModelSpecValidator(PartTypeRegistry? registry = null)
    {
        _registry = registry ?? PartTypeRegistry.CreateDefault();
    }

    public PartFamilyValidationResult Validate(CADModelSpec? spec)
    {
        if (spec is null || string.IsNullOrWhiteSpace(spec.PartType))
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.MissingRequiredParameter,
                "missing_required_parameter: part_type is required.");
        }

        if (!_registry.TryGetDefinition(spec.PartType, out var definition))
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedPartType,
                $"unsupported_part_type: {spec.PartType} is not registered.");
        }

        return definition.Validator.Validate(spec);
    }
}

public sealed class PlateBasic4HolesDefinition : IPartFamilyDefinition
{
    public const string Type = "plate_basic_4holes";

    public string PartType => Type;

    public IReadOnlyList<PartParameterSchema> ParameterSchema { get; } =
    [
        new("length_mm", PartParameterValueKind.Number, true, "Plate length in millimetres."),
        new("width_mm", PartParameterValueKind.Number, true, "Plate width in millimetres."),
        new("thickness_mm", PartParameterValueKind.Number, true, "Plate thickness in millimetres."),
        new("hole_diameter_mm", PartParameterValueKind.Number, true, "Through-hole diameter in millimetres."),
        new("hole_count", PartParameterValueKind.Integer, true, "Fixed four-hole count.")
    ];

    public IPartFamilyValidator Validator { get; } = new PlateBasic4HolesValidator();

    public string BuilderFailureStage => "plate_build_failed";

    public string ApiEvidence => "real_solidworks_plate_basic_4holes_smoke_passed";

    public PartFamilyBuildPlanResult GenerateBuildPlan(string taskId, CADModelSpec spec)
    {
        var validation = Validator.Validate(spec);
        if (!validation.IsValid)
        {
            return new(null, validation.FailureStage, validation.Issues);
        }

        var length = PartFamilyParameters.Get(spec, "length_mm");
        var width = PartFamilyParameters.Get(spec, "width_mm");
        var thickness = PartFamilyParameters.Get(spec, "thickness_mm");
        var holeDiameter = PartFamilyParameters.Get(spec, "hole_diameter_mm");
        var holeCount = PartFamilyParameters.Get(spec, "hole_count");
        var operations = new[]
        {
            Operation("op-001", "CreateSketch", "TopPlane", new Dictionary<string, string>
            {
                ["profile"] = "center_rectangle", ["length_mm"] = length, ["width_mm"] = width
            }, [], "Base plate sketch created."),
            Operation("op-002", "ExtrudeBoss", "TopPlane", new Dictionary<string, string>
            {
                ["depth_mm"] = thickness, ["direction"] = "mid_plane"
            }, ["op-001"], "Plate solid body created."),
            Operation("op-003", "CreateSketch", "TopFace", new Dictionary<string, string>
            {
                ["pattern"] = "rectangular", ["hole_count"] = holeCount, ["margin_x_mm"] = "20", ["margin_y_mm"] = "20"
            }, ["op-002"], "Hole sketch points created."),
            Operation("op-004", "CutExtrude", "TopFace", new Dictionary<string, string>
            {
                ["hole_diameter_mm"] = holeDiameter, ["through_all"] = "true", ["hole_count"] = holeCount
            }, ["op-003"], "Four through holes cut through the plate."),
            Operation("op-005", "SavePart", string.Empty, new Dictionary<string, string>
            {
                ["file_name"] = "fake_plate_basic_4holes.SLDPRT.txt"
            }, ["op-004"], "Dry-run part artifact path planned."),
            Operation("op-006", "ExportStep", string.Empty, new Dictionary<string, string>
            {
                ["file_name"] = "fake_plate_basic_4holes.STEP.txt"
            }, ["op-005"], "Dry-run STEP artifact path planned.")
        };

        return Success(taskId, spec, operations);
    }

    public IReadOnlyList<string> ReviewBuildPlan(SolidWorksBuildPlan plan)
    {
        var issues = new List<string>();
        var sketch = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase) &&
            operation.Parameters.ContainsKey("length_mm"));
        var extrude = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("ExtrudeBoss", StringComparison.OrdinalIgnoreCase));
        var cut = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CutExtrude", StringComparison.OrdinalIgnoreCase));
        var holeSketch = plan.Operations.FirstOrDefault(operation =>
            operation.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase) &&
            operation.Parameters.ContainsKey("margin_x_mm"));

        var lengthValid = PartFamilyParameters.TryPositive(sketch, "length_mm", out var length);
        var widthValid = PartFamilyParameters.TryPositive(sketch, "width_mm", out var width);
        var thicknessValid = PartFamilyParameters.TryPositive(extrude, "depth_mm", out _);
        var holeDiameterValid = PartFamilyParameters.TryPositive(cut, "hole_diameter_mm", out var holeDiameter);
        if (!lengthValid || !widthValid || !thicknessValid || !holeDiameterValid)
        {
            issues.Add("plate dimensions in BuildPlan must be positive and complete.");
        }

        if (cut is null || !cut.Parameters.TryGetValue("hole_count", out var countText) ||
            !int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) || count != 4)
        {
            issues.Add("plate_basic_4holes must contain four holes.");
        }

        if (holeDiameter > 0 && width > 0 && holeDiameter > width / 2)
        {
            issues.Add("hole diameter is too large for the simplified plate boundary rule.");
        }

        if (holeSketch is null ||
            !PartFamilyParameters.TryPositive(holeSketch, "margin_x_mm", out var marginX) ||
            !PartFamilyParameters.TryPositive(holeSketch, "margin_y_mm", out var marginY) ||
            marginX <= holeDiameter / 2 || marginY <= holeDiameter / 2 ||
            marginX >= length / 2 || marginY >= width / 2 ||
            length - 2 * marginX <= holeDiameter || width - 2 * marginY <= holeDiameter)
        {
            issues.Add("four-hole pattern may exceed the simplified plate boundary or contain overlapping holes.");
        }

        return issues;
    }

    private static PartFamilyBuildPlanResult Success(
        string taskId,
        CADModelSpec spec,
        IReadOnlyList<SolidWorksOperation> operations) =>
        PartFamilyPlanFactory.Success(taskId, spec, operations);

    private static SolidWorksOperation Operation(
        string id,
        string type,
        string plane,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> dependsOn,
        string expected) =>
        new(id, type, plane, parameters, dependsOn, expected);
}

public sealed class FlangeBasicDefinition : IPartFamilyDefinition
{
    public const string Type = "flange_basic";

    public string PartType => Type;

    public IReadOnlyList<PartParameterSchema> ParameterSchema { get; } =
    [
        new("outer_diameter_mm", PartParameterValueKind.Number, true, "Outer diameter."),
        new("inner_diameter_mm", PartParameterValueKind.Number, true, "Inner bore diameter."),
        new("thickness_mm", PartParameterValueKind.Number, true, "Flange thickness."),
        new("bolt_hole_count", PartParameterValueKind.Integer, true, "Bolt-hole count."),
        new("bolt_hole_diameter_mm", PartParameterValueKind.Number, true, "Bolt-hole diameter."),
        new("bolt_circle_diameter_mm", PartParameterValueKind.Number, true, "Pitch-circle diameter.")
    ];

    public IPartFamilyValidator Validator { get; } = new FlangeBasicValidator();

    public string BuilderFailureStage => PartFamilyFailureStages.FlangeBuildFailed;

    public string ApiEvidence => "dry_run_only_real_api_smoke_not_completed";

    public PartFamilyBuildPlanResult GenerateBuildPlan(string taskId, CADModelSpec spec)
    {
        var validation = Validator.Validate(spec);
        if (!validation.IsValid)
        {
            return new(null, validation.FailureStage, validation.Issues);
        }

        var operations = new[]
        {
            new SolidWorksOperation("op-001", "CreateSketch", "TopPlane", new Dictionary<string, string>
            {
                ["profile"] = "annulus",
                ["outer_diameter_mm"] = PartFamilyParameters.Get(spec, "outer_diameter_mm"),
                ["inner_diameter_mm"] = PartFamilyParameters.Get(spec, "inner_diameter_mm")
            }, [], "Concentric flange profile created."),
            new SolidWorksOperation("op-002", "ExtrudeBoss", "TopPlane", new Dictionary<string, string>
            {
                ["depth_mm"] = PartFamilyParameters.Get(spec, "thickness_mm"), ["direction"] = "mid_plane"
            }, ["op-001"], "Flange body created."),
            new SolidWorksOperation("op-003", "CreateSketch", "TopFace", new Dictionary<string, string>
            {
                ["pattern"] = "bolt_circle",
                ["bolt_hole_count"] = PartFamilyParameters.Get(spec, "bolt_hole_count"),
                ["bolt_circle_diameter_mm"] = PartFamilyParameters.Get(spec, "bolt_circle_diameter_mm")
            }, ["op-002"], "Bolt-circle sketch created."),
            new SolidWorksOperation("op-004", "CutExtrude", "TopFace", new Dictionary<string, string>
            {
                ["hole_diameter_mm"] = PartFamilyParameters.Get(spec, "bolt_hole_diameter_mm"),
                ["hole_count"] = PartFamilyParameters.Get(spec, "bolt_hole_count"),
                ["through_all"] = "true"
            }, ["op-003"], "Bolt holes cut through the flange."),
            new SolidWorksOperation("op-005", "SavePart", string.Empty, new Dictionary<string, string>
            {
                ["file_name"] = "fake_flange_basic.SLDPRT.txt"
            }, ["op-004"], "Dry-run flange part path planned."),
            new SolidWorksOperation("op-006", "ExportStep", string.Empty, new Dictionary<string, string>
            {
                ["file_name"] = "fake_flange_basic.STEP.txt"
            }, ["op-005"], "Dry-run flange STEP path planned.")
        };
        return PartFamilyPlanFactory.Success(taskId, spec, operations);
    }

    public IReadOnlyList<string> ReviewBuildPlan(SolidWorksBuildPlan plan) =>
        PartFamilyPlanFactory.ReviewCommon(plan, Type);
}

public sealed class ShaftBasicDefinition : IPartFamilyDefinition
{
    public const string Type = "shaft_basic";

    public string PartType => Type;

    public IReadOnlyList<PartParameterSchema> ParameterSchema { get; } =
    [
        new("diameter_mm", PartParameterValueKind.Number, true, "Base shaft diameter."),
        new("length_mm", PartParameterValueKind.Number, true, "Overall shaft length."),
        new("optional_step_diameters", PartParameterValueKind.NumberList, false, "Comma-separated optional step diameters."),
        new("optional_step_lengths", PartParameterValueKind.NumberList, false, "Comma-separated optional step lengths.")
    ];

    public IPartFamilyValidator Validator { get; } = new ShaftBasicValidator();

    public string BuilderFailureStage => PartFamilyFailureStages.ShaftBuildFailed;

    public string ApiEvidence => "dry_run_only_optional_step_api_evidence_insufficient";

    public PartFamilyBuildPlanResult GenerateBuildPlan(string taskId, CADModelSpec spec)
    {
        var validation = Validator.Validate(spec);
        if (!validation.IsValid)
        {
            return new(null, validation.FailureStage, validation.Issues);
        }

        var diameters = PartFamilyParameters.ParseNumberList(spec, "optional_step_diameters");
        var lengths = PartFamilyParameters.ParseNumberList(spec, "optional_step_lengths");
        var overallLength = double.Parse(
            PartFamilyParameters.Get(spec, "length_mm"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        var baseLength = overallLength - lengths.Sum();
        var operations = new List<SolidWorksOperation>
        {
            new("op-001", "CreateSketch", "RightPlane", new Dictionary<string, string>
            {
                ["profile"] = "circle", ["diameter_mm"] = PartFamilyParameters.Get(spec, "diameter_mm")
            }, [], "Base shaft profile created."),
            new("op-002", "ExtrudeBoss", "RightPlane", new Dictionary<string, string>
            {
                ["depth_mm"] = PartFamilyParameters.Format(baseLength), ["direction"] = "blind"
            }, ["op-001"], "Base shaft body created.")
        };
        var previous = "op-002";
        for (var index = 0; index < diameters.Count; index++)
        {
            var sketchId = $"op-{operations.Count + 1:000}";
            operations.Add(new SolidWorksOperation(sketchId, "CreateSketch", "EndFace", new Dictionary<string, string>
            {
                ["profile"] = "circle", ["diameter_mm"] = PartFamilyParameters.Format(diameters[index]), ["step_index"] = (index + 1).ToString(CultureInfo.InvariantCulture)
            }, [previous], $"Optional shaft step {index + 1} profile created."));
            var extrudeId = $"op-{operations.Count + 1:000}";
            operations.Add(new SolidWorksOperation(extrudeId, "ExtrudeBoss", "EndFace", new Dictionary<string, string>
            {
                ["depth_mm"] = PartFamilyParameters.Format(lengths[index]), ["direction"] = "blind", ["step_index"] = (index + 1).ToString(CultureInfo.InvariantCulture)
            }, [sketchId], $"Optional shaft step {index + 1} created."));
            previous = extrudeId;
        }

        operations.Add(new SolidWorksOperation($"op-{operations.Count + 1:000}", "SavePart", string.Empty, new Dictionary<string, string>
        {
            ["file_name"] = "fake_shaft_basic.SLDPRT.txt"
        }, [previous], "Dry-run shaft part path planned."));
        operations.Add(new SolidWorksOperation($"op-{operations.Count + 1:000}", "ExportStep", string.Empty, new Dictionary<string, string>
        {
            ["file_name"] = "fake_shaft_basic.STEP.txt"
        }, [operations[^1].OperationId], "Dry-run shaft STEP path planned."));

        return PartFamilyPlanFactory.Success(taskId, spec, operations);
    }

    public IReadOnlyList<string> ReviewBuildPlan(SolidWorksBuildPlan plan) =>
        PartFamilyPlanFactory.ReviewCommon(plan, Type);
}

public sealed class PlateBasic4HolesValidator : IPartFamilyValidator
{
    public PartFamilyValidationResult Validate(CADModelSpec spec)
    {
        var required = PartFamilyParameters.Require(spec,
            "length_mm", "width_mm", "thickness_mm", "hole_diameter_mm", "hole_count");
        if (required is not null)
        {
            return required;
        }

        if (!PartFamilyParameters.TryPositive(spec, "length_mm", out var length) ||
            !PartFamilyParameters.TryPositive(spec, "width_mm", out var width) ||
            !PartFamilyParameters.TryPositive(spec, "thickness_mm", out _) ||
            !PartFamilyParameters.TryPositive(spec, "hole_diameter_mm", out var holeDiameter) ||
            !PartFamilyParameters.TryInteger(spec, "hole_count", out var holeCount) ||
            holeCount != 4 || holeDiameter / 2 >= 20 || length / 2 <= 20 || width / 2 <= 20 ||
            length - 40 <= holeDiameter || width - 40 <= holeDiameter)
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.InvalidParameterValue,
                "invalid_parameter_value: plate dimensions must be positive, hole_count must be 4, and the 20 mm margin hole pattern must fit without overlapping holes.");
        }

        return PartFamilyValidationResult.Passed();
    }
}

public sealed class FlangeBasicValidator : IPartFamilyValidator
{
    public PartFamilyValidationResult Validate(CADModelSpec spec)
    {
        var required = PartFamilyParameters.Require(spec,
            "outer_diameter_mm", "inner_diameter_mm", "thickness_mm", "bolt_hole_count",
            "bolt_hole_diameter_mm", "bolt_circle_diameter_mm");
        if (required is not null)
        {
            return required;
        }

        if (!PartFamilyParameters.TryPositive(spec, "outer_diameter_mm", out var outer) ||
            !PartFamilyParameters.TryPositive(spec, "inner_diameter_mm", out var inner) ||
            !PartFamilyParameters.TryPositive(spec, "thickness_mm", out _) ||
            !PartFamilyParameters.TryInteger(spec, "bolt_hole_count", out var count) || count < 1 ||
            !PartFamilyParameters.TryPositive(spec, "bolt_hole_diameter_mm", out var hole) ||
            !PartFamilyParameters.TryPositive(spec, "bolt_circle_diameter_mm", out var circle) ||
            inner >= outer || circle <= inner + hole || circle + hole >= outer ||
            count > 1 && circle * Math.Sin(Math.PI / count) <= hole)
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.InvalidParameterValue,
                "invalid_parameter_value: flange diameters, bolt count, bolt-circle clearance or adjacent bolt-hole spacing are invalid.");
        }

        return PartFamilyValidationResult.Passed();
    }
}

public sealed class ShaftBasicValidator : IPartFamilyValidator
{
    public PartFamilyValidationResult Validate(CADModelSpec spec)
    {
        var required = PartFamilyParameters.Require(spec, "diameter_mm", "length_mm");
        if (required is not null)
        {
            return required;
        }

        if (!PartFamilyParameters.TryPositive(spec, "diameter_mm", out _) ||
            !PartFamilyParameters.TryPositive(spec, "length_mm", out var length))
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.InvalidParameterValue,
                "invalid_parameter_value: shaft diameter and length must be positive.");
        }

        var hasDiameters = spec.TryGetParameter("optional_step_diameters", out var diameterText) && !string.IsNullOrWhiteSpace(diameterText);
        var hasLengths = spec.TryGetParameter("optional_step_lengths", out var lengthText) && !string.IsNullOrWhiteSpace(lengthText);
        if (hasDiameters != hasLengths)
        {
            return PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.MissingRequiredParameter,
                "missing_required_parameter: optional_step_diameters and optional_step_lengths must be supplied together.");
        }

        if (hasDiameters)
        {
            var diameters = PartFamilyParameters.ParseNumberList(diameterText!);
            var lengths = PartFamilyParameters.ParseNumberList(lengthText!);
            if (diameters.Count == 0 || diameters.Count != lengths.Count ||
                diameters.Any(value => !double.IsFinite(value) || value <= 0) ||
                lengths.Any(value => !double.IsFinite(value) || value <= 0) ||
                lengths.Sum() >= length)
            {
                return PartFamilyValidationResult.Failed(
                    PartFamilyFailureStages.InvalidParameterValue,
                    "invalid_parameter_value: optional shaft step lists must contain matching finite positive values and total step length must be less than shaft length.");
            }
        }

        return PartFamilyValidationResult.Passed();
    }
}

internal static class PartFamilyPlanFactory
{
    public static PartFamilyBuildPlanResult Success(
        string taskId,
        CADModelSpec spec,
        IReadOnlyList<SolidWorksOperation> operations)
    {
        var safeType = spec.PartType.ToLowerInvariant();
        return new PartFamilyBuildPlanResult(
            new SolidWorksBuildPlan(
                $"solidworks-build-plan-{taskId}",
                spec.Id,
                "SolidWorks",
                spec.PartType,
                "mm",
                operations,
                [
                    new SolidWorksArtifact("expected-part", "Part", $"output/solidworks/artifacts/fake_{safeType}.SLDPRT.txt", ".SLDPRT", false, 0, $"Dry-run {safeType} part placeholder."),
                    new SolidWorksArtifact("expected-step", "Step", $"output/solidworks/artifacts/fake_{safeType}.STEP.txt", ".STEP", false, 0, $"Dry-run {safeType} STEP placeholder."),
                    new SolidWorksArtifact("expected-build-report", "BuildReport", "output/solidworks/reports/build_report.json", ".json", false, 0, "Dry-run family-aware build report.")
                ],
                ["target_cad_system must be SolidWorks", "part_type must be registered", "parameters must pass the part-family validator"],
                ["Dry-run plan generation does not call SolidWorks or COM."],
                Material: spec.Material,
                Features: new Dictionary<string, string>(spec.Features, StringComparer.OrdinalIgnoreCase),
                OutputRequirements: spec.OutputRequirements.ToArray(),
                DrawingRequirements: new Dictionary<string, string>(spec.DrawingRequirements, StringComparer.OrdinalIgnoreCase),
                ExecutionOptions: new Dictionary<string, string>(spec.ExecutionOptions, StringComparer.OrdinalIgnoreCase),
                Dimensions: new Dictionary<string, string>(spec.Dimensions, StringComparer.OrdinalIgnoreCase)),
            null,
            Array.Empty<string>());
    }

    public static IReadOnlyList<string> ReviewCommon(SolidWorksBuildPlan plan, string expectedPartType)
    {
        var issues = new List<string>();
        if (!string.Equals(plan.PartType, expectedPartType, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add($"part_type must be {expectedPartType}.");
        }

        if (!plan.ExpectedArtifacts.Any(artifact => artifact.ExpectedExtension.Equals(".SLDPRT", StringComparison.OrdinalIgnoreCase)) ||
            !plan.ExpectedArtifacts.Any(artifact => artifact.ExpectedExtension.Equals(".STEP", StringComparison.OrdinalIgnoreCase)) ||
            !plan.ExpectedArtifacts.Any(artifact => artifact.FilePath.EndsWith("build_report.json", StringComparison.OrdinalIgnoreCase)))
        {
            issues.Add("expected output formats must include .SLDPRT, .STEP and build_report.json.");
        }

        return issues;
    }
}

internal static class PartFamilyParameters
{
    public static string Get(CADModelSpec spec, string name) =>
        spec.TryGetParameter(name, out var value) ? value : string.Empty;

    public static PartFamilyValidationResult? Require(CADModelSpec spec, params string[] names)
    {
        var missing = names.Where(name => !spec.TryGetParameter(name, out var value) || string.IsNullOrWhiteSpace(value)).ToArray();
        return missing.Length == 0
            ? null
            : PartFamilyValidationResult.Failed(
                PartFamilyFailureStages.MissingRequiredParameter,
                $"missing_required_parameter: {string.Join(", ", missing)}.");
    }

    public static bool TryPositive(CADModelSpec spec, string name, out double value)
    {
        value = 0;
        return spec.TryGetParameter(name, out var text) && TryPositive(text, out value);
    }

    public static bool TryPositive(SolidWorksOperation? operation, string name, out double value)
    {
        value = 0;
        return operation is not null && operation.Parameters.TryGetValue(name, out var text) && TryPositive(text, out value);
    }

    public static bool TryInteger(CADModelSpec spec, string name, out int value)
    {
        value = 0;
        return spec.TryGetParameter(name, out var text) &&
               int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    public static IReadOnlyList<double> ParseNumberList(CADModelSpec spec, string name) =>
        spec.TryGetParameter(name, out var text) ? ParseNumberList(text) : Array.Empty<double>();

    public static IReadOnlyList<double> ParseNumberList(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<double>();
        }

        var values = new List<double>();
        foreach (var item in text.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(item, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value))
            {
                return Array.Empty<double>();
            }

            values.Add(value);
        }

        return values;
    }

    public static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static bool TryPositive(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        double.IsFinite(value) && value > 0;
}
