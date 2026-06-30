using System.Text.Json.Serialization;
using SolidWorksWorker;

namespace SolidWorksSmokeRunner;

public sealed class SolidWorksDiagnosticReport
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = $"sw-diag-{Guid.NewGuid():N}";

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("output_directory")]
    public string OutputDirectory { get; set; } = string.Empty;

    [JsonPropertyName("solidworks_connected")]
    public bool SolidWorksConnected { get; set; }

    [JsonPropertyName("solidworks_version")]
    public string? SolidWorksVersion { get; set; }

    [JsonPropertyName("active_doc_title")]
    public string? ActiveDocTitle { get; set; }

    [JsonPropertyName("operations")]
    public List<SolidWorksDiagnosticOperation> Operations { get; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; } = [];

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; } = [];

    [JsonPropertyName("sldprt_path")]
    public string? SldprtPath { get; set; }

    [JsonPropertyName("sldprt_exists")]
    public bool SldprtExists { get; set; }

    [JsonPropertyName("sldprt_size_bytes")]
    public long SldprtSizeBytes { get; set; }

    [JsonPropertyName("step_path")]
    public string? StepPath { get; set; }

    [JsonPropertyName("step_exists")]
    public bool StepExists { get; set; }

    [JsonPropertyName("step_size_bytes")]
    public long StepSizeBytes { get; set; }

    [JsonPropertyName("save_sldprt_attempted")]
    public bool SaveSldprtAttempted { get; set; }

    [JsonPropertyName("save_sldprt_success")]
    public bool SaveSldprtSuccess { get; set; }

    [JsonPropertyName("save_sldprt_errors")]
    public List<string> SaveSldprtErrors { get; } = [];

    [JsonPropertyName("save_sldprt_warnings")]
    public List<string> SaveSldprtWarnings { get; } = [];

    [JsonPropertyName("export_step_attempted")]
    public bool ExportStepAttempted { get; set; }

    [JsonPropertyName("export_step_success")]
    public bool ExportStepSuccess { get; set; }

    [JsonPropertyName("export_step_errors")]
    public List<string> ExportStepErrors { get; } = [];

    [JsonPropertyName("export_step_warnings")]
    public List<string> ExportStepWarnings { get; } = [];

    [JsonPropertyName("active_doc_title_before_step_export")]
    public string? ActiveDocTitleBeforeStepExport { get; set; }

    [JsonPropertyName("active_doc_title_after_activate")]
    public string? ActiveDocTitleAfterActivate { get; set; }

    [JsonPropertyName("plane_selection_attempted")]
    public bool PlaneSelectionAttempted { get; set; }

    [JsonPropertyName("plane_selection_success")]
    public bool PlaneSelectionSuccess { get; set; }

    [JsonPropertyName("selected_plane_name")]
    public string? SelectedPlaneName { get; set; }

    [JsonPropertyName("selected_plane_strategy")]
    public string? SelectedPlaneStrategy { get; set; }

    [JsonPropertyName("plane_selection_errors")]
    public List<string> PlaneSelectionErrors { get; } = [];

    [JsonPropertyName("available_reference_planes")]
    public List<SolidWorksReferencePlaneInfo> AvailableReferencePlanes { get; } = [];

    [JsonPropertyName("api_evidence_report_path")]
    public string? ApiEvidenceReportPath { get; set; }

    [JsonPropertyName("api_repair_attempted")]
    public bool ApiRepairAttempted { get; set; }

    [JsonPropertyName("api_repair_strategy")]
    public string? ApiRepairStrategy { get; set; }

    [JsonPropertyName("repair_failure_reason")]
    public string? RepairFailureReason { get; set; }

    [JsonPropertyName("failure_stage")]
    public string? FailureStage { get; set; }

    [JsonPropertyName("final_status")]
    public string FinalStatus { get; set; } = "Failed";
}

public sealed class SolidWorksDiagnosticOperation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("started_at")]
    public DateTimeOffset StartedAt { get; set; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs { get; set; }
}

public sealed record SolidWorksDiagnosticOptions(
    string OutputRoot,
    string? TemplatePartPath = null,
    bool Visible = true);
