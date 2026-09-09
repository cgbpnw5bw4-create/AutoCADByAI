using AgentContracts;
using DomainSchemas;
using PlatformCore.Modules.CADModeling;
using System.Text.Json;

namespace PlatformCore;

public sealed class SolidWorksWorkflowRouter
{
    private readonly PartTypeRegistry _partTypeRegistry;

    public SolidWorksWorkflowRouter(PartTypeRegistry? partTypeRegistry = null)
    {
        _partTypeRegistry = partTypeRegistry ?? PartTypeRegistry.CreateDefault();
    }

    public SolidWorksMainWorkflowRequest? TryBuildRequest(AgentContext context)
    {
        var inputIssues = ValidateStructuredInput(context);
        if (inputIssues.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", inputIssues), nameof(context));
        }

        if (!ShouldRun(context))
        {
            return null;
        }

        var projectRoot = ResolveProjectRoot(context);
        var modelSpec = ResolveModelSpec(context) ??
            CreatePlateBasicFourHolesSpec(
                id: "cad-model-spec-plate-basic-4holes-main-workflow",
                description: "Main workflow controlled SolidWorks plate_basic_4holes build.",
                values: context.Input.Context,
                constraints: ["main_workflow_controlled_real_cad_uses_default_on_runtime_policy"]);
        var outputDirectory = ResolveSolidWorksOutputDirectory(context, projectRoot, modelSpec.PartType);
        var dryRun = FlagEnabled(context, "dry_run");
        var isCompleteDrawingPackage = ContextValueEquals(
            context,
            SolidWorksE2eCliContract.CompleteDrawingPackageOperation,
            "operation");
        var isPartFamilyReleasePackage = ContextValueEquals(
            context,
            SolidWorksE2eCliContract.PartFamilyReleasePackageOperation,
            "operation");
        var isModelUpdateReleasePackage = ContextValueEquals(
            context,
            SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation,
            "operation");

        return new SolidWorksMainWorkflowRequest(
            $"solidworks-main-workflow-{context.TaskId}",
            context.TaskId,
            projectRoot,
            outputDirectory,
            dryRun,
            AllowRealCadExecution: !dryRun,
            ModelSpec: modelSpec,
            Operation: isCompleteDrawingPackage
                ? SolidWorksMainWorkflowOperation.BuildCompleteDrawingPackage
                : isPartFamilyReleasePackage
                ? SolidWorksMainWorkflowOperation.BuildPartFamilyReleasePackage
                : isModelUpdateReleasePackage
                    ? SolidWorksMainWorkflowOperation.BuildModelUpdateReleasePackage
                    : SolidWorksMainWorkflowOperation.BuildPlate,
            GenerateDrawing: FlagEnabled(context, "generate_drawing"),
            GenerateDimensions: FlagEnabled(context, "generate_dimensions"),
            GenerateTitleBlock: FlagEnabled(context, "generate_title_block"),
            GenerateReleasePackage: FlagEnabled(context, "generate_release_package"),
            StructuredInputReceived: FlagEnabled(context, "structured_input_received") || HasExplicitPartType(context),
            ChiefEngineerInvoked: true,
            GatewayInvoked: FlagEnabled(context, "gateway_invoked"),
            SolidWorksRouterTriggered: true,
            ParameterUpdate: ResolveParameterUpdate(context));
    }

    public bool ShouldRun(AgentContext context)
    {
        // 显式输入即为 CAD 意图；解析错误交给输入门禁，不得当成未提供。
        if (context.Input.Context.ContainsKey("cad_model_spec_json")) return true;

        if (ContextValueEquals(context, SolidWorksE2eCliContract.CompleteDrawingPackageOperation, "operation") ||
            ContextValueEquals(context, SolidWorksE2eCliContract.PartFamilyReleasePackageOperation, "operation") ||
            ContextValueEquals(context, SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation, "operation"))
        {
            // Explicit structured CAD operations must enter the workflow so an
            // unknown family returns unsupported_part_type instead of null.
            return HasExplicitPartType(context) ||
                   TryGetContextModelSpec(context) is not null ||
                   TryGetSharedStateModelSpec(context) is not null;
        }

        if (FlagEnabled(context, "solidworks_main_workflow"))
        {
            return true;
        }

        if (TryGetContextModelSpec(context) is not null)
        {
            return true;
        }

        if (TryGetSharedStateModelSpec(context) is not null)
        {
            // Shared CADModelSpec is structured CAD intent even when its family
            // is unknown. The validator owns the canonical rejection stage.
            return true;
        }

        var explicitPartType = ReadExplicitPartType(context);
        if (!string.IsNullOrWhiteSpace(explicitPartType))
        {
            // An explicit part_type is already a structured CAD intent. Route
            // unknown values into the workflow so the validator can return the
            // canonical unsupported_part_type failure before Worker dispatch.
            return true;
        }

        return _partTypeRegistry.GetAll().Any(definition =>
            context.Input.Message.Contains(definition.PartType, StringComparison.OrdinalIgnoreCase));
    }

