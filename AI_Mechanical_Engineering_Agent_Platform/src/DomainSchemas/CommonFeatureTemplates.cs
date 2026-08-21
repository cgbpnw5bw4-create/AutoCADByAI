namespace DomainSchemas;

/// <summary>
/// 零件族共享的 FeatureGraph 小模板。模板只生成领域 DTO，
/// 不创建 BuildPlan、更不调用 SolidWorks API。
/// </summary>
public static class CommonFeatureTemplates
{
    public static SketchDefinition CreateCircleSketch(
        string sketchId,
        string entityId,
        string referencePlane,
        string dimensionName,
        string dimensionValue,
        IReadOnlyDictionary<string, string> additions,
        int executionOrder)
    {
        var parameters = new Dictionary<string, string>(additions, StringComparer.OrdinalIgnoreCase)
        {
            [dimensionName] = dimensionValue
        };
        return new SketchDefinition(
            sketchId,
            referencePlane,
            [new SketchEntity(entityId, SketchEntityTypes.Circle, parameters)],
            [CreateDimensionalConstraint($"{entityId}_dimension", entityId, dimensionName, dimensionValue)],
            new Dictionary<string, string> { [dimensionName] = dimensionValue },
            executionOrder);
    }

    public static SketchConstraint CreateDimensionalConstraint(
        string constraintId,
        string entityId,
        string parameterName,
        string value) =>
        new(
            constraintId,
            SketchConstraintTypes.Dimensional,
            [entityId],
            value,
            new Dictionary<string, string> { ["parameter"] = parameterName });
}
