using System.Text.Json;
using System.Text.Json.Nodes;
using AgentContracts;
using DomainSchemas;
using ModelRuntime;

namespace PlatformCore;

// 注入模型替身并强制 dry-run；此组行为检查不访问网络或 COM，也不证明真实 CAD 验收。
public static class EngineeringPlanningSelfCheck
{
    public const string Requirement = "建模圆筒夹套，外径140 mm，内径120 mm，轴向长度180 mm。输出SLDPRT和STEP。";

    public static async Task<IReadOnlyDictionary<string, bool>> RunAsync(string projectRoot, string outputRoot)
    {
        var checks = new Dictionary<string, bool>();
        var json = SampleJson(Requirement);
        var provider = new ProbeProvider(json);
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        platform.EngineeringPlanningRuntime = new ModelRuntime.ModelRuntime(provider);
        var runId = $"engineering-self-check-{Guid.NewGuid():N}";
        var context = new Dictionary<string, string>
        {
            ["engineering_planning"] = "true", ["dry_run"] = "true", ["project_root"] = projectRoot,
            ["request_id"] = runId, ["engineering_plan_source"] = "self-check-probe",
            ["solidworks_output_directory"] = Path.Combine(outputRoot, "engineering_planning", runId)
        };
        var service = new AgentTaskService(platform);
        var creation = await service.ExecuteAsync("chief-engineer",
            new AgentInput("self-check", "local", runId, "self-check", Requirement, [], context));
        checks["engineering_plan_model_contract_executed"] = provider.Calls == 1 &&
            provider.Request?.Purpose == EngineeringModelPurpose.DesignPlanning && provider.Request.UserMessage == Requirement;
        var validation = new EngineeringPlanValidator().Validate(EngineeringPlanParser.Parse(json), Requirement, runId);
        checks["engineering_plan_maps_existing_cad_schema"] = validation.IsValid &&
            validation.ModelSpec is { ModelType: "jacket_basic", RequiresFeatureHandlerPipeline: true } spec &&
            spec.Sketches.Count > 0 && spec.Features.Count > 0 &&
            validation.BuildPlan?.ExecutionStrategy == SolidWorksBuildExecutionStrategies.FeatureHandlerGraph;
        checks["engineering_plan_public_route_dry_run_completed"] = creation?.Result.Output?.Status == AgentOutputStatus.Completed &&
            creation.Result.GateDecision?.Result == GateDecisionResult.Passed &&
            creation.Result.Output.InternalCollaborationReport?.StepResults?.Any(step => step.StepId == EngineeringPlanningStep.Id) == true &&
            creation.Result.Output.Artifacts.Any(artifact => artifact.Path.EndsWith(".SLDPRT.txt", StringComparison.OrdinalIgnoreCase)) &&
            creation.Result.Output.Artifacts.Any(artifact => artifact.Path.EndsWith(".STEP.txt", StringComparison.OrdinalIgnoreCase)) &&
            !creation.Result.Output.Artifacts.Any(artifact => artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) ||
                artifact.Path.EndsWith(".STEP", StringComparison.OrdinalIgnoreCase));

        var malformedBlocked = false;
        try { EngineeringPlanParser.Parse("{\"decision\":\"ready\"}"); }
        catch (JsonException) { malformedBlocked = true; }
        var missing = JsonNode.Parse(json)!.AsObject();
        missing["cad_model_spec"]!["parameters"]!.AsObject().Remove("length_mm");
        var missingValidation = new EngineeringPlanValidator().Validate(EngineeringPlanParser.Parse(missing.ToJsonString()), Requirement, runId);
        var risk = JsonNode.Parse(json)!.AsObject();
        risk["risks"] = new JsonArray("承压能力未校验");
        var riskValidation = new EngineeringPlanValidator().Validate(EngineeringPlanParser.Parse(risk.ToJsonString()), Requirement, runId);
        checks["engineering_plan_invalid_inputs_blocked"] = malformedBlocked && !missingValidation.IsValid &&
            missingValidation.ModelSpec is null && !riskValidation.IsValid && riskValidation.BuildPlan is null;
        var unavailable = PlatformBootstrapper.CreateDefault(projectRoot);
        var blocked = await new AgentTaskService(unavailable).ExecuteAsync("chief-engineer",
            new AgentInput("self-check", "local", runId + "-unavailable", "self-check", Requirement, [], context));
        checks["engineering_plan_missing_runtime_rejected"] = blocked?.Result.Output?.Status != AgentOutputStatus.Completed &&
            blocked?.Result.Output?.InternalCollaborationReport?.StepResults?.All(step => step.StepId == EngineeringPlanningStep.Id) == true &&
            !blocked.Result.Output.Artifacts.Any(artifact => artifact.Kind is "EngineeringPlan" or "EngineeringModelResponse" or
                "MappedCADModelSpec" or "SolidWorksBuildPlan" || artifact.Path.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase));
        return checks;
    }

    private static string SampleJson(string requirement) => JsonSerializer.Serialize(new
    {
        schema_version = "2.3", requirement, decision = "ready", rationale = "需求完整，映射已有受证夹套几何档案。",
        cad_model_spec = new
        {
            model_id = "self-check-jacket", model_type = "jacket_basic", unit = "mm",
            parameters = new Dictionary<string, string> { ["outer_diameter_mm"] = "140", ["inner_diameter_mm"] = "120", ["length_mm"] = "180" },
            material = "", output_requirements = new[] { "SLDPRT", "STEP" }
        },
        assumptions = Array.Empty<string>(), missing_parameters = Array.Empty<string>(), risks = Array.Empty<string>()
    });

    private sealed class ProbeProvider(string response) : IModelProvider
    {
        public int Calls { get; private set; }
        public ModelRequest? Request { get; private set; }
        public Task<string> GenerateAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Request = request;
            return Task.FromResult(response);
        }
    }
}
