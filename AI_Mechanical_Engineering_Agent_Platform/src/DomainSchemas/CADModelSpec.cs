using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainSchemas;

/// <summary>
/// Canonical, CAD-system-neutral description of one parameterized part.
/// The legacy constructor and aliases remain available for V1.7 callers.
/// </summary>
[JsonConverter(typeof(CADModelSpecJsonConverter))]
public sealed record CADModelSpec
{
    private string _modelId = string.Empty;
    private string _modelType = string.Empty;
    private IReadOnlyDictionary<string, string> _parameters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CADModelSpec(
        string modelId,
        string modelType,
        string unit,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, string>? referenceGeometry = null,
        IReadOnlyList<SketchDefinition>? sketches = null,
        IReadOnlyList<FeatureDefinition>? features = null,
        string? material = null,
        IReadOnlyList<string>? outputRequirements = null,
        IReadOnlyDictionary<string, string>? drawingRequirements = null,
        IReadOnlyDictionary<string, string>? executionOptions = null,
        IReadOnlyDictionary<string, string>? featureOptions = null,
        string? title = null,
        string? description = null,
        IReadOnlyList<string>? materials = null,
        IReadOnlyList<string>? constraints = null)
    {
        ModelId = modelId ?? string.Empty;
        ModelType = modelType ?? string.Empty;
        Unit = string.IsNullOrWhiteSpace(unit) ? "mm" : unit;
        Parameters = Copy(parameters);
        ReferenceGeometry = Copy(referenceGeometry);
        Sketches = sketches?.ToArray() ?? Array.Empty<SketchDefinition>();
        Features = features?.ToArray() ?? Array.Empty<FeatureDefinition>();
        Material = material ?? materials?.FirstOrDefault() ?? string.Empty;
        OutputRequirements = outputRequirements?.ToArray() ?? Array.Empty<string>();
        DrawingRequirements = Copy(drawingRequirements);
        ExecutionOptions = Copy(executionOptions);
        FeatureOptions = Copy(featureOptions);
        Title = title ?? ModelType;
        Description = description ?? string.Empty;
        Materials = materials?.ToArray() ??
                    (string.IsNullOrWhiteSpace(Material) ? Array.Empty<string>() : [Material]);
        Constraints = constraints?.ToArray() ?? Array.Empty<string>();

        // Compatibility aliases for the V1.7/V1.8 part-family pipeline.
    }

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
        ModelId = id;
        ModelType = partType;
        Unit = "mm";
        Title = title ?? partType;
        Description = description ?? string.Empty;
        FeatureOptions = Copy(features);
        Material = material ?? string.Empty;
        OutputRequirements = outputRequirements?.ToArray() ?? Array.Empty<string>();
        DrawingRequirements = Copy(drawingRequirements);
        ExecutionOptions = Copy(executionOptions);
        Constraints = constraints?.ToArray() ?? Array.Empty<string>();
        Parameters = Merge(Copy(dimensions), FeatureOptions);
        ReferenceGeometry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Sketches = Array.Empty<SketchDefinition>();
        Features = Array.Empty<FeatureDefinition>();
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
        Parameters = Copy(parameters);
        Materials = materials.ToArray();
    }

    [JsonPropertyName("model_id")]
    public string ModelId
    {
        get => _modelId;
        init => _modelId = value ?? string.Empty;
    }

    [JsonPropertyName("model_type")]
    public string ModelType
    {
        get => _modelType;
        init => _modelType = value ?? string.Empty;
    }

    [JsonPropertyName("unit")]
    public string Unit { get; init; }

    [JsonPropertyName("parameters")]
    public IReadOnlyDictionary<string, string> Parameters
    {
        get => _parameters;
        init => _parameters = Copy(value);
    }

    [JsonPropertyName("reference_geometry")]
    public IReadOnlyDictionary<string, string> ReferenceGeometry { get; init; }

    [JsonPropertyName("sketches")]
    public IReadOnlyList<SketchDefinition> Sketches { get; init; }

    [JsonPropertyName("features")]
    public IReadOnlyList<FeatureDefinition> Features { get; init; }

    [JsonIgnore]
    public IReadOnlyList<FeatureDefinition> FeatureDefinitions => Features;

    [JsonPropertyName("feature_options")]
    public IReadOnlyDictionary<string, string> FeatureOptions { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> FeatureParameters => FeatureOptions;

    [JsonIgnore]
    public string Id
    {
        get => _modelId;
        init => _modelId = value ?? string.Empty;
    }

    [JsonIgnore]
    public string PartType
    {
        get => _modelType;
        init => _modelType = value ?? string.Empty;
    }

    [JsonIgnore]
    public IReadOnlyDictionary<string, string> Dimensions
    {
        get => _parameters;
        init => _parameters = Copy(value);
    }

    [JsonPropertyName("material")]
    public string Material { get; init; }

    [JsonPropertyName("output_requirements")]
    public IReadOnlyList<string> OutputRequirements { get; init; }

    [JsonPropertyName("drawing_requirements")]
    public IReadOnlyDictionary<string, string> DrawingRequirements { get; init; }

    [JsonPropertyName("execution_options")]
    public IReadOnlyDictionary<string, string> ExecutionOptions { get; init; }

    /// <summary>
    /// Server-owned provenance marker. It is deliberately excluded from the wire
    /// contract so callers cannot promote an arbitrary plan into the real feature
    /// handler execution path through request JSON.
    /// </summary>
    [JsonIgnore]
    public bool RequiresFeatureHandlerPipeline { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; }

    // Compatibility aliases retained for existing V1.7 integrations.
    [JsonPropertyName("materials")]
    public IReadOnlyList<string> Materials { get; init; }

    [JsonPropertyName("constraints")]
    public IReadOnlyList<string> Constraints { get; init; }

    public bool TryGetParameter(string name, out string value)
    {
        if (Dimensions.TryGetValue(name, out value!) || FeatureOptions.TryGetValue(name, out value!))
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

public sealed class CADModelSpecJsonConverter : JsonConverter<CADModelSpec>
{
    public override CADModelSpec Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var legacyPartType = ReadString(root, "part_type");
        var modelType = ReadString(root, "model_type");
        if (string.IsNullOrWhiteSpace(modelType) ||
            string.Equals(modelType, "Part", StringComparison.OrdinalIgnoreCase))
        {
            modelType = legacyPartType ?? modelType;
        }

        var parameters = ReadDictionary(root, "parameters");
        MergeMissing(parameters, ReadDictionary(root, "dimensions"));
        var featureOptions = ReadDictionary(root, "feature_options");
        var featureDefinitions = Array.Empty<FeatureDefinition>();
        if (root.TryGetProperty("features", out var features))
        {
            if (features.ValueKind == JsonValueKind.Array)
            {
                featureDefinitions = JsonSerializer.Deserialize<FeatureDefinition[]>(
                                         features.GetRawText(),
                                         options)
                                     ?? Array.Empty<FeatureDefinition>();
            }
            else if (features.ValueKind == JsonValueKind.Object)
            {
                MergeMissing(featureOptions, ReadDictionary(features));
            }
            else if (features.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                throw new JsonException("features must be an array or an object.");
            }
        }

        var material = ReadString(root, "material") ?? string.Empty;
        var materials = ReadStringArray(root, "materials");
        if (string.IsNullOrWhiteSpace(material))
        {
            material = materials.FirstOrDefault() ?? string.Empty;
        }

        return new CADModelSpec(
            ReadString(root, "model_id") ?? ReadString(root, "id") ?? string.Empty,
            modelType ?? legacyPartType ?? string.Empty,
            ReadString(root, "unit") ?? "mm",
            parameters,
            ReadDictionary(root, "reference_geometry"),
            ReadArray<SketchDefinition>(root, "sketches", options),
            featureDefinitions,
            material,
            ReadStringArray(root, "output_requirements"),
            ReadDictionary(root, "drawing_requirements"),
            ReadDictionary(root, "execution_options"),
            featureOptions,
            ReadString(root, "title"),
            ReadString(root, "description"),
            materials,
            ReadStringArray(root, "constraints"));
    }

    public override void Write(
        Utf8JsonWriter writer,
        CADModelSpec value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("model_id", value.ModelId);
        writer.WriteString("model_type", value.ModelType);
        writer.WriteString("unit", value.Unit);
        WriteValue(writer, "parameters", value.Parameters, options);
        WriteValue(writer, "reference_geometry", value.ReferenceGeometry, options);
        WriteValue(writer, "sketches", value.Sketches, options);
        WriteValue(writer, "features", value.Features, options);
        writer.WriteString("material", value.Material);
        WriteValue(writer, "output_requirements", value.OutputRequirements, options);
        WriteValue(writer, "drawing_requirements", value.DrawingRequirements, options);
        WriteValue(writer, "execution_options", value.ExecutionOptions, options);
        if (value.FeatureOptions.Count > 0)
        {
            WriteValue(writer, "feature_options", value.FeatureOptions, options);
        }

        if (!string.IsNullOrWhiteSpace(value.Title))
        {
            writer.WriteString("title", value.Title);
        }

        if (!string.IsNullOrWhiteSpace(value.Description))
        {
            writer.WriteString("description", value.Description);
        }

        if (value.Constraints.Count > 0)
        {
            WriteValue(writer, "constraints", value.Constraints, options);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue<T>(
        Utf8JsonWriter writer,
        string name,
        T value,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(name);
        JsonSerializer.Serialize(writer, value, options);
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Dictionary<string, string> ReadDictionary(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object
            ? ReadDictionary(value)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadDictionary(JsonElement value)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value.GetRawText();
        }

        return result;
    }

    private static IReadOnlyList<T> ReadArray<T>(
        JsonElement root,
        string name,
        JsonSerializerOptions options) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<T[]>(value.GetRawText(), options) ?? Array.Empty<T>()
            : Array.Empty<T>();

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray()
            : Array.Empty<string>();

    private static void MergeMissing(
        Dictionary<string, string> target,
        IReadOnlyDictionary<string, string> source)
    {
        foreach (var item in source)
        {
            target.TryAdd(item.Key, item.Value);
        }
    }
}
