using DomainSchemas;
using System.Text.Json.Serialization;

namespace PlatformCore;

public sealed record ModuleSummary(string Name, string Version, string Description);

public sealed record AgentSummary(string Id, string Name, string Role, string Visibility);

public sealed record SkillSummary(string Name, string Description);

public sealed record WorkerSummary(string Name, string TargetSystem);

public sealed record ModuleStructureCheck(
    string ModuleName,
    bool Passed,
    IReadOnlyList<string> MissingEntries);

public sealed record PlatformSelfCheckReport(
    [property: JsonPropertyName("schema_version")] string SchemaVersion,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("source_revision")] string SourceRevision,
    IReadOnlyList<ModuleSummary> RegisteredModules,
    IReadOnlyList<AgentSummary> RegisteredAgents,
    IReadOnlyList<AgentSummary> PublicAgents,
    IReadOnlyList<AgentSummary> InternalAgents,
    IReadOnlyList<SkillSummary> RegisteredSkills,
    IReadOnlyList<WorkerSummary> RegisteredWorkers,
    IReadOnlyList<WorkflowStepResult> WorkflowSteps,
    GateDecision GateDecision,
    IReadOnlyList<AgentDirectoryEntry> GatewayVisibleAgents,
    IReadOnlyList<AuditLogEntry> AuditLogs,
    bool SolutionExists,
    IReadOnlyList<ModuleStructureCheck> ModuleStructureChecks,
    string ModuleManifestSource,
    IReadOnlyList<string> YamlLoadedModules,
    IReadOnlyList<string> FallbackModules,
    bool GatewayQualityGateEnabled,
    bool StorageContractsRegistered,
    bool RejectReportBuilderCheck,
    bool InternalRoutingEnabled,
    IReadOnlyList<string> InternalAgentsInvoked,
    bool CollaborationReportCreated,
    bool GatewayBlocksInternalAgents,
    bool QualityGateAfterCollaboration,
    bool AuditInternalAgentCalls,
    bool AgentRuntimeProjectExists,
    bool MicrosoftRuntimeDependencyIsolated,
    string RuntimeMode,
    bool MockRuntimeAgentCreation,
    bool MicrosoftAgentAdapterCheck,
    bool MicrosoftWorkflowRuntimeCheck,
    bool RuntimeTypesDoNotLeakToContracts,
    bool GatewayVisibilityStillValid,
    bool QualityGateStillEnabled,
    bool WorkflowQualityLoopEnabled,
    string WorkflowPassedScenario,
    string WorkflowRejectedRetryPassedScenario,
    string WorkflowRejectedMaxRetriesScenario,
    string WorkflowHumanApprovalScenario,
    bool RetryPolicyEnabled,
    bool FailureReportGenerated,
    bool HumanApprovalRequestGenerated,
    bool ModuleAgentsRegistered,
    bool PlaceholderAgentIsFallbackOnly,
    bool ChiefEngineerInternalOrchestrationUsesWorkflowEngine,
    bool InternalAgentWorkflowStepsCreated,
    bool QualityGateAfterEachInternalStep,
    string InternalWorkflowPassedScenario,
    string InternalWorkflowRetryThenPassedScenario,
    string InternalWorkflowMaxRetriesExceededScenario,
    string InternalWorkflowFailedScenario,
    string InternalWorkflowHumanApprovalScenario,
    bool RetryPolicyInterfaceEnabled,
    bool ExponentialBackoffPolicyAvailable,
    bool CodeEngineerAgentRegistered,
    bool CodeReviewerAgentRegistered,
    bool CodeAgentsAreInternal,
    bool GatewayBlocksCodeAgents,
    bool RealRuntimeInvokerImplemented,
    bool RuntimeConfigEnvSupported,
    bool RuntimeModeDefaultIsMock,
    bool RuntimeFallbackWhenMissingKey,
    bool ChiefEngineerRealRuntimeOnly,
    bool InternalAgentsRemainMock,
    bool MicrosoftAgentOutputMapperEnabled,
    bool InvalidModelOutputFallbackEnabled,
    bool ModelCannotEscalatePermissions,
    bool ModelCannotCallWorkerDirectly,
    bool ChiefEngineerRuntimeThenWorkflowEngine,
    bool QualityGateAfterRealRuntime,
    bool GatewayResponseContainsRuntimeMetadata,
    bool MicrosoftRuntimeSmokeTestAttempted,
    bool MicrosoftRuntimeSmokeTestPassed,
    string? MicrosoftRuntimeSmokeTestError,
    bool RetryDelayActuallyAwaited,
    bool ExponentialBackoffDelayRespected,
    bool WorkflowRetryDelayCancellationSupported,
    bool RuntimeTimeoutConfigSupported,
    bool InvalidTimeoutFallsBackToDefault,
    [property: JsonPropertyName("openai_client_timeout_configured")] bool OpenAIClientTimeoutConfigured,
    [property: JsonPropertyName("openai_client_cancellation_supported")] bool OpenAIClientCancellationSupported,
    bool ProviderErrorsAreStructured,
    bool ApiKeyNotLogged,
    bool MarkdownChineseStandardExists,
    int MarkdownFilesScanned,
    bool MarkdownChineseValidatorEnabled,
    bool MarkdownChineseCheckPassed,
    bool MarkdownLanguageReportGenerated,
    bool MarkdownEnglishExceptionsSupported,
    [property: JsonPropertyName("solidworks_module_skeleton_enabled")] bool SolidWorksModuleSkeletonEnabled,
    [property: JsonPropertyName("solidworks_build_plan_skill_registered")] bool SolidWorksBuildPlanSkillRegistered,
    [property: JsonPropertyName("solidworks_build_plan_generated")] bool SolidWorksBuildPlanGenerated,
    [property: JsonPropertyName("solidworks_worker_contract_exists")] bool SolidWorksWorkerContractExists,
    [property: JsonPropertyName("fake_solidworks_worker_registered")] bool FakeSolidWorksWorkerRegistered,
    [property: JsonPropertyName("fake_solidworks_worker_dry_run_passed")] bool FakeSolidWorksWorkerDryRunPassed,
    [property: JsonPropertyName("solidworks_build_plan_validator_passed")] bool SolidWorksBuildPlanValidatorPassed,
    [property: JsonPropertyName("solidworks_artifact_validator_passed")] bool SolidWorksArtifactValidatorPassed,
    [property: JsonPropertyName("solidworks_build_plan_reviewer_passed")] bool SolidWorksBuildPlanReviewerPassed,
    [property: JsonPropertyName("solidworks_quality_gate_passed")] bool SolidWorksQualityGatePassed,
    [property: JsonPropertyName("solidworks_fake_artifacts_generated")] bool SolidWorksFakeArtifactsGenerated,
    [property: JsonPropertyName("solidworks_real_cad_not_executed")] bool SolidWorksRealCadNotExecuted,
    [property: JsonPropertyName("solidworks_agent_does_not_call_worker_directly")] bool SolidWorksAgentDoesNotCallWorkerDirectly,
    [property: JsonPropertyName("gateway_does_not_call_solidworks_worker")] bool GatewayDoesNotCallSolidWorksWorker,
    [property: JsonPropertyName("solidworks_self_check_error")] string? SolidWorksSelfCheckError,
    [property: JsonPropertyName("solidworks_real_worker_skeleton_exists")] bool SolidWorksRealWorkerSkeletonExists,
    [property: JsonPropertyName("solidworks_environment_validator_exists")] bool SolidWorksEnvironmentValidatorExists,
    [property: JsonPropertyName("solidworks_preflight_report_generated")] bool SolidWorksPreflightReportGenerated,
    [property: JsonPropertyName("solidworks_session_manager_exists")] bool SolidWorksSessionManagerExists,
    [property: JsonPropertyName("solidworks_real_execution_default_disabled")] bool SolidWorksRealExecutionDefaultDisabled,
    [property: JsonPropertyName("solidworks_real_execution_requires_request_flag")] bool SolidWorksRealExecutionRequiresRequestFlag,
    [property: JsonPropertyName("solidworks_real_execution_requires_env_flag")] bool SolidWorksRealExecutionRequiresEnvFlag,
    [property: JsonPropertyName("solidworks_com_not_called_in_default_self_check")] bool SolidWorksComNotCalledInDefaultSelfCheck,
    [property: JsonPropertyName("solidworks_real_connection_smoke_test_attempted")] bool SolidWorksRealConnectionSmokeTestAttempted,
    [property: JsonPropertyName("solidworks_real_connection_smoke_test_passed")] bool SolidWorksRealConnectionSmokeTestPassed,
    [property: JsonPropertyName("solidworks_real_connection_smoke_test_error")] string? SolidWorksRealConnectionSmokeTestError,
    [property: JsonPropertyName("solidworks_generic_real_build_not_implemented")] bool SolidWorksGenericRealBuildNotImplemented,
    [property: JsonPropertyName("solidworks_real_cad_not_executed_by_default")] bool SolidWorksRealCadNotExecutedByDefault,
    [property: JsonPropertyName("solidworks_real_plate_build_implemented")] bool SolidWorksRealPlateBuildImplemented,
    [property: JsonPropertyName("solidworks_real_build_requires_env_flag")] bool SolidWorksRealBuildRequiresEnvFlag,
    [property: JsonPropertyName("solidworks_real_build_requires_request_flag")] bool SolidWorksRealBuildRequiresRequestFlag,
    [property: JsonPropertyName("solidworks_real_build_requires_dry_run_false")] bool SolidWorksRealBuildRequiresDryRunFalse,
    [property: JsonPropertyName("solidworks_real_build_default_disabled")] bool SolidWorksRealBuildDefaultDisabled,
    [property: JsonPropertyName("solidworks_real_build_smoke_test_attempted")] bool SolidWorksRealBuildSmokeTestAttempted,
    [property: JsonPropertyName("solidworks_real_build_smoke_test_passed")] bool SolidWorksRealBuildSmokeTestPassed,
    [property: JsonPropertyName("solidworks_real_build_smoke_test_error")] string? SolidWorksRealBuildSmokeTestError,
    [property: JsonPropertyName("solidworks_real_build_artifacts_validated")] bool SolidWorksRealBuildArtifactsValidated,
    [property: JsonPropertyName("solidworks_real_build_report_generated")] bool SolidWorksRealBuildReportGenerated,
    [property: JsonPropertyName("solidworks_real_build_outputs_sldprt")] bool SolidWorksRealBuildOutputsSldprt,
    [property: JsonPropertyName("solidworks_real_build_outputs_step")] bool SolidWorksRealBuildOutputsStep,
    [property: JsonPropertyName("solidworks_real_build_outputs_json_report")] bool SolidWorksRealBuildOutputsJsonReport,
    [property: JsonPropertyName("solidworks_real_build_not_called_in_default_self_check")] bool SolidWorksRealBuildNotCalledInDefaultSelfCheck,
    [property: JsonPropertyName("sw_real_build_smoke_test_env_value")] string? SwRealBuildSmokeTestEnvValue,
    [property: JsonPropertyName("sw_strict_real_build_test_env_value")] string? SwStrictRealBuildTestEnvValue,
    [property: JsonPropertyName("real_build_request_dry_run")] bool? RealBuildRequestDryRun,
    [property: JsonPropertyName("real_build_execution_mode")] string? RealBuildExecutionMode,
    [property: JsonPropertyName("real_build_output_directory")] string? RealBuildOutputDirectory,
    [property: JsonPropertyName("real_build_latest_report_path")] string? RealBuildLatestReportPath,
    [property: JsonPropertyName("solidworks_diagnostic_runner_exists")] bool SolidWorksDiagnosticRunnerExists,
    [property: JsonPropertyName("solidworks_diagnostic_runner_not_called_by_default")] bool SolidWorksDiagnosticRunnerNotCalledByDefault,
    [property: JsonPropertyName("solidworks_latest_diagnostic_report_path")] string? SolidWorksLatestDiagnosticReportPath,
    [property: JsonPropertyName("solidworks_latest_diagnostic_final_status")] string? SolidWorksLatestDiagnosticFinalStatus,
    [property: JsonPropertyName("solidworks_real_build_failure_stage")] string? SolidWorksRealBuildFailureStage,
    [property: JsonPropertyName("solidworks_real_build_error_is_actionable")] bool SolidWorksRealBuildErrorIsActionable,
    [property: JsonPropertyName("solidworks_api_failure_analyzer_exists")] bool SolidWorksApiFailureAnalyzerExists,
    [property: JsonPropertyName("solidworks_api_evidence_collector_exists")] bool SolidWorksApiEvidenceCollectorExists,
    [property: JsonPropertyName("solidworks_api_evidence_report_schema_exists")] bool SolidWorksApiEvidenceReportSchemaExists,
    [property: JsonPropertyName("solidworks_cut_holes_api_evidence_supported")] bool SolidWorksCutHolesApiEvidenceSupported,
    [property: JsonPropertyName("solidworks_reference_skill_readonly_analysis_supported")] bool SolidWorksReferenceSkillReadonlyAnalysisSupported,
    [property: JsonPropertyName("solidworks_external_scripts_not_copied")] bool SolidWorksExternalScriptsNotCopied,
    [property: JsonPropertyName("solidworks_api_repair_loop_available")] bool SolidWorksApiRepairLoopAvailable,
    [property: JsonPropertyName("solidworks_macro_recording_request_available")] bool SolidWorksMacroRecordingRequestAvailable,
    [property: JsonPropertyName("solidworks_plate_feature_builder_exists")] bool SolidWorksPlateFeatureBuilderExists,
    [property: JsonPropertyName("solidworks_real_drawing_basic_views_implemented")] bool SolidWorksRealDrawingBasicViewsImplemented,
    [property: JsonPropertyName("solidworks_real_drawing_default_disabled")] bool SolidWorksRealDrawingDefaultDisabled,
    [property: JsonPropertyName("solidworks_real_drawing_requires_env_flag")] bool SolidWorksRealDrawingRequiresEnvFlag,
    [property: JsonPropertyName("solidworks_real_drawing_smoke_test_attempted")] bool SolidWorksRealDrawingSmokeTestAttempted,
    [property: JsonPropertyName("solidworks_real_drawing_smoke_test_passed")] bool SolidWorksRealDrawingSmokeTestPassed,
    [property: JsonPropertyName("solidworks_real_drawing_smoke_test_error")] string? SolidWorksRealDrawingSmokeTestError,
    [property: JsonPropertyName("solidworks_real_drawing_outputs_slddrw")] bool SolidWorksRealDrawingOutputsSlddrw,
    [property: JsonPropertyName("solidworks_real_drawing_outputs_pdf")] bool SolidWorksRealDrawingOutputsPdf,
    [property: JsonPropertyName("solidworks_real_drawing_outputs_json_report")] bool SolidWorksRealDrawingOutputsJsonReport,
    [property: JsonPropertyName("solidworks_real_drawing_not_called_in_default_self_check")] bool SolidWorksRealDrawingNotCalledInDefaultSelfCheck,
    [property: JsonPropertyName("solidworks_drawing_report_generated")] bool SolidWorksDrawingReportGenerated,
    [property: JsonPropertyName("solidworks_real_drawing_failure_stage")] string? SolidWorksRealDrawingFailureStage,
    [property: JsonPropertyName("solidworks_drawing_failure_stage_actionable")] bool SolidWorksDrawingFailureStageActionable,
    [property: JsonPropertyName("real_drawing_output_directory")] string? RealDrawingOutputDirectory,
    [property: JsonPropertyName("real_drawing_latest_report_path")] string? RealDrawingLatestReportPath,
    [property: JsonPropertyName("v1_1_version_stage_documented")] bool V11VersionStageDocumented,
    [property: JsonPropertyName("solidworks_drawing_failure_repair_documented")] bool SolidWorksDrawingFailureRepairDocumented,
    [property: JsonPropertyName("solidworks_drawing_api_evidence_documented")] bool SolidWorksDrawingApiEvidenceDocumented,
    [property: JsonPropertyName("solidworks_drawing_review_checklist_updated")] bool SolidWorksDrawingReviewChecklistUpdated,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_implemented")] bool SolidWorksRealDrawingDimensionsImplemented,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_default_disabled")] bool SolidWorksRealDrawingDimensionsDefaultDisabled,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_requires_env_flag")] bool SolidWorksRealDrawingDimensionsRequiresEnvFlag,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_smoke_test_attempted")] bool SolidWorksRealDrawingDimensionsSmokeTestAttempted,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_smoke_test_passed")] bool SolidWorksRealDrawingDimensionsSmokeTestPassed,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_smoke_test_error")] string? SolidWorksRealDrawingDimensionsSmokeTestError,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_outputs_slddrw")] bool SolidWorksRealDrawingDimensionsOutputsSlddrw,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_outputs_pdf")] bool SolidWorksRealDrawingDimensionsOutputsPdf,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_outputs_json_report")] bool SolidWorksRealDrawingDimensionsOutputsJsonReport,
    [property: JsonPropertyName("solidworks_real_drawing_dimensions_not_called_in_default_self_check")] bool SolidWorksRealDrawingDimensionsNotCalledInDefaultSelfCheck,
    [property: JsonPropertyName("solidworks_drawing_dimension_report_generated")] bool SolidWorksDrawingDimensionReportGenerated,
    [property: JsonPropertyName("solidworks_real_drawing_dimension_failure_stage")] string? SolidWorksRealDrawingDimensionFailureStage,
    [property: JsonPropertyName("solidworks_drawing_dimension_failure_stage_actionable")] bool SolidWorksDrawingDimensionFailureStageActionable,
    [property: JsonPropertyName("real_drawing_dimension_output_directory")] string? RealDrawingDimensionOutputDirectory,
    [property: JsonPropertyName("real_drawing_dimension_latest_report_path")] string? RealDrawingDimensionLatestReportPath,
    [property: JsonPropertyName("v1_2_version_stage_documented")] bool V12VersionStageDocumented,
    [property: JsonPropertyName("solidworks_drawing_dimension_failure_repair_documented")] bool SolidWorksDrawingDimensionFailureRepairDocumented,
    [property: JsonPropertyName("solidworks_drawing_dimension_api_evidence_documented")] bool SolidWorksDrawingDimensionApiEvidenceDocumented,
    [property: JsonPropertyName("solidworks_drawing_dimension_review_checklist_updated")] bool SolidWorksDrawingDimensionReviewChecklistUpdated,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_implemented")] bool SolidWorksRealDrawingTitleBlockImplemented,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_default_disabled")] bool SolidWorksRealDrawingTitleBlockDefaultDisabled,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_requires_env_flag")] bool SolidWorksRealDrawingTitleBlockRequiresEnvFlag,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_smoke_test_attempted")] bool SolidWorksRealDrawingTitleBlockSmokeTestAttempted,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_smoke_test_passed")] bool SolidWorksRealDrawingTitleBlockSmokeTestPassed,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_smoke_test_error")] string? SolidWorksRealDrawingTitleBlockSmokeTestError,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_outputs_slddrw")] bool SolidWorksRealDrawingTitleBlockOutputsSlddrw,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_outputs_pdf")] bool SolidWorksRealDrawingTitleBlockOutputsPdf,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_outputs_json_report")] bool SolidWorksRealDrawingTitleBlockOutputsJsonReport,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_not_called_in_default_self_check")] bool SolidWorksRealDrawingTitleBlockNotCalledInDefaultSelfCheck,
    [property: JsonPropertyName("solidworks_drawing_title_block_report_generated")] bool SolidWorksDrawingTitleBlockReportGenerated,
    [property: JsonPropertyName("solidworks_real_drawing_title_block_failure_stage")] string? SolidWorksRealDrawingTitleBlockFailureStage,
    [property: JsonPropertyName("solidworks_drawing_title_block_failure_stage_actionable")] bool SolidWorksDrawingTitleBlockFailureStageActionable,
    [property: JsonPropertyName("real_drawing_title_block_output_directory")] string? RealDrawingTitleBlockOutputDirectory,
    [property: JsonPropertyName("real_drawing_title_block_latest_report_path")] string? RealDrawingTitleBlockLatestReportPath,
    [property: JsonPropertyName("v1_3_version_stage_documented")] bool V13VersionStageDocumented,
    [property: JsonPropertyName("solidworks_drawing_title_block_failure_repair_documented")] bool SolidWorksDrawingTitleBlockFailureRepairDocumented,
    [property: JsonPropertyName("solidworks_drawing_title_block_api_evidence_documented")] bool SolidWorksDrawingTitleBlockApiEvidenceDocumented,
    [property: JsonPropertyName("solidworks_drawing_title_block_review_checklist_updated")] bool SolidWorksDrawingTitleBlockReviewChecklistUpdated,
    [property: JsonPropertyName("solidworks_drawing_title_block_population_strategy")] string SolidWorksDrawingTitleBlockPopulationStrategy,
    [property: JsonPropertyName("solidworks_drawing_title_block_fields_verified_in_sheet_format")] bool SolidWorksDrawingTitleBlockFieldsVerifiedInSheetFormat,
    [property: JsonPropertyName("solidworks_release_package_implemented")] bool SolidWorksReleasePackageImplemented,
    [property: JsonPropertyName("solidworks_release_package_default_no_cad_execution")] bool SolidWorksReleasePackageDefaultNoCadExecution,
    [property: JsonPropertyName("solidworks_release_manifest_generated")] bool SolidWorksReleaseManifestGenerated,
    [property: JsonPropertyName("solidworks_package_quality_report_generated")] bool SolidWorksPackageQualityReportGenerated,
    [property: JsonPropertyName("solidworks_release_summary_generated")] bool SolidWorksReleaseSummaryGenerated,
    [property: JsonPropertyName("solidworks_release_artifacts_collected")] bool SolidWorksReleaseArtifactsCollected,
    [property: JsonPropertyName("solidworks_release_reports_collected")] bool SolidWorksReleaseReportsCollected,
    [property: JsonPropertyName("solidworks_release_package_failure_stage")] string? SolidWorksReleasePackageFailureStage,
    [property: JsonPropertyName("solidworks_release_package_failure_stage_actionable")] bool SolidWorksReleasePackageFailureStageActionable,
    [property: JsonPropertyName("solidworks_release_manifest_path")] string? SolidWorksReleaseManifestPath,
    [property: JsonPropertyName("solidworks_package_quality_report_path")] string? SolidWorksPackageQualityReportPath,
    [property: JsonPropertyName("solidworks_release_summary_path")] string? SolidWorksReleaseSummaryPath,
    [property: JsonPropertyName("v1_4_version_stage_documented")] bool V14VersionStageDocumented,
    [property: JsonPropertyName("solidworks_release_package_failure_repair_documented")] bool SolidWorksReleasePackageFailureRepairDocumented,
    [property: JsonPropertyName("solidworks_release_package_review_checklist_updated")] bool SolidWorksReleasePackageReviewChecklistUpdated,
    [property: JsonPropertyName("executable_docs_layer_enabled")] bool ExecutableDocsLayerEnabled,
    [property: JsonPropertyName("docs_index_exists")] bool DocsIndexExists,
    [property: JsonPropertyName("project_execution_standard_exists")] bool ProjectExecutionStandardExists,
    [property: JsonPropertyName("module_document_standard_exists")] bool ModuleDocumentStandardExists,
    [property: JsonPropertyName("step_execution_standard_exists")] bool StepExecutionStandardExists,
    [property: JsonPropertyName("failure_repair_standard_exists")] bool FailureRepairStandardExists,
    [property: JsonPropertyName("codex_execution_protocol_exists")] bool CodexExecutionProtocolExists,
    [property: JsonPropertyName("claude_review_protocol_exists")] bool ClaudeReviewProtocolExists,
    [property: JsonPropertyName("version_stage_index_exists")] bool VersionStageIndexExists,
    [property: JsonPropertyName("codex_agent_team_guide_exists")] bool CodexAgentTeamGuideExists,
    [property: JsonPropertyName("codex_agent_registry_exists")] bool CodexAgentRegistryExists,
    [property: JsonPropertyName("codex_agent_governance_doc_exists")] bool CodexAgentGovernanceDocExists,
    [property: JsonPropertyName("agents_md_exists")] bool AgentsMdExists,
    [property: JsonPropertyName("codex_agents_configured")] bool CodexAgentsConfigured,
    [property: JsonPropertyName("codex_agent_registry_lists_canonical_agents")] bool CodexAgentRegistryListsCanonicalAgents,
    [property: JsonPropertyName("codex_no_duplicate_active_agents")] bool CodexNoDuplicateActiveAgents,
    [property: JsonPropertyName("codex_agent_reuse_policy_documented")] bool CodexAgentReusePolicyDocumented,
    [property: JsonPropertyName("codex_agent_new_requirements_go_to_skills_or_docs")] bool CodexAgentNewRequirementsGoToSkillsOrDocs,
    [property: JsonPropertyName("codex_active_agent_count_is_expected")] bool CodexActiveAgentCountIsExpected,
    [property: JsonPropertyName("codex_only_canonical_agents_active")] bool CodexOnlyCanonicalAgentsActive,
    [property: JsonPropertyName("codex_config_example_exists")] bool CodexConfigExampleExists,
    [property: JsonPropertyName("codex_project_manager_agent_exists")] bool CodexProjectManagerAgentExists,
    [property: JsonPropertyName("codex_code_mapper_agent_exists")] bool CodexCodeMapperAgentExists,
    [property: JsonPropertyName("codex_api_researcher_agent_exists")] bool CodexApiResearcherAgentExists,
    [property: JsonPropertyName("codex_cad_worker_agent_exists")] bool CodexCadWorkerAgentExists,
    [property: JsonPropertyName("codex_quality_gate_agent_exists")] bool CodexQualityGateAgentExists,
    [property: JsonPropertyName("codex_docs_writer_agent_exists")] bool CodexDocsWriterAgentExists,
    [property: JsonPropertyName("codex_agents_do_not_replace_project_modules")] bool CodexAgentsDoNotReplaceProjectModules,
    [property: JsonPropertyName("codex_agents_respect_worker_boundaries")] bool CodexAgentsRespectWorkerBoundaries,
    [property: JsonPropertyName("agents_skills_directory_exists")] bool AgentsSkillsDirectoryExists,
    [property: JsonPropertyName("solidworks_api_repair_skill_exists")] bool SolidWorksApiRepairSkillExists,
    [property: JsonPropertyName("markdown_docs_standard_skill_exists")] bool MarkdownDocsStandardSkillExists,
    [property: JsonPropertyName("quality_review_skill_exists")] bool QualityReviewSkillExists,
    [property: JsonPropertyName("cadmodeling_execution_doc_exists")] bool CadModelingExecutionDocExists,
    [property: JsonPropertyName("cadmodeling_failure_repair_doc_exists")] bool CadModelingFailureRepairDocExists,
    [property: JsonPropertyName("cadmodeling_api_evidence_doc_exists")] bool CadModelingApiEvidenceDocExists,
    [property: JsonPropertyName("cadmodeling_review_checklist_exists")] bool CadModelingReviewChecklistExists,
    [property: JsonPropertyName("solidworks_worker_execution_doc_exists")] bool SolidWorksWorkerExecutionDocExists,
    [property: JsonPropertyName("solidworks_worker_failure_repair_doc_exists")] bool SolidWorksWorkerFailureRepairDocExists,
    [property: JsonPropertyName("solidworks_worker_api_evidence_doc_exists")] bool SolidWorksWorkerApiEvidenceDocExists,
    [property: JsonPropertyName("solidworks_worker_review_checklist_exists")] bool SolidWorksWorkerReviewChecklistExists,
    [property: JsonPropertyName("real_cad_worker_integrated_into_main_workflow")] bool RealCadWorkerIntegratedIntoMainWorkflow,
    [property: JsonPropertyName("chief_engineer_orchestrator_invokes_cad_workflow")] bool ChiefEngineerOrchestratorInvokesCadWorkflow,
    [property: JsonPropertyName("workflow_engine_can_route_to_solidworks_worker")] bool WorkflowEngineCanRouteToSolidWorksWorker,
    [property: JsonPropertyName("real_cad_main_workflow_default_disabled")] bool RealCadMainWorkflowDefaultDisabled,
    [property: JsonPropertyName("real_cad_main_workflow_requires_request_flag")] bool RealCadMainWorkflowRequiresRequestFlag,
    [property: JsonPropertyName("real_cad_main_workflow_requires_env_flag")] bool RealCadMainWorkflowRequiresEnvFlag,
    [property: JsonPropertyName("real_cad_main_workflow_passes_quality_gate")] bool RealCadMainWorkflowPassesQualityGate,
    [property: JsonPropertyName("gateway_does_not_call_worker_directly")] bool GatewayDoesNotCallWorkerDirectly,
    [property: JsonPropertyName("llm_does_not_call_worker_directly")] bool LlmDoesNotCallWorkerDirectly,
    [property: JsonPropertyName("release_package_all_source_reports_passed_field_exists")] bool ReleasePackageAllSourceReportsPassedFieldExists,
    [property: JsonPropertyName("release_package_deliverable_status_field_exists")] bool ReleasePackageDeliverableStatusFieldExists,
    [property: JsonPropertyName("release_package_failed_source_reports_block_deliverable")] bool ReleasePackageFailedSourceReportsBlockDeliverable,
    [property: JsonPropertyName("v1_5_version_stage_documented")] bool V15VersionStageDocumented,
    [property: JsonPropertyName("solidworks_com_facade_injection_supported")] bool SolidWorksComFacadeInjectionSupported,
    [property: JsonPropertyName("solidworks_real_acceptance_protocol_exists")] bool SolidWorksRealAcceptanceProtocolExists,
    [property: JsonPropertyName("solidworks_latest_real_outputs_report_supported")] bool SolidWorksLatestRealOutputsReportSupported,
    [property: JsonPropertyName("v1_6_test_a_documented")] bool V16TestADocumented,
    [property: JsonPropertyName("real_cad_e2e_cli_entry_exists")] bool RealCadE2eCliEntryExists,
    [property: JsonPropertyName("real_cad_e2e_structured_input_supported")] bool RealCadE2eStructuredInputSupported,
    [property: JsonPropertyName("real_cad_e2e_uses_chief_engineer_orchestrator")] bool RealCadE2eUsesChiefEngineerOrchestrator,
    [property: JsonPropertyName("real_cad_e2e_uses_workflow_engine")] bool RealCadE2eUsesWorkflowEngine,
    [property: JsonPropertyName("real_cad_e2e_uses_solidworks_router")] bool RealCadE2eUsesSolidWorksRouter,
    [property: JsonPropertyName("real_cad_e2e_can_invoke_real_worker")] bool RealCadE2eCanInvokeRealWorker,
    [property: JsonPropertyName("real_cad_e2e_passes_quality_gate")] bool RealCadE2ePassesQualityGate,
    [property: JsonPropertyName("real_cad_e2e_default_disabled")] bool RealCadE2eDefaultDisabled,
    [property: JsonPropertyName("real_cad_e2e_requires_request_confirmation")] bool RealCadE2eRequiresRequestConfirmation,
    [property: JsonPropertyName("real_cad_e2e_requires_env_confirmation")] bool RealCadE2eRequiresEnvConfirmation,
    [property: JsonPropertyName("real_cad_e2e_report_supported")] bool RealCadE2eReportSupported,
    [property: JsonPropertyName("real_cad_e2e_deliverable_semantics_supported")] bool RealCadE2eDeliverableSemanticsSupported,
    [property: JsonPropertyName("v1_7_version_stage_documented")] bool V17VersionStageDocumented,
    [property: JsonPropertyName("real_cad_e2e_local_authorization_profile_supported")] bool RealCadE2eLocalAuthorizationProfileSupported,
    [property: JsonPropertyName("real_cad_e2e_local_authorization_default_disabled")] bool RealCadE2eLocalAuthorizationDefaultDisabled,
    string FinalStatus)
{
    [JsonPropertyName("generic_cad_model_spec_supported")]
    public bool GenericCadModelSpecSupported { get; init; }

    [JsonPropertyName("part_type_registry_exists")]
    public bool PartTypeRegistryExists { get; init; }

    [JsonPropertyName("plate_part_family_registered")]
    public bool PlatePartFamilyRegistered { get; init; }

    [JsonPropertyName("flange_part_family_registered")]
    public bool FlangePartFamilyRegistered { get; init; }

    [JsonPropertyName("shaft_part_family_registered")]
    public bool ShaftPartFamilyRegistered { get; init; }

    [JsonPropertyName("unsupported_part_type_rejected")]
    public bool UnsupportedPartTypeRejected { get; init; }

    [JsonPropertyName("invalid_part_parameters_rejected_before_worker")]
    public bool InvalidPartParametersRejectedBeforeWorker { get; init; }

    [JsonPropertyName("part_family_builders_do_not_use_large_switch")]
    public bool PartFamilyBuildersDoNotUseLargeSwitch { get; init; }

    [JsonPropertyName("plate_regression_passed")]
    public bool PlateRegressionPassed { get; init; }

    [JsonPropertyName("flange_dry_run_passed")]
    public bool FlangeDryRunPassed { get; init; }

    [JsonPropertyName("shaft_dry_run_passed")]
    public bool ShaftDryRunPassed { get; init; }

    [JsonPropertyName("real_cad_part_family_default_disabled")]
    public bool RealCadPartFamilyDefaultDisabled { get; init; }

    [JsonPropertyName("v1_8_version_stage_documented")]
    public bool V18VersionStageDocumented { get; init; }

    [JsonPropertyName("flange_real_builder_implemented")]
    public bool FlangeRealBuilderImplemented { get; init; }

    [JsonPropertyName("shaft_real_builder_implemented")]
    public bool ShaftRealBuilderImplemented { get; init; }

    [JsonPropertyName("flange_real_workflow_supported")]
    public bool FlangeRealWorkflowSupported { get; init; }

    [JsonPropertyName("shaft_real_workflow_supported")]
    public bool ShaftRealWorkflowSupported { get; init; }

    [JsonPropertyName("flange_real_workflow_default_disabled")]
    public bool FlangeRealWorkflowDefaultDisabled { get; init; }

    [JsonPropertyName("shaft_real_workflow_default_disabled")]
    public bool ShaftRealWorkflowDefaultDisabled { get; init; }

    [JsonPropertyName("flange_api_evidence_documented")]
    public bool FlangeApiEvidenceDocumented { get; init; }

    [JsonPropertyName("shaft_api_evidence_documented")]
    public bool ShaftApiEvidenceDocumented { get; init; }

    [JsonPropertyName("flange_artifact_validation_supported")]
    public bool FlangeArtifactValidationSupported { get; init; }

    [JsonPropertyName("shaft_artifact_validation_supported")]
    public bool ShaftArtifactValidationSupported { get; init; }

    [JsonPropertyName("plate_part_family_regression_passed")]
    public bool PlatePartFamilyRegressionPassed { get; init; }

    [JsonPropertyName("no_large_part_type_switch")]
    public bool NoLargePartTypeSwitch { get; init; }

    [JsonPropertyName("all_part_families_use_registry")]
    public bool AllPartFamiliesUseRegistry { get; init; }

    [JsonPropertyName("v1_9_version_stage_documented")]
    public bool V19VersionStageDocumented { get; init; }

    [JsonPropertyName("solidworks_local_interactive_default_enabled")]
    public bool SolidWorksLocalInteractiveDefaultEnabled { get; init; }

    [JsonPropertyName("solidworks_disable_env_supported")]
    public bool SolidWorksDisableEnvSupported { get; init; }

    [JsonPropertyName("solidworks_ci_execution_disabled")]
    public bool SolidWorksCiExecutionDisabled { get; init; }

    [JsonPropertyName("solidworks_unit_test_execution_disabled")]
    public bool SolidWorksUnitTestExecutionDisabled { get; init; }

    [JsonPropertyName("solidworks_dry_run_disables_real_execution")]
    public bool SolidWorksDryRunDisablesRealExecution { get; init; }

    [JsonPropertyName("solidworks_visible_default_true")]
    public bool SolidWorksVisibleDefaultTrue { get; init; }

    [JsonPropertyName("solidworks_execution_environment_probe_supported")]
    public bool SolidWorksExecutionEnvironmentProbeSupported { get; init; }

    [JsonPropertyName("legacy_enable_flag_not_required")]
    public bool LegacyEnableFlagNotRequired { get; init; }

    [JsonPropertyName("legacy_request_confirmation_not_required")]
    public bool LegacyRequestConfirmationNotRequired { get; init; }

    [JsonPropertyName("generic_cad_model_spec_v2_supported")]
    public bool GenericCadModelSpecV2Supported { get; init; }

    [JsonPropertyName("sketch_definition_supported")]
    public bool SketchDefinitionSupported { get; init; }

    [JsonPropertyName("sketch_constraints_supported")]
    public bool SketchConstraintsSupported { get; init; }

    [JsonPropertyName("feature_definition_supported")]
    public bool FeatureDefinitionSupported { get; init; }

    [JsonPropertyName("feature_graph_supported")]
    public bool FeatureGraphSupported { get; init; }

    [JsonPropertyName("feature_graph_cycle_detected")]
    public bool FeatureGraphCycleDetected { get; init; }

    [JsonPropertyName("missing_feature_dependency_rejected")]
    public bool MissingFeatureDependencyRejected { get; init; }

    [JsonPropertyName("build_plan_compiler_supported")]
    public bool BuildPlanCompilerSupported { get; init; }

    [JsonPropertyName("plate_uses_generic_feature_graph")]
    public bool PlateUsesGenericFeatureGraph { get; init; }

    [JsonPropertyName("flange_uses_generic_feature_graph")]
    public bool FlangeUsesGenericFeatureGraph { get; init; }

    [JsonPropertyName("shaft_uses_generic_feature_graph")]
    public bool ShaftUsesGenericFeatureGraph { get; init; }

    [JsonPropertyName("no_part_specific_logic_in_real_worker")]
    public bool NoPartSpecificLogicInRealWorker { get; init; }

    [JsonPropertyName("v2_0_a_documented")]
    public bool V20ADocumented { get; init; }

    [JsonPropertyName("feature_handler_registry_exists")]
    public bool FeatureHandlerRegistryExists { get; init; }

    [JsonPropertyName("no_feature_type_large_switch")]
    public bool NoFeatureTypeLargeSwitch { get; init; }

    [JsonPropertyName("sketch_handler_registered")]
    public bool SketchHandlerRegistered { get; init; }

    [JsonPropertyName("extrude_handler_registered")]
    public bool ExtrudeHandlerRegistered { get; init; }

    [JsonPropertyName("cut_handler_registered")]
    public bool CutHandlerRegistered { get; init; }

    [JsonPropertyName("hole_handler_registered")]
    public bool HoleHandlerRegistered { get; init; }

    [JsonPropertyName("revolve_handler_registered")]
    public bool RevolveHandlerRegistered { get; init; }

    [JsonPropertyName("feature_handler_validation_supported")]
    public bool FeatureHandlerValidationSupported { get; init; }

    [JsonPropertyName("feature_api_evidence_required")]
    public bool FeatureApiEvidenceRequired { get; init; }

    [JsonPropertyName("unverified_api_blocks_real_execution")]
    public bool UnverifiedApiBlocksRealExecution { get; init; }

    [JsonPropertyName("feature_handler_docs_completed")]
    public bool FeatureHandlerDocsCompleted { get; init; }

    [JsonPropertyName("v2_0_b_documented")]
    public bool V20BDocumented { get; init; }

    [JsonPropertyName("feature_adapter_layer_exists")]
    public bool FeatureAdapterLayerExists { get; init; }

    [JsonPropertyName("feature_handler_no_direct_com_access")]
    public bool FeatureHandlerNoDirectComAccess { get; init; }

    [JsonPropertyName("solidworks_feature_adapter_exists")]
    public bool SolidWorksFeatureAdapterExists { get; init; }

    [JsonPropertyName("sketch_real_execution_supported")]
    public bool SketchRealExecutionSupported { get; init; }

    [JsonPropertyName("extrude_real_execution_supported")]
    public bool ExtrudeRealExecutionSupported { get; init; }

    [JsonPropertyName("cut_real_execution_supported")]
    public bool CutRealExecutionSupported { get; init; }

    [JsonPropertyName("hole_real_execution_supported")]
    public bool HoleRealExecutionSupported { get; init; }

    [JsonPropertyName("feature_pipeline_end_to_end_supported")]
    public bool FeaturePipelineEndToEndSupported { get; init; }

    [JsonPropertyName("feature_production_evidence_active")]
    public bool FeatureProductionEvidenceActive { get; init; }

    [JsonPropertyName("feature_result_validation_supported")]
    public bool FeatureResultValidationSupported { get; init; }

    [JsonPropertyName("feature_fake_success_guard_supported")]
    public bool FeatureFakeSuccessGuardSupported { get; init; }

    [JsonPropertyName("v2_0_c_documented")]
    public bool V20CDocumented { get; init; }

    [JsonPropertyName("model_rebuild_pipeline_exists")]
    public bool ModelRebuildPipelineExists { get; init; }

    [JsonPropertyName("parameter_update_supported")]
    public bool ParameterUpdateSupported { get; init; }

    [JsonPropertyName("solidworks_rebuild_supported")]
    public bool SolidWorksRebuildSupported { get; init; }

    [JsonPropertyName("geometry_validator_exists")]
    public bool GeometryValidatorExists { get; init; }

    [JsonPropertyName("bounding_box_validation_supported")]
    public bool BoundingBoxValidationSupported { get; init; }

    [JsonPropertyName("volume_validation_supported")]
    public bool VolumeValidationSupported { get; init; }

    [JsonPropertyName("parameter_geometry_match_supported")]
    public bool ParameterGeometryMatchSupported { get; init; }

    [JsonPropertyName("rebuild_failure_detected")]
    public bool RebuildFailureDetected { get; init; }

    [JsonPropertyName("geometry_report_generated")]
    public bool GeometryReportGenerated { get; init; }

    [JsonPropertyName("v2_0_d_documented")]
    public bool V20DDocumented { get; init; }

    [JsonPropertyName("v2_0_d_production_evidence_active")]
    public bool V20DProductionEvidenceActive { get; init; }

    [JsonPropertyName("v2_0_e_unified_part_family_builders")]
    public bool V20EUnifiedPartFamilyBuilders { get; init; }

    [JsonPropertyName("v2_0_e_controlled_plate_evidence_active")]
    public bool V20EControlledPlateEvidenceActive { get; init; }

    [JsonPropertyName("v2_0_e_step_content_gate_active")]
    public bool V20EStepContentGateActive { get; init; }

    [JsonPropertyName("v2_1_a_real_execution_frozen")]
    public bool V21ARealExecutionFrozen { get; init; }

    [JsonPropertyName("v2_0_e_documented")]
    public bool V20EDocumented { get; init; }

    [JsonPropertyName("v2_0_e_capability_regression_gate_passed")]
    public bool V20ECapabilityRegressionGatePassed { get; init; }

    [JsonPropertyName("v2_0_e_capability_regressions")]
    public IReadOnlyList<string> V20ECapabilityRegressions { get; init; } = Array.Empty<string>();

    [JsonPropertyName("part_family_definition_supported")]
    public bool PartFamilyDefinitionSupported { get; init; }

    [JsonPropertyName("plate_uses_part_family_definition")]
    public bool PlateUsesPartFamilyDefinition { get; init; }

    [JsonPropertyName("flange_uses_part_family_definition")]
    public bool FlangeUsesPartFamilyDefinition { get; init; }

    [JsonPropertyName("shaft_uses_part_family_definition")]
    public bool ShaftUsesPartFamilyDefinition { get; init; }

    [JsonPropertyName("no_part_specific_builder_logic")]
    public bool NoPartSpecificBuilderLogic { get; init; }

    [JsonPropertyName("feature_graph_template_reuse_supported")]
    public bool FeatureGraphTemplateReuseSupported { get; init; }

    [JsonPropertyName("common_feature_templates_exists")]
    public bool CommonFeatureTemplatesExists { get; init; }

    [JsonPropertyName("cad_capability_matrix_exists")]
    public bool CadCapabilityMatrixExists { get; init; }

    [JsonPropertyName("regression_models_supported")]
    public bool RegressionModelsSupported { get; init; }

    [JsonPropertyName("flange_regression_passed")]
    public bool FlangeRegressionPassed { get; init; }

    [JsonPropertyName("shaft_regression_passed")]
    public bool ShaftRegressionPassed { get; init; }

    [JsonPropertyName("fillet_handler_registered")]
    public bool FilletHandlerRegistered { get; init; }

    [JsonPropertyName("chamfer_handler_registered")]
    public bool ChamferHandlerRegistered { get; init; }

    [JsonPropertyName("linear_pattern_handler_registered")]
    public bool LinearPatternHandlerRegistered { get; init; }

    [JsonPropertyName("circular_pattern_handler_registered")]
    public bool CircularPatternHandlerRegistered { get; init; }

    [JsonPropertyName("mirror_handler_registered")]
    public bool MirrorHandlerRegistered { get; init; }

    [JsonPropertyName("complex_feature_registry_supported")]
    public bool ComplexFeatureRegistrySupported { get; init; }

    [JsonPropertyName("unverified_feature_blocks_execution")]
    public bool UnverifiedFeatureBlocksExecution { get; init; }

    [JsonPropertyName("feature_library_documented")]
    public bool FeatureLibraryDocumented { get; init; }

    [JsonPropertyName("feature_regression_tests_passed")]
    public bool FeatureRegressionTestsPassed { get; init; }

    [JsonPropertyName("edge_selection_model_supported")]
    public bool EdgeSelectionModelSupported { get; init; }

    [JsonPropertyName("v2_1_a_documented")]
    public bool V21ADocumented { get; init; }

    [JsonPropertyName("simple_hole_supported")]
    public bool SimpleHoleSupported { get; init; }

    [JsonPropertyName("counterbore_hole_supported")]
    public bool CounterboreHoleSupported { get; init; }

    [JsonPropertyName("countersink_hole_supported")]
    public bool CountersinkHoleSupported { get; init; }

    [JsonPropertyName("tapped_hole_supported")]
    public bool TappedHoleSupported { get; init; }

    [JsonPropertyName("hole_type_validation_supported")]
    public bool HoleTypeValidationSupported { get; init; }

    [JsonPropertyName("hole_geometry_validation_supported")]
    public bool HoleGeometryValidationSupported { get; init; }

    [JsonPropertyName("tapped_hole_semantics_separated_from_simple_cut")]
    public bool TappedHoleSemanticsSeparatedFromSimpleCut { get; init; }

    [JsonPropertyName("hole_api_evidence_required")]
    public bool HoleApiEvidenceRequired { get; init; }

    [JsonPropertyName("unverified_hole_blocks_real_execution")]
    public bool UnverifiedHoleBlocksRealExecution { get; init; }

    [JsonPropertyName("hole_feature_regression_tests_passed")]
    public bool HoleFeatureRegressionTestsPassed { get; init; }

    [JsonPropertyName("v2_1_b_documented")]
    public bool V21BDocumented { get; init; }

    [JsonPropertyName("v2_0_e_geometry_validation_platform_wide")]
    public bool V20EGeometryValidationPlatformWide { get; init; }

    [JsonPropertyName("v2_0_e_final_status")]
    public string V20EFinalStatus { get; init; } = "Failed";

    [JsonPropertyName("workflow_human_approval_resume_supported")]
    public bool HumanApprovalResumeSupported { get; init; }

    [JsonPropertyName("jacket_part_family_registered")]
    public bool JacketPartFamilyRegistered { get; init; }

    [JsonPropertyName("jacket_uses_generic_feature_graph")]
    public bool JacketUsesGenericFeatureGraph { get; init; }

    [JsonPropertyName("jacket_real_builder_implemented")]
    public bool JacketRealBuilderImplemented { get; init; }

    [JsonPropertyName("jacket_dry_run_passed")]
    public bool JacketDryRunPassed { get; init; }

    [JsonPropertyName("jacket_real_workflow_supported")]
    public bool JacketRealWorkflowSupported { get; init; }

    [JsonPropertyName("jacket_api_evidence_documented")]
    public bool JacketApiEvidenceDocumented { get; init; }

    [JsonPropertyName("jacket_production_evidence_active")]
    public bool JacketProductionEvidenceActive { get; init; }

    [JsonPropertyName("v2_1_a_jacket_documented")]
    public bool V21AJacketDocumented { get; init; }
}
