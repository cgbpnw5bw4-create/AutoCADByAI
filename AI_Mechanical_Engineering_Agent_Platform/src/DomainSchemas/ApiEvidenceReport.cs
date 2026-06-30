using System.Text.Json.Serialization;

namespace DomainSchemas;

public sealed record ApiEvidenceReport(
    [property: JsonPropertyName("report_id")] string ReportId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("failure_stage")] string FailureStage,
    [property: JsonPropertyName("target_operation")] string TargetOperation,
    [property: JsonPropertyName("official_api_sources")] IReadOnlyList<ApiEvidenceSource> OfficialApiSources,
    [property: JsonPropertyName("local_reference_sources")] IReadOnlyList<ApiEvidenceSource> LocalReferenceSources,
    [property: JsonPropertyName("third_party_reference_sources")] IReadOnlyList<ApiEvidenceSource> ThirdPartyReferenceSources,
    [property: JsonPropertyName("extracted_api_candidates")] IReadOnlyList<ApiCandidate> ExtractedApiCandidates,
    [property: JsonPropertyName("selected_api_strategy")] string SelectedApiStrategy,
    [property: JsonPropertyName("rejected_strategies")] IReadOnlyList<string> RejectedStrategies,
    [property: JsonPropertyName("risk_notes")] IReadOnlyList<string> RiskNotes,
    [property: JsonPropertyName("implementation_notes")] IReadOnlyList<string> ImplementationNotes,
    [property: JsonPropertyName("final_recommendation")] string FinalRecommendation);

public sealed record ApiEvidenceSource(
    [property: JsonPropertyName("source_type")] string SourceType,
    [property: JsonPropertyName("source_name")] string SourceName,
    [property: JsonPropertyName("source_path_or_url")] string SourcePathOrUrl,
    [property: JsonPropertyName("api_names")] IReadOnlyList<string> ApiNames,
    [property: JsonPropertyName("evidence_summary")] string EvidenceSummary,
    [property: JsonPropertyName("license_note")] string LicenseNote,
    [property: JsonPropertyName("can_reuse_code")] bool CanReuseCode,
    [property: JsonPropertyName("can_reuse_idea")] bool CanReuseIdea);

public sealed record ApiCandidate(
    [property: JsonPropertyName("api_name")] string ApiName,
    [property: JsonPropertyName("api_purpose")] string ApiPurpose,
    [property: JsonPropertyName("call_order")] IReadOnlyList<string> CallOrder,
    [property: JsonPropertyName("required_preconditions")] IReadOnlyList<string> RequiredPreconditions,
    [property: JsonPropertyName("required_selection_state")] string RequiredSelectionState,
    [property: JsonPropertyName("expected_return")] string ExpectedReturn,
    [property: JsonPropertyName("failure_modes")] IReadOnlyList<string> FailureModes,
    [property: JsonPropertyName("verification_method")] string VerificationMethod);
