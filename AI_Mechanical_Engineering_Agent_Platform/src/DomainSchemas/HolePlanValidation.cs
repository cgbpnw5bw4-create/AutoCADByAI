using System.Text.Json;

namespace DomainSchemas;

// 从实际执行计划重新构造图进行前置校验，拒绝修改编译后参数绕过输入检查。
public static class HolePlanValidation
{
    public static CADModelSpec Reconstruct(SolidWorksBuildPlan plan)
    {
        var ids = plan.Operations.ToDictionary(o => o.OperationId,
            o => o.Parameters.GetValueOrDefault("feature_id") ?? o.Parameters.GetValueOrDefault("sketch_id") ?? o.OperationId,
            StringComparer.OrdinalIgnoreCase);
        var sketches = plan.Operations.Where(o => o.OperationType.Equals("CreateSketch", StringComparison.OrdinalIgnoreCase)).Select(o => new SketchDefinition(
            o.Parameters.GetValueOrDefault("sketch_id") ?? o.OperationId, o.SketchPlane,
            JsonSerializer.Deserialize<SketchEntity[]>(o.Parameters.GetValueOrDefault("entities") ?? "[]") ?? [],
            JsonSerializer.Deserialize<SketchConstraint[]>(o.Parameters.GetValueOrDefault("constraints") ?? "[]") ?? [])).ToArray();
        var excluded = new HashSet<string>(["CreateSketch", "CreateCenterLine", "SavePart", "ExportStep"], StringComparer.OrdinalIgnoreCase);
        var features = plan.Operations.Where(o => !excluded.Contains(o.OperationType))
            .Select(o => new FeatureDefinition(ids[o.OperationId], o.Parameters.GetValueOrDefault("feature_type") ?? "", o.Parameters,
                o.DependsOn.Select(d => ids.GetValueOrDefault(d) ?? d).ToArray(),
                o.Parameters.TryGetValue("sketch_id", out var sketch) ? [sketch] : [])).ToArray();
        return new(plan.SourceCadModelSpecId, plan.PartType, plan.Unit, plan.Dimensions, sketches: sketches, features: features);
    }
    public static HoleValidationResult? Validate(SolidWorksBuildPlan plan)
    {
        if (!plan.Operations.Any(o => o.Parameters.ContainsKey("hole_type") || o.OperationType.Equals("CreateHole", StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            if (!string.Equals(plan.Unit, "mm", StringComparison.OrdinalIgnoreCase))
                return new(null, PartFamilyFailureStages.InvalidHoleParameter, ["invalid_hole_parameter: 孔定义仅接受 mm。"]);
            if (plan.Operations.Any(o => (o.Parameters.ContainsKey("hole_type") || o.OperationType.Equals("CreateHole", StringComparison.OrdinalIgnoreCase)) &&
                (!o.Parameters.ContainsKey("hole_type") || !string.Equals(o.Parameters.GetValueOrDefault("feature_type"), FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase) ||
                 !o.OperationType.Equals("CreateHole", StringComparison.OrdinalIgnoreCase))))
                return new(null, PartFamilyFailureStages.InvalidHoleParameter, ["invalid_hole_parameter: hole_type 只能位于显式 hole/CreateHole 操作，不能通过删除或篡改标识跳过校验。"]);
            var spec = Reconstruct(plan);
            foreach (var feature in spec.Features.Where(f => f.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase)))
            {
                var result = new HoleValidator().Validate(feature, spec);
                if (!result.IsValid) return result;
            }
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        { return new(null, PartFamilyFailureStages.InvalidHoleParameter, [$"invalid_hole_parameter: 计划引用无法解析：{ex.Message}"]); }
    }
}
