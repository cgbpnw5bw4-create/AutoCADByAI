using System.Text.Json.Serialization;

namespace DomainSchemas;

/// <summary>
/// Canonical, CAD-system-neutral description of one parameterized part.
/// The legacy constructor and aliases remain available for V1.7 callers.
/// </summary>
public sealed record CADModelSpec
{
    [JsonConstructor]
    public CADModelSpec(
        string id,
        string partType,
        IReadOnlyDictionary<string, string> dimensions,
        IReadOnlyDictionary<string, string>? features = null,
        string? material = null,
        IReadOnlyList<string>? outputRequirements = null,
        IReadOnlyDictionary<string, string>? drawingRequirements = null,
        IReadOnlyDictionary<string, string>? executionOptions = null,
        string? title = null,
        string? description = null,
        IReadOnlyList<string>? constraints = null)
    {
        Id = id;
        PartType = partType;
        ModelType = partType;
        Title = title ?? partType;
        Description = description ?? string.Empty;
        Dimensions = Copy(dimensions);
        Features = Copy(features);
        Material = material ?? string.Empty;
        OutputRequirements = outputRequirements?.ToArray() ?? Array.Empty<string>();
        DrawingRequirements = Copy(drawingRequirements);
        ExecutionOptions = Copy(executionOptions);
        Constraints = constraints?.ToArray() ?? Array.Empty<string>();
        Parameters = Merge(Dimensions, Features);
        Materials = string.IsNullOrWhiteSpace(Material) ? Array.Empty<string>() : [Material];
    }

    /// <summary>
    /// V1.7-compatible constructor. Older callers used ModelType="Part" and
    /// placed the actual family key in Title, so both shapes are recognized.
    /// </summary>
    public CADModelSpec(
        string id,
        string modelType,
        string title,
        string description,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<string> materials,
        IReadOnlyList<string> constraints)
        : this(
            id,
            ResolveLegacyPartType(modelType, title),
            parameters,
            features: null,
            material: materials.FirstOrDefault(),
            outputRequirements: ["SLDPRT", "STEP", "build_report.json"],
            drawingRequirements: null,
            executionOptions: null,
            title,
            description,
            constraints)
    {
        ModelType = modelType;
        Parameters = Copy(parameters);
        Materials = materials.ToArray();
    }

    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("part_type")]
    public string PartType { get; init; }

    [JsonPropertyName("dimensions")]
    public IReadOnlyDictionary<string, string> Dimensions { get; init; }

    [JsonPropertyName("features")]
    public IReadOnlyDictionary<string, string> Features { get; init; }

    [JsonPropertyName("material")]
    public string Material { get; init; }

    [JsonPropertyName("output_requirements")]
    public IReadOnlyList<string> OutputRequirements { get; init; }

    [JsonPropertyName("drawing_requirements")]
    public IReadOnlyDictionary<string, string> DrawingRequirements { get; init; }

    [JsonPropertyName("execution_options")]
    public IReadOnlyDictionary<string, string> ExecutionOptions { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; }

    // Compatibility aliases retained for existing V1.7 integrations.
    [JsonPropertyName("model_type")]
    public string ModelType { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters { get; init; }

    [JsonPropertyName("materials")]
    public IReadOnlyList<string> Materials { get; init; }

    [JsonPropertyName("constraints")]
    public IReadOnlyList<string> Constraints { get; init; }

    public bool TryGetParameter(string name, out string value)
    {
        if (Dimensions.TryGetValue(name, out value!) || Features.TryGetValue(name, out value!))
        {
            return true;
        }

        return Parameters.TryGetValue(name, out value!);
    }

    private static string ResolveLegacyPartType(string modelType, string title) =>
        string.Equals(modelType, "Part", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(title)
            ? title
            : modelType;

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string>? source) =>
        source is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> Merge(
        IReadOnlyDictionary<string, string> dimensions,
        IReadOnlyDictionary<string, string> features)
    {
        var merged = new Dictionary<string, string>(dimensions, StringComparer.OrdinalIgnoreCase);
        foreach (var feature in features)
        {
            merged[feature.Key] = feature.Value;
        }

        return merged;
    }
}
