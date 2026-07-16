using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public sealed class SolidWorksWorkflowRouter
{
    private const string PlateBasicFourHoles = "plate_basic_4holes";
    private const string BuildCompleteDrawingPackage = "build_complete_drawing_package";

    public SolidWorksMainWorkflowRequest? TryBuildRequest(AgentContext context)
    {
        var sharedSpec = TryGetSharedStateModelSpec(context);
        if (sharedSpec is not null && !IsSupportedModelType(sharedSpec.ModelType))
        {
            return null;
        }

        if (!ShouldRun(context))
        {
            return null;
        }

        var projectRoot = ResolveProjectRoot(context);
        var outputDirectory = ResolveSolidWorksOutputDirectory(context, projectRoot);
        var requestAllowsReal = FlagEnabled(context, "allow_real_cad_execution") ||
            FlagEnabled(context, "solidworks_allow_real_cad_execution");
        var dryRun = !FlagDisabled(context, "dry_run");
        var isCompleteDrawingPackage = ContextValueEquals(context, BuildCompleteDrawingPackage, "operation");
        var modelSpec = ResolveModelSpec(context) ??
            CreatePlateBasicFourHolesSpec(
                id: "cad-model-spec-plate-basic-4holes-main-workflow",
                description: "Main workflow controlled SolidWorks plate_basic_4holes build.",
                values: context.Input.Context,
                constraints: ["main_workflow_controlled_real_cad_requires_request_and_environment_flags"]);

        return new SolidWorksMainWorkflowRequest(
            $"solidworks-main-workflow-{context.TaskId}",
            context.TaskId,
            projectRoot,
            outputDirectory,
            dryRun,
            requestAllowsReal,
            modelSpec,
            Operation: isCompleteDrawingPackage
                ? SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage
                : SolidWorksMainWorkflowOperation.BuildPlate,
            GenerateDrawing: FlagEnabled(context, "generate_drawing"),
            GenerateDimensions: FlagEnabled(context, "generate_dimensions"),
            GenerateTitleBlock: FlagEnabled(context, "generate_title_block"),
            GenerateReleasePackage: FlagEnabled(context, "generate_release_package"),
            StructuredInputReceived: FlagEnabled(context, "structured_input_received"),
            ChiefEngineerInvoked: true,
            GatewayInvoked: FlagEnabled(context, "gateway_invoked"),
            SolidWorksRouterTriggered: true);
    }

    public bool ShouldRun(AgentContext context)
    {
        if (ContextValueEquals(context, BuildCompleteDrawingPackage, "operation"))
        {
            return ContextValueEquals(context, PlateBasicFourHoles, "part_type", "cad_model_type", "solidworks_model_type");
        }

        if (FlagEnabled(context, "solidworks_main_workflow"))
        {
            return true;
        }

        if (TryGetSharedStateModelSpec(context) is { } sharedSpec &&
            IsSupportedModelType(sharedSpec.ModelType))
        {
            return true;
        }

        return ContextValueEquals(context, PlateBasicFourHoles, "part_type", "cad_model_type", "solidworks_model_type", "part_name", "solidworks_part_name", "cad_model_name") ||
               context.Input.Message.Contains(PlateBasicFourHoles, StringComparison.OrdinalIgnoreCase);
    }

    public static CADModelSpec CreatePlateBasicFourHolesSpec(
        string id = "cad-model-spec-plate-basic-4holes",
        string description = "Basic 160 x 80 x 12 mm plate with four 10 mm through holes.",
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyList<string>? constraints = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["length_mm"] = ValueOrDefault(values, "length_mm", "160"),
            ["width_mm"] = ValueOrDefault(values, "width_mm", "80"),
            ["thickness_mm"] = ValueOrDefault(values, "thickness_mm", "12"),
            ["hole_diameter_mm"] = ValueOrDefault(values, "hole_diameter_mm", "10"),
            ["hole_count"] = ValueOrDefault(values, "hole_count", "4")
        };
        var material = FirstValue(values, "material", "solidworks_material") ?? "Q235";

        return new CADModelSpec(
            id,
            PlateBasicFourHoles,
            PlateBasicFourHoles,
            description,
            parameters,
            [material],
            constraints ?? ["main_workflow_controlled_plate_basic_4holes_only"]);
    }

    private static CADModelSpec? ResolveModelSpec(AgentContext context)
    {
        var sharedSpec = TryGetSharedStateModelSpec(context);
        if (sharedSpec is not null && IsSupportedModelType(sharedSpec.ModelType))
        {
            return sharedSpec;
        }

        return ContextValueEquals(context, PlateBasicFourHoles, "part_type", "cad_model_type", "solidworks_model_type", "part_name", "solidworks_part_name", "cad_model_name")
            ? CreatePlateBasicFourHolesSpec(values: context.Input.Context)
            : null;
    }

    private static CADModelSpec? TryGetSharedStateModelSpec(AgentContext context)
    {
        foreach (var key in new[] { "cad_model_spec", "solidworks_model_spec", "model_spec" })
        {
            if (context.SharedState.TryGetValue(key, out var value) && value is CADModelSpec spec)
            {
                return spec;
            }
        }

        return null;
    }

    private static bool IsSupportedModelType(string modelType) =>
        string.Equals(modelType, PlateBasicFourHoles, StringComparison.OrdinalIgnoreCase);

    private static bool ContextValueEquals(AgentContext context, string expectedValue, params string[] keys) =>
        keys.Any(key =>
            context.Input.Context.TryGetValue(key, out var value) &&
            string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase));

    private static bool FlagEnabled(AgentContext context, string key) =>
        context.Input.Context.TryGetValue(key, out var value) &&
        IsTrue(value);

    private static bool FlagDisabled(AgentContext context, string key) =>
        context.Input.Context.TryGetValue(key, out var value) &&
        IsFalse(value);

    private static bool IsTrue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string value) =>
        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);

    private static string ResolveProjectRoot(AgentContext context) =>
        context.Input.Context.TryGetValue("project_root", out var projectRoot) &&
        !string.IsNullOrWhiteSpace(projectRoot)
            ? Path.GetFullPath(projectRoot)
            : PlatformPathResolver.FindProjectRoot();

    private static string ResolveSolidWorksOutputDirectory(AgentContext context, string projectRoot)
    {
        if (context.Input.Context.TryGetValue("solidworks_output_directory", out var outputDirectory) &&
            !string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        return Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "main_workflow",
            PlateBasicFourHoles,
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));
    }

    private static string ValueOrDefault(IReadOnlyDictionary<string, string>? values, string key, string fallback) =>
        FirstValue(values, key, $"solidworks_{key}") ?? fallback;

    private static string? FirstValue(IReadOnlyDictionary<string, string>? values, params string[] keys)
    {
        if (values is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
