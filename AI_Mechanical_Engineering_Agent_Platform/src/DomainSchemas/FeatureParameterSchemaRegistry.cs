using System.Globalization;

namespace DomainSchemas;

public sealed record FeatureParameterValidationResult(
    bool IsValid,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public static FeatureParameterValidationResult Passed() =>
        new(true, null, Array.Empty<string>());

    public static FeatureParameterValidationResult Failed(FeatureDefinition feature) =>
        new(
            false,
            PartFamilyFailureStages.InvalidFeatureParameter,
            [$"invalid_feature_parameter: feature {feature.FeatureId} has invalid parameters for {feature.FeatureType}."]);
}

public interface IFeatureParameterSchema
{
    string FeatureType { get; }

    FeatureParameterValidationResult Validate(FeatureDefinition feature);
}

/// <summary>
/// CAD-system-neutral feature parameter schemas. Runtime COM evidence and
/// execution remain owned by the SolidWorks feature-handler registry.
/// </summary>
public sealed class FeatureParameterSchemaRegistry
{
    private readonly Dictionary<string, IFeatureParameterSchema> _schemas =
        new(StringComparer.OrdinalIgnoreCase);

    public FeatureParameterSchemaRegistry(IEnumerable<IFeatureParameterSchema>? schemas = null)
    {
        foreach (var schema in schemas ?? Array.Empty<IFeatureParameterSchema>())
        {
            Register(schema);
        }
    }

    public static FeatureParameterSchemaRegistry CreateDefault() =>
        new(
        [
            Schema(FeatureTypes.ExtrudeBoss, feature => Positive(feature, "depth_mm")),
            Schema(FeatureTypes.ExtrudeCut, feature => True(feature, "through_all") || Positive(feature, "depth_mm")),
            Schema(FeatureTypes.RevolveBoss, ValidRevolve),
            Schema(FeatureTypes.RevolveCut, ValidRevolve),
            Schema(FeatureTypes.Hole, feature => Positive(feature, "hole_diameter_mm") || Positive(feature, "diameter_mm")),
            Schema(FeatureTypes.Fillet, feature => Positive(feature, "radius_mm")),
            Schema(FeatureTypes.Chamfer, feature => Positive(feature, "distance_mm") || Positive(feature, "size_mm")),
            Schema(FeatureTypes.LinearPattern, ValidPattern),
            Schema(FeatureTypes.CircularPattern, ValidPattern),
            Schema(FeatureTypes.Mirror, feature => feature.ReferencedFeatures.Count > 0)
        ]);

    public void Register(IFeatureParameterSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.FeatureType) || !_schemas.TryAdd(schema.FeatureType, schema))
        {
            throw new InvalidOperationException(
                $"A feature parameter schema is already registered for {schema.FeatureType}.");
        }
    }

    public FeatureParameterValidationResult Validate(FeatureDefinition feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _schemas.TryGetValue(feature.FeatureType, out var schema)
            ? schema.Validate(feature)
            : FeatureParameterValidationResult.Passed();
    }

    public bool TryGetSchema(string featureType, out IFeatureParameterSchema schema) =>
        _schemas.TryGetValue(featureType, out schema!);

    public IReadOnlyList<IFeatureParameterSchema> GetAll() =>
        _schemas.Values.OrderBy(schema => schema.FeatureType, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IFeatureParameterSchema Schema(
        string featureType,
        Func<FeatureDefinition, bool> predicate) =>
        new DelegateFeatureParameterSchema(featureType, predicate);

    private static bool Positive(FeatureDefinition feature, string name) =>
        feature.Parameters.TryGetValue(name, out var text) &&
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
        double.IsFinite(value) &&
        value > 0;

    private static bool True(FeatureDefinition feature, string name) =>
        feature.Parameters.TryGetValue(name, out var text) &&
        bool.TryParse(text, out var value) &&
        value;

    private static bool ValidRevolve(FeatureDefinition feature) =>
        Positive(feature, "angle_degrees") &&
        double.Parse(feature.Parameters["angle_degrees"], CultureInfo.InvariantCulture) <= 360;

    /// <summary>
    /// 阵列实例数的规格层判据。
    /// <para>
    /// 参数名必须与 Handler 声明的 schema 一致。此处原本要求 <c>count</c>，
    /// 而 Handler 声明的是 <c>instance_count</c> 且会拒绝未声明参数——两层互相
    /// 矛盾，任何阵列规格都不可能同时通过。该矛盾一直没暴露，只因为阵列从未
    /// 真正执行过；V2.1-A 接通真实执行时它才第一次挡住路。
    /// </para>
    /// </summary>
    private static bool ValidPattern(FeatureDefinition feature) =>
        feature.Parameters.TryGetValue("instance_count", out var countText) &&
        int.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) &&
        count >= 2;

    private sealed class DelegateFeatureParameterSchema(
        string featureType,
        Func<FeatureDefinition, bool> predicate) : IFeatureParameterSchema
    {
        public string FeatureType { get; } = featureType;

        public FeatureParameterValidationResult Validate(FeatureDefinition feature) =>
            predicate(feature)
                ? FeatureParameterValidationResult.Passed()
                : FeatureParameterValidationResult.Failed(feature);
    }
}
