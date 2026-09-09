using System.Text.Json;
using AgentContracts;
using DomainSchemas;

namespace PlatformCore;

public static class StructuredCadInputSelfCheck
{
    public static async Task<bool> RunAsync()
    {
        var platform = PlatformBootstrapper.CreateDefault();
        var chief = platform.AgentRegistry.GetById("chief-engineer");
        if (chief is null) return false;
        var router = new SolidWorksWorkflowRouter();
        var invalidInputs = new List<(Dictionary<string, string> Values, string Stage)>();
        foreach (var json in new[] { "{", "null", "", "[]", "{\"features\":[null]}" })
        {
            invalidInputs.Add((new() { ["cad_model_spec_json"] = json }, "invalid_cad_model_spec:"));
        }
        foreach (var json in new[] { "{", "null", "", "[]", "{}", "{\"new_parameters\":null}" })
        {
            invalidInputs.Add((new()
            {
                ["parameter_update_json"] = json,
                ["part_type"] = PlateBasic4HolesDefinition.Type,
                ["operation"] = SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation
            }, "invalid_parameter_update:"));
        }
        foreach (var hint in new[] { "none", "part_type", "operation", "wrong_operation" })
        {
            var values = new Dictionary<string, string>
            {
                ["parameter_update_json"] = "{\"new_parameters\":{\"length_mm\":\"200\"}}"
            };
            if (hint is "part_type" or "wrong_operation") values["part_type"] = PlateBasic4HolesDefinition.Type;
            if (hint == "operation") values["operation"] = SolidWorksE2eCliContract.ModelUpdateReleasePackageOperation;
            if (hint == "wrong_operation") values["operation"] = SolidWorksE2eCliContract.CompleteDrawingPackageOperation;
            invalidInputs.Add((values, "invalid_parameter_update:"));
        }

        foreach (var (values, stage) in invalidInputs)
        {
            values["dry_run"] = "true";
            var context = Context(values);
            if (!SolidWorksWorkflowRouter.ValidateStructuredInput(context).Any(issue =>
                    issue.StartsWith(stage, StringComparison.Ordinal))) return false;
            try
            {
                router.TryBuildRequest(context);
                return false;
            }
            catch (ArgumentException exception) when (exception.Message.Contains(stage, StringComparison.Ordinal))
            {
                // 路由必须显式拒绝，不能返回 null 或构造默认模型。
            }
            var output = await chief.ExecuteAsync(context);
            if (output.Status != AgentOutputStatus.Failed || output.Artifacts.Count != 0 ||
                !output.Issues.Any(issue => issue.StartsWith(stage, StringComparison.Ordinal)) ||
                platform.AuditLog.GetEntries().Any(entry => entry.Action == "workflow_started"))
            {
                return false;
            }
        }

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
        foreach (var regression in PartFamilyRegressionModels.CreateDefault())
        {
            var originalJson = JsonSerializer.Serialize(regression.Spec, jsonOptions);
            var context = Context(new()
            {
                ["cad_model_spec_json"] = originalJson,
                ["part_type"] = "unrecognized_legacy_hint",
                ["dry_run"] = "true"
            });
            if (SolidWorksWorkflowRouter.ValidateStructuredInput(context).Count != 0) return false;
            var request = router.TryBuildRequest(context);
            if (request?.ModelSpec is null || !request.DryRun || request.AllowRealCadExecution ||
                JsonSerializer.Serialize(request.ModelSpec, jsonOptions) != originalJson)
            {
                return false;
            }
        }
        return true;
    }

    private static AgentContext Context(Dictionary<string, string> values) =>
        new($"structured-input-self-check-{Guid.NewGuid():N}",
            new AgentInput("self-check", "self-check", "structured-input", "self-check", "检查结构化 CAD 输入。", [], values),
            new Dictionary<string, object?>(), DateTimeOffset.UtcNow);
}
