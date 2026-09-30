using System.Text.Json;
using AgentContracts;
using DomainSchemas;
using ModelRuntime;
using QualityGate;

namespace PlatformCore;

/// <summary>总工程师在顺序工作流中生成与审查计划；只交接平台生成的 CAD 合同。</summary>
public sealed class EngineeringPlanningStep(
    AgentContext inputContext,
    ModelRuntime.ModelRuntime? runtime,
    InMemoryAuditLog auditLog)
{
    public const string Id = "engineering-plan-generation";
    public const string SystemPrompt = """
        你是机械总工程师，仅理解、规划和建议，不调用工具、Worker 或 SolidWorks API。
        只返回一个 JSON 对象，不加 Markdown、代码围栏或解释。必须严格包含：
        schema_version="2.3"、requirement=原始用户消息（逐字符保持）、decision="ready" 或 "reject"、
        rationale=中文工程依据、cad_model_spec、assumptions、missing_parameters、risks。
        后三个字段是字符串数组。无法确定、缺参、附加要求、风险或不在已受证档案内，decision 必须为 reject；
        cad_model_spec 可以为 null，不可编造参数、使用样例默认值或把 API 建议当作 evidence。
        首个档案仅为毫米制 jacket_basic：外径140、内径120、轴向长度180，单实体同轴直筒两端开口，输出SLDPRT和STEP。
        仅接纳原文明确三个尺寸和各自mm/毫米单位、要求夹套及两种输出的纯几何建模需求。
        CADModelSpec 直接复用现有合同；只填 model_id（安全英文标识）、model_type="jacket_basic"、unit="mm"、
        parameters（outer_diameter_mm/inner_diameter_mm/length_mm 三个字符串）、material=""、output_requirements=["SLDPRT","STEP"]。
        不得输出图、草图、API、执行选项或附加字段。平台将通过已有模板、FeatureGraph和BuildPlanCompiler生成受控图，
        再由Verified Worker、API Evidence与QualityGate决定是否执行与交付。不得声称材料、承载、压力或制造适用性已验证。
        """;

    public CADModelSpec? ValidatedSpec { get; private set; }
    public IReadOnlyList<ArtifactInfo> Artifacts { get; private set; } = [];
    private string? _outputDirectory;
    private static readonly HashSet<string> ConflictingContextKeys = new(StringComparer.OrdinalIgnoreCase)
        { "cad_model_spec_json", "parameter_update_json", "operation", "part_type", "cad_model_type", "solidworks_part_type", "model_type",
            "solidworks_model_type", "part_name", "solidworks_part_name", "cad_model_name" };
    private static readonly HashSet<string> ConflictingStateKeys = new(StringComparer.OrdinalIgnoreCase)
        { "cad_model_spec", "solidworks_model_spec", "model_spec" };

    public static bool IsRequested(AgentContext context) => context.Input.Context.Any(entry =>
        string.Equals(entry.Key, "engineering_planning", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(entry.Value, "true", StringComparison.OrdinalIgnoreCase));

    private string? ContextValue(string key) => inputContext.Input.Context.FirstOrDefault(entry =>
        string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    public WorkflowStep ToWorkflowStep() => new("工程计划生成与校验", ExecuteAsync, Id, MaxRetries: 0);

    public AgentContext ToCadContext()
    {
        if (ValidatedSpec is null) throw new InvalidOperationException("engineering_plan_not_validated: 不允许进入 CAD 路由。");
        var dryRun = string.Equals(ContextValue("dry_run"), "true", StringComparison.OrdinalIgnoreCase);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in new[] { "project_root", "request_id", "gateway_invoked", "dry_run", "engineering_planning", "engineering_plan_source" })
            if (ContextValue(key) is { } value) values[key] = value;
        values["operation"] = dryRun ? "build_plate" : SolidWorksE2eCliContract.PartFamilyReleasePackageOperation;
        foreach (var entry in new Dictionary<string, string>
        {
            ["structured_input_received"] = "true",
            ["generate_drawing"] = "false",
            ["generate_dimensions"] = "false",
            ["generate_title_block"] = "false",
            ["generate_release_package"] = dryRun ? "false" : "true",
            ["solidworks_output_directory"] = _outputDirectory!
        }) values[entry.Key] = entry.Value;
        var state = new Dictionary<string, object?>(inputContext.SharedState)
        {
            ["cad_model_spec"] = ValidatedSpec
        };
        return inputContext with { Input = inputContext.Input with { Context = values }, SharedState = state };
    }

    private async Task<WorkflowStepResult> ExecuteAsync(WorkflowContext workflowContext)
    {
        var issues = new List<string>();
        var artifacts = new List<ArtifactInfo>();
        EngineeringPlanValidationResult? validated = null;
        if (runtime is null) issues.Add("engineering_model_not_configured: 实时规划需要已配置的 IModelProvider，禁止回退默认 CAD。");
        if (string.IsNullOrWhiteSpace(inputContext.Input.Message)) issues.Add("engineering_requirement_missing: 必须提供非空自然语言需求。");
        if (inputContext.Input.Context.Keys.Any(ConflictingContextKeys.Contains) ||
            inputContext.SharedState.Keys.Any(ConflictingStateKeys.Contains))
            issues.Add("engineering_plan_input_conflict: 规划入口不接纳另一路 CAD 输入或操作覆盖。");
        if (inputContext.Input.Context.Keys.GroupBy(key => key, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            issues.Add("engineering_plan_input_conflict: 上下文字段存在大小写歧义。");
        if (issues.Count == 0)
        {
            try
            {
                var response = await runtime!.GenerateAsync(new ModelRequest(EngineeringModelPurpose.DesignPlanning,
                    SystemPrompt, inputContext.Input.Message));
                var root = ContextValue("project_root") ?? PlatformPathResolver.FindProjectRoot();
                _outputDirectory = ContextValue("solidworks_output_directory") is { } output
                    ? Path.GetFullPath(output) : Path.Combine(root, "output", "solidworks", "engineering", $"{Guid.NewGuid():N}");
                Directory.CreateDirectory(_outputDirectory);
                artifacts.Add(await SaveAsync("model_response.json", "EngineeringModelResponse", response));
                artifacts.Add(await SaveAsync("model_invocation_report.json", "EngineeringModelInvocationReport", JsonSerializer.Serialize(new
                {
                    purpose = EngineeringModelPurpose.DesignPlanning.ToString(), model_calls = 1,
                    plan_source = ContextValue("engineering_plan_source") ?? "configured-provider",
                    requirement = inputContext.Input.Message, task_id = inputContext.TaskId,
                    real_cad_executed = false
                }, EngineeringPlanParser.JsonOptions)));
                var plan = EngineeringPlanParser.Parse(response);
                artifacts.Add(await SaveAsync("engineering_plan.json", "EngineeringPlan", EngineeringPlanParser.Serialize(plan)));
                validated = new EngineeringPlanValidator().Validate(plan, inputContext.Input.Message, inputContext.TaskId);
                issues.AddRange(validated.Issues);
                if (validated.IsValid)
                {
                    artifacts.Add(await SaveAsync("mapped_cad_model_spec.json", "MappedCADModelSpec", JsonSerializer.Serialize(validated.ModelSpec, EngineeringPlanParser.JsonOptions)));
                    artifacts.Add(await SaveAsync("engineering_build_plan.json", "SolidWorksBuildPlan", JsonSerializer.Serialize(validated.BuildPlan, EngineeringPlanParser.JsonOptions)));
                }
            }
            catch (JsonException exception) { issues.Add($"engineering_plan_structure_invalid: {exception.Message}"); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 不把提供商错误响应、凭据或私有端点写进业务报告。
                issues.Add($"engineering_plan_generation_failed: {exception.GetType().Name}，已阻断 Worker；核查模型配置、响应或产物目录。");
            }
        }
        var review = new ReviewReport($"engineering-plan-{inputContext.TaskId}", Id,
            issues.Count == 0, issues.Count == 0 ? 1 : 0, issues, RequiresHumanApproval: false, HasFatalError: issues.Count > 0);
        var gate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(review);
        if (gate.Decision.Result == GateDecisionResult.Passed && validated is { IsValid: true }) ValidatedSpec = validated.ModelSpec;
        Artifacts = artifacts;
        auditLog.Record("quality-gate", Id, "engineering_plan_validated", $"工程计划门禁：{gate.Decision.Result}，未调用 Worker 或 CAD API。");
        return new WorkflowStepResult(Id, "工程计划生成与校验", ValidatedSpec is not null ? WorkflowStepStatus.Passed : WorkflowStepStatus.Failed,
            ValidatedSpec is not null ? "工程计划通过结构、原文绑定、工程规则和既有计划校验。" : "工程计划被阻断，未进入 CAD Worker。",
            ReviewReport: review, GateDecision: gate.Decision, RejectReport: gate.RejectReport,
            Logs: ["ModelRuntime 仅规划；确定性平台负责映射和校验；既有 evidence 与最终 QualityGate 仍须独立通过。"], Issues: issues);
    }

    private async Task<ArtifactInfo> SaveAsync(string name, string kind, string content)
    {
        var path = Path.Combine(_outputDirectory!, name);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);
        return new ArtifactInfo($"engineering-{Guid.NewGuid():N}", name, kind, path, "application/json");
    }
}
