using System.Text.Json;
using DomainSchemas;

namespace PlatformCore;

public static class EngineeringPlanParser
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public static EngineeringPlan Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 65536)
            throw new JsonException("engineering_plan_structure_invalid: 计划为空或超过 65536 字符。");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 20 });
        var root = document.RootElement;
        RequireObject(root, ["schema_version", "requirement", "decision", "rationale", "cad_model_spec",
            "assumptions", "missing_parameters", "risks"]);
        foreach (var key in new[] { "schema_version", "requirement", "decision", "rationale" }) RequireString(root, key);
        foreach (var key in new[] { "assumptions", "missing_parameters", "risks" })
        {
            var value = root.GetProperty(key);
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 32 ||
                value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
                throw new JsonException($"engineering_plan_structure_invalid: {key} 必须是非空字符串项数组。");
        }
        var cad = root.GetProperty("cad_model_spec");
        if (cad.ValueKind != JsonValueKind.Null)
        {
            // 先检查原始 JSON，再调用既有兼容 converter；避免别名、缺省值或未知字段被静默吞掉。
            RequireObject(cad, ["model_id", "model_type", "unit", "parameters", "material", "output_requirements"]);
            foreach (var key in new[] { "model_id", "model_type", "unit", "material" }) RequireString(cad, key);
            var parameters = cad.GetProperty("parameters");
            if (parameters.ValueKind != JsonValueKind.Object) throw new JsonException("engineering_plan_structure_invalid: parameters 必须是对象。");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in parameters.EnumerateObject())
                if (!names.Add(item.Name) || item.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.Value.GetString()))
                    throw new JsonException("engineering_plan_structure_invalid: 参数重复或不是非空字符串。");
            var outputs = cad.GetProperty("output_requirements");
            if (outputs.ValueKind != JsonValueKind.Array || outputs.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                throw new JsonException("engineering_plan_structure_invalid: output_requirements 必须是字符串数组。");
        }
        return JsonSerializer.Deserialize<EngineeringPlan>(json, JsonOptions)
            ?? throw new JsonException("engineering_plan_structure_invalid: 无法解析计划。");
    }

    public static string Serialize(EngineeringPlan plan) => JsonSerializer.Serialize(new
    {
        schema_version = plan.SchemaVersion, requirement = plan.Requirement, decision = plan.Decision, rationale = plan.Rationale,
        cad_model_spec = plan.CadModelSpec is null ? null : new
        {
            model_id = plan.CadModelSpec.ModelId, model_type = plan.CadModelSpec.ModelType, unit = plan.CadModelSpec.Unit,
            parameters = plan.CadModelSpec.Parameters, material = plan.CadModelSpec.Material,
            output_requirements = plan.CadModelSpec.OutputRequirements
        },
        assumptions = plan.Assumptions, missing_parameters = plan.MissingParameters, risks = plan.Risks
    }, JsonOptions);

    private static void RequireString(JsonElement parent, string name)
    {
        if (parent.GetProperty(name).ValueKind != JsonValueKind.String)
            throw new JsonException($"engineering_plan_structure_invalid: {name} 必须是字符串。");
    }

    private static void RequireObject(JsonElement value, string[] fields)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new JsonException("engineering_plan_structure_invalid: 必须是对象。");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!fields.Contains(item.Name, StringComparer.Ordinal) || !seen.Add(item.Name))
                throw new JsonException($"engineering_plan_structure_invalid: 未知或重复字段 {item.Name}。");
        if (seen.Count != fields.Length) throw new JsonException("engineering_plan_structure_invalid: 缺少必要字段。");
    }
}