    public static CADModelSpec CreatePlateBasicFourHolesSpec(
        string id = "cad-model-spec-plate-basic-4holes",
        string description = "Basic 160 x 80 x 12 mm plate with four 10 mm through holes.",
        IReadOnlyDictionary<string, string>? values = null,
        IReadOnlyList<string>? constraints = null)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["length_mm"] = ValueOrDefault(values, "length_mm", "160"),
            ["width_mm"] = ValueOrDefault(values, "width_mm", "80"),
            ["thickness_mm"] = ValueOrDefault(values, "thickness_mm", "12"),
            ["hole_diameter_mm"] = ValueOrDefault(values, "hole_diameter_mm", "10"),
            ["hole_count"] = ValueOrDefault(values, "hole_count", "4")
        };

        return CreateSpec(
            id,
            PlateBasic4HolesDefinition.Type,
            description,
            dimensions,
            values,
            constraints ?? ["main_workflow_controlled_plate_basic_4holes_only"]);
    }

    public CADModelSpec CreatePartFamilySpec(
        string partType,
        IReadOnlyDictionary<string, string> values,
        string? id = null,
        string? description = null)
    {
        var dimensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_partTypeRegistry.TryGetDefinition(partType, out var definition))
        {
            foreach (var parameter in definition.ParameterSchema)
            {
                var value = FirstValue(values, parameter.Name, $"solidworks_{parameter.Name}") ??
                    parameter.DefaultValue;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    dimensions[parameter.Name] = value;
                }
            }
        }
        else
        {
            foreach (var parameter in values.Where(item => item.Key.EndsWith("_mm", StringComparison.OrdinalIgnoreCase) ||
                                                          item.Key.Contains("count", StringComparison.OrdinalIgnoreCase)))
            {
                dimensions[parameter.Key] = parameter.Value;
            }
        }

        return CreateSpec(
            id ?? $"cad-model-spec-{partType}-{Guid.NewGuid():N}",
            partType,
            description ?? $"Structured {partType} CAD model request.",
            dimensions,
            values,
            ["part_family_registry_dispatch_required"]);
    }

    private CADModelSpec? ResolveModelSpec(AgentContext context)
    {
        var contextSpec = TryGetContextModelSpec(context);
        if (contextSpec is not null)
        {
            return contextSpec;
        }

        var sharedSpec = TryGetSharedStateModelSpec(context);
        if (sharedSpec is not null)
        {
            return sharedSpec;
        }

        var explicitPartType = ReadExplicitPartType(context);
        if (!string.IsNullOrWhiteSpace(explicitPartType))
        {
            return CreatePartFamilySpec(explicitPartType, context.Input.Context);
        }

        var messageFamily = _partTypeRegistry.GetAll().FirstOrDefault(definition =>
            context.Input.Message.Contains(definition.PartType, StringComparison.OrdinalIgnoreCase));
        return messageFamily is null
            ? null
            : CreatePartFamilySpec(messageFamily.PartType, context.Input.Context);
    }

    private static CADModelSpec? TryGetContextModelSpec(AgentContext context)
    {
        if (!context.Input.Context.TryGetValue("cad_model_spec_json", out var json))
        {
            return null;
        }

        return ReadStructuredValue<CADModelSpec>(json, "cad_model_spec_json");
    }

    public static IReadOnlyList<string> ValidateStructuredInput(AgentContext context)
    {
        var issues = new List<string>();
        foreach (var key in new[] { "cad_model_spec_json", "parameter_update_json" })
        {
            if (!context.Input.Context.ContainsKey(key)) continue;
            try
            {
                if (key == "cad_model_spec_json")
                {
                    var spec = TryGetContextModelSpec(context)!;
                    EnsureModelStructure(spec);
                }
                else
                {
                    var update = ResolveParameterUpdate(context)!;
                    if (update.NewParameters is null || update.NewParameters.Count == 0 || update.NewParameters.Values.Any(value => value is null))
                        throw new JsonException("new_parameters 必须包含需要更新的参数。");
                    if (!ContextValueEquals(context, SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation, "operation") ||
                        (!context.Input.Context.ContainsKey("cad_model_spec_json") && !HasExplicitPartType(context) && TryGetSharedStateModelSpec(context) is null))
                        throw new JsonException("参数更新必须同时指定更新 operation 与明确模型或 part_type，不能回退默认模型。");
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException or FormatException or NotSupportedException)
            {
                var stage = key == "cad_model_spec_json" ? "invalid_cad_model_spec" : "invalid_parameter_update";
                issues.Add($"{stage}: {key}: {exception.Message} 修正结构化输入后重新提交。");
            }
        }
        return issues;
    }

    private static T ReadStructuredValue<T>(string json, string key) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException($"{key} 不能为空。");
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{key} 必须为 JSON 对象，不能为 null 或数组。");
        return document.RootElement.Deserialize<T>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }) ?? throw new JsonException($"{key} 不能为 null。");
    }

    internal static void EnsureModelStructure(CADModelSpec spec)
    {
        if (spec.Features.Any(feature => feature is null) || spec.Sketches.Any(sketch => sketch is null))
            throw new JsonException("features 和 sketches 不得包含 null 节点。");
        void CheckValues(IReadOnlyDictionary<string, string> values)
        {
            if (values.Values.Any(value => value is null)) throw new JsonException("参数字典不得包含 null 值。");
        }
        CheckValues(spec.Parameters);
        foreach (var feature in spec.Features) CheckValues(feature.Parameters);
        foreach (var sketch in spec.Sketches)
        {
            if (sketch.Entities.Any(entity => entity is null) || sketch.Constraints.Any(constraint => constraint is null))
                throw new JsonException("草图 entities 和 constraints 不得包含 null 节点。");
            CheckValues(sketch.Dimensions);
            foreach (var entity in sketch.Entities) CheckValues(entity.Parameters);
            foreach (var constraint in sketch.Constraints) CheckValues(constraint.Parameters);
        }
    }

    private static ModelParameterUpdateRequest? ResolveParameterUpdate(AgentContext context)
    {
        if (!context.Input.Context.TryGetValue("parameter_update_json", out var json))
        {
            return null;
        }

        return ReadStructuredValue<ModelParameterUpdateRequest>(json, "parameter_update_json");
    }

    private static CADModelSpec CreateSpec(
        string id,
        string partType,
        string description,
        IReadOnlyDictionary<string, string> dimensions,
        IReadOnlyDictionary<string, string>? values,
        IReadOnlyList<string> constraints)
    {
        var material = FirstValue(values, "material", "solidworks_material") ?? "Q235";
        var features = values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : values.Where(item => item.Key.StartsWith("feature_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var drawing = values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : values.Where(item => item.Key.StartsWith("drawing_", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var execution = values is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : values.Where(item => item.Key is "dry_run")
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        var outputs = SplitList(FirstValue(values, "output_requirements"));
        if (outputs.Count == 0)
        {
            outputs = ["SLDPRT", "STEP", "build_report.json"];
        }

        return new CADModelSpec(
            id,
            partType,
            dimensions,
            features,
            material,
            outputs,
            drawing,
            execution,
            title: partType,
            description,
            constraints);
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

    private static bool HasExplicitPartType(AgentContext context) =>
        !string.IsNullOrWhiteSpace(ReadExplicitPartType(context));

    private static string? ReadExplicitPartType(AgentContext context) =>
        FirstValue(context.Input.Context, "part_type", "cad_model_type", "solidworks_model_type", "part_name", "solidworks_part_name", "cad_model_name");

    private static bool ContextValueEquals(AgentContext context, string expectedValue, params string[] keys) =>
        keys.Any(key => context.Input.Context.TryGetValue(key, out var value) &&
                        string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase));

    private static bool FlagEnabled(AgentContext context, string key) =>
        context.Input.Context.TryGetValue(key, out var value) && IsTrue(value);

    private static bool FlagDisabled(AgentContext context, string key) =>
        context.Input.Context.TryGetValue(key, out var value) && IsFalse(value);

    private static bool IsTrue(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static bool IsFalse(string value) =>
        string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "no", StringComparison.OrdinalIgnoreCase);

    private static string ResolveProjectRoot(AgentContext context) =>
        context.Input.Context.TryGetValue("project_root", out var projectRoot) && !string.IsNullOrWhiteSpace(projectRoot)
            ? Path.GetFullPath(projectRoot)
            : PlatformPathResolver.FindProjectRoot();

    private static string ResolveSolidWorksOutputDirectory(AgentContext context, string projectRoot, string partType)
    {
        if (context.Input.Context.TryGetValue("solidworks_output_directory", out var outputDirectory) && !string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.GetFullPath(outputDirectory);
        }

        return Path.GetFullPath(Path.Combine(
            projectRoot,
            "output",
            "solidworks",
            "main_workflow",
            SanitizePathSegment(partType),
            $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}"));
    }

    private static string SanitizePathSegment(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

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

    private static IReadOnlyList<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split([',', ';', '|'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
