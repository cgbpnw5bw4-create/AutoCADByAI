using System.Text.Json.Serialization;

namespace DomainSchemas;

/// <summary>工程思考的待校验输出；CAD 描述直接复用现有合同，不携带执行权限。</summary>
public sealed record EngineeringPlan(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("requirement")] string Requirement,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("cad_model_spec")] CADModelSpec? CadModelSpec,
    [property: JsonPropertyName("assumptions")] IReadOnlyList<string> Assumptions,
    [property: JsonPropertyName("missing_parameters")] IReadOnlyList<string> MissingParameters,
    [property: JsonPropertyName("risks")] IReadOnlyList<string> Risks);

public sealed record EngineeringPlanValidationResult(
    bool IsValid,
    CADModelSpec? ModelSpec,
    SolidWorksBuildPlan? BuildPlan,
    IReadOnlyList<string> Issues);
