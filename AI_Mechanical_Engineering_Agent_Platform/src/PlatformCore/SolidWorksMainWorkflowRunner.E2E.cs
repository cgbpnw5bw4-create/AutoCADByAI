using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DomainSchemas;
using QualityGate;

namespace PlatformCore;

public sealed partial class SolidWorksMainWorkflowRunner
{
    private async Task<SolidWorksMainWorkflowResult> ExecuteCompleteDrawingPackageAsync(
        SolidWorksMainWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var releaseDirectory = Path.GetFullPath(request.OutputDirectory);
        var reportsDirectory = Path.Combine(releaseDirectory, "reports");
        Directory.CreateDirectory(reportsDirectory);

        var runtimeOptions = _runtimeOptionsProvider();
        var localAuthorization = SolidWorksLocalExecutionProfile.Load(request.ProjectRoot);
        var confirmationIssues = GetE2eConfirmationIssues(request, runtimeOptions, localAuthorization);
        if (confirmationIssues.Count > 0)
        {
            var failedWorkflow = await _workflowEngine.ExecuteAsync(
                new[]
                {
                    new WorkflowStep(
                        "V1.7 real CAD execution confirmation",
                        _ => Task.FromResult(StepFailed(
                            "solidworks-e2e-execution-confirmation",
                            "V1.7 real CAD workflow was rejected because its required confirmations are incomplete.",
                            confirmationIssues,
                            fatal: true)),
                        "solidworks-e2e-execution-confirmation")
                },
                new WorkflowContext($"solidworks-e2e-{request.TaskId}", new Dictionary<string, object?>
                {
                    ["request_id"] = request.RequestId,
                    ["operation"] = request.Operation.ToString()
                }),
                cancellationToken);

            return await WriteE2eResultAsync(
                request,
                releaseDirectory,
                Array.Empty<(string Stage, SolidWorksMainWorkflowResult Result)>(),
                package: null,
                failedWorkflow,
                new GateDecision($"gate-solidworks-e2e-confirmation-{Guid.NewGuid():N}", GateDecisionResult.Failed, "Required real CAD confirmations are missing."),
                confirmationIssues,
                localAuthorization.IsAuthorized
                    ? "real_execution_confirmation_missing"
                    : "local_execution_authorization_missing",
                cancellationToken);
        }

        var runSegment = ToSafePathSegment(request.RequestId);
        var stageResults = new List<(string Stage, SolidWorksMainWorkflowResult Result)>();

        var build = await ExecuteSingleStageAsync(
            request with
            {
                Operation = SolidWorksMainWorkflowOperation.BuildPlate,
                Stage = SolidWorksMainWorkflowStage.BuildPlate,
                OutputDirectory = Path.Combine(request.ProjectRoot, "output", "solidworks", "real", "plate_basic_4holes", runSegment)
            },
            cancellationToken);
        stageResults.Add(("build", build));

        if (StagePassedForE2e(build))
        {
            var drawing = await ExecuteSingleStageAsync(
                request with
                {
                    Operation = SolidWorksMainWorkflowOperation.BuildPlate,
                    Stage = SolidWorksMainWorkflowStage.CreateDrawing,
                    OutputDirectory = Path.Combine(request.ProjectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing", runSegment),
                    SourcePartPath = FindArtifactPath(build, "plate_basic_4holes.SLDPRT")
                },
                cancellationToken);
            stageResults.Add(("drawing", drawing));

            if (StagePassedForE2e(drawing))
            {
                var dimension = await ExecuteSingleStageAsync(
                    request with
                    {
                        Operation = SolidWorksMainWorkflowOperation.BuildPlate,
                        Stage = SolidWorksMainWorkflowStage.AddDrawingDimensions,
                        OutputDirectory = Path.Combine(request.ProjectRoot, "output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions", runSegment),
                        SourceDrawingPath = FindArtifactPath(drawing, "plate_basic_4holes.SLDDRW")
                    },
                    cancellationToken);
                stageResults.Add(("dimension", dimension));

                if (StagePassedForE2e(dimension))
                {
                    var titleBlock = await ExecuteSingleStageAsync(
                        request with
                        {
                            Operation = SolidWorksMainWorkflowOperation.BuildPlate,
                            Stage = SolidWorksMainWorkflowStage.ApplyDrawingTitleBlock,
                            OutputDirectory = Path.Combine(request.ProjectRoot, "output", "solidworks", "real", "plate_basic_4holes_title_block", runSegment),
                            SourceDimensionedDrawingPath = FindArtifactPath(dimension, "plate_basic_4holes_dimensioned.SLDDRW")
                        },
                        cancellationToken);
                    stageResults.Add(("title_block", titleBlock));
                }
            }
        }

        var sourceSet = new SolidWorksReleasePackageSourceSet(
            FindArtifactPath(stageResults, "build", "plate_basic_4holes.SLDPRT"),
            FindArtifactPath(stageResults, "build", "plate_basic_4holes.STEP"),
            FindArtifactPath(stageResults, "title_block", "plate_basic_4holes_title_block.SLDDRW"),
            FindArtifactPath(stageResults, "title_block", "plate_basic_4holes_title_block.pdf"),
            FindArtifactPath(stageResults, "build", "build_report.json"),
            FindArtifactPath(stageResults, "drawing", "drawing_report.json"),
            FindArtifactPath(stageResults, "dimension", "dimension_report.json"),
            FindArtifactPath(stageResults, "title_block", "title_block_report.json"),
            stageResults.Select(ToExecutionEvidence).ToArray(),
            Warnings: new[] { "V1.7 release uses only paths produced by this request_id; historical latest outputs were not scanned." },
            RequireRealExecutionEvidence: true);

        var package = await BuildE2eReleasePackageAsync(request.ProjectRoot, sourceSet, releaseDirectory, cancellationToken);
        var allStagesPassed = stageResults.Count == 4 && stageResults.All(item => StagePassedForE2e(item.Result));
        var packageOutcome = ReadPackageOutcome(package?.QualityReportPath);
        var finalIssues = stageResults.SelectMany(item => item.Result.Issues)
            .Concat(package?.Issues ?? Array.Empty<string>())
            .Concat(allStagesPassed ? Array.Empty<string>() : new[] { "one_or_more_real_cad_stages_failed" })
            .Concat(packageOutcome.AllSourceReportsPassed ? Array.Empty<string>() : new[] { "all_source_reports_passed=false" })
            .Concat(string.Equals(packageOutcome.DeliverableStatus, "Deliverable", StringComparison.OrdinalIgnoreCase)
                ? Array.Empty<string>()
                : new[] { "deliverable_status=NotDeliverable" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var passed = allStagesPassed &&
            package is { Status: "Completed" } &&
            packageOutcome.AllSourceReportsPassed &&
            string.Equals(packageOutcome.DeliverableStatus, "Deliverable", StringComparison.OrdinalIgnoreCase) &&
            finalIssues.Length == 0;
        var finalReview = new ReviewReport(
            $"solidworks-e2e-review-{Guid.NewGuid():N}",
            "solidworks-e2e-reviewer",
            passed,
            passed ? 0.98 : 0.0,
            finalIssues,
            RequiresHumanApproval: false,
            HasFatalError: !passed);
        var finalGate = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder()).Evaluate(finalReview).Decision;
        _auditLog.Record(
            "quality-gate",
            "solidworks-e2e-workflow",
            "quality_gate_after_complete_drawing_package",
            $"V1.7 end-to-end QualityGate evaluated the run as {finalGate.Result}.");

        var lastWorkflow = stageResults.LastOrDefault().Result?.WorkflowResult ??
            await _workflowEngine.ExecuteAsync(
                Array.Empty<WorkflowStep>(),
                new WorkflowContext($"solidworks-e2e-empty-{request.TaskId}", new Dictionary<string, object?>()),
                cancellationToken);
        var failureStage = passed
            ? null
            : FirstFailureStage(stageResults) ?? packageOutcome.FailureStage ?? package?.FailureStage ?? "quality_gate_failed";

        return await WriteE2eResultAsync(
            request,
            releaseDirectory,
            stageResults,
            package,
            lastWorkflow,
            finalGate,
            finalIssues,
            failureStage,
            cancellationToken,
            packageOutcome);
    }

    private async Task<SolidWorksMainWorkflowResult> WriteE2eResultAsync(
        SolidWorksMainWorkflowRequest request,
        string releaseDirectory,
        IReadOnlyList<(string Stage, SolidWorksMainWorkflowResult Result)> stageResults,
        E2eReleasePackageResult? package,
        WorkflowExecutionResult workflowResult,
        GateDecision finalGate,
        IReadOnlyList<string> issues,
        string? failureStage,
        CancellationToken cancellationToken,
        E2ePackageOutcome? packageOutcome = null)
    {
        var reportsDirectory = Path.Combine(releaseDirectory, "reports");
        Directory.CreateDirectory(reportsDirectory);
        var reportPath = Path.Combine(reportsDirectory, "e2e_execution_report.json");
        var latestOutputsPath = Path.Combine(releaseDirectory, "latest_real_outputs.md");
        var generatedArtifacts = new[]
        {
            Path.Combine(releaseDirectory, "artifacts", "plate_basic_4holes.SLDPRT"),
            Path.Combine(releaseDirectory, "artifacts", "plate_basic_4holes.STEP"),
            Path.Combine(releaseDirectory, "artifacts", "plate_basic_4holes.SLDDRW"),
            Path.Combine(releaseDirectory, "artifacts", "plate_basic_4holes.pdf")
        }.Where(ExistingNonEmpty).ToArray();
        var allSourceReportsPassed = packageOutcome?.AllSourceReportsPassed ?? false;
        var deliverableStatus = packageOutcome?.DeliverableStatus ?? "NotDeliverable";
        var finalStatus = finalGate.Result == GateDecisionResult.Passed &&
            allSourceReportsPassed &&
            string.Equals(deliverableStatus, "Deliverable", StringComparison.OrdinalIgnoreCase)
            ? "Passed"
            : "Failed";
        var sourceReports = stageResults.Select(item => new E2eSourceReport(
            item.Stage,
            FindStageReportPath(item.Stage, item.Result),
            ReadJsonString(FindStageReportPath(item.Stage, item.Result), "final_status"),
            ReadJsonString(FindStageReportPath(item.Stage, item.Result), "failure_stage"),
            item.Result.RealCadExecuted,
            item.Result.RealCadConnected,
            item.Result.ExecutionMode,
            item.Result.QualityGatePassed)).ToArray();
        var workflowSteps = stageResults.SelectMany(item => item.Result.WorkflowResult.Steps.Select(step => new E2eWorkflowStep(
            item.Stage,
            step.StepId,
            step.Status.ToString(),
            step.GateDecision?.Result.ToString(),
            step.Issues))).Append(new E2eWorkflowStep(
                "release",
                "solidworks-e2e-release-package",
                package?.Status ?? "Failed",
                packageOutcome?.FinalStatus,
                package?.Issues ?? Array.Empty<string>()))
            .Append(new E2eWorkflowStep(
                "quality_gate",
                "solidworks-e2e-quality-gate",
                finalGate.Result.ToString(),
                finalGate.Result.ToString(),
                issues)).ToArray();
        var report = new SolidWorksE2eExecutionReport(
            request.RequestId,
            request.StructuredInputReceived,
            request.GatewayInvoked,
            request.ChiefEngineerInvoked,
            stageResults.Count > 0 || workflowResult.Steps.Count > 0,
            workflowSteps,
            SolidWorksLocalExecutionProfile.Load(request.ProjectRoot).IsAuthorized,
            SolidWorksLocalExecutionProfile.Load(request.ProjectRoot).ExecutionAuthorizationSource,
            request.SolidWorksRouterTriggered,
            stageResults.Any(item => item.Result.Logs.Contains("operation_executed: connection_started", StringComparer.OrdinalIgnoreCase)),
            stageResults.Any(item => item.Result.Logs.Contains("operation_executed: real_build_request_received", StringComparer.OrdinalIgnoreCase)),
            stageResults.Count == 4 && stageResults.All(item => item.Result.RealCadConnected),
            stageResults.Count == 4 && stageResults.All(item => item.Result.RealCadExecuted),
            finalGate.Result.ToString(),
            sourceReports,
            allSourceReportsPassed,
            deliverableStatus,
            generatedArtifacts,
            failureStage,
            issues,
            Array.Empty<string>(),
            finalStatus,
            "custom_properties_only",
            false);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, JsonOptions()), cancellationToken);
        await File.WriteAllTextAsync(latestOutputsPath, BuildLatestOutputsMarkdown(report, reportPath), cancellationToken);

        var artifacts = generatedArtifacts.Select(path => ToFileArtifact(path, "SolidWorksArtifact"))
            .Append(ToFileArtifact(reportPath, "E2eExecutionReport"))
            .Append(ToFileArtifact(latestOutputsPath, "LatestRealOutputs"))
            .Concat(new[]
            {
                package?.ManifestPath,
                package?.QualityReportPath,
                package?.SummaryPath
            }.Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)).Select(path => ToFileArtifact(path!, "ReleasePackageReport")))
            .ToArray();
        var resultStatus = finalStatus == "Passed" ? "Completed" : "Failed";
        return new SolidWorksMainWorkflowResult(
            request.RequestId,
            $"solidworks-e2e-{request.TaskId}",
            resultStatus,
            stageResults.Any(item => string.Equals(item.Result.WorkerName, "RealSolidWorksWorker", StringComparison.OrdinalIgnoreCase)) ? "RealSolidWorksWorker" : "NotInvoked",
            stageResults.LastOrDefault().Result?.ExecutionMode ?? "RealNotStarted",
            request.AllowRealCadExecution && !request.DryRun,
            _runtimeOptionsProvider().EnableRealExecution,
            _runtimeOptionsProvider().MainWorkflowExecutionEnabled,
            request.AllowRealCadExecution && !request.DryRun,
            report.RealCadExecuted,
            report.SolidWorksConnected,
            finalGate.Result == GateDecisionResult.Passed && finalStatus == "Passed",
            finalGate.Result.ToString(),
            releaseDirectory,
            artifacts,
            artifacts.Select(artifact => artifact.Path).ToArray(),
            stageResults.SelectMany(item => item.Result.Logs).ToArray(),
            issues,
            failureStage,
            workflowResult);
    }

    private async Task<E2eReleasePackageResult?> BuildE2eReleasePackageAsync(
        string projectRoot,
        SolidWorksReleasePackageSourceSet sources,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            var type = Type.GetType("SolidWorksWorker.SolidWorksE2EReleasePackageBuilder, SolidWorksWorker", throwOnError: false)
                ?? throw new TypeLoadException("SolidWorksE2EReleasePackageBuilder was not found.");
            var builder = Activator.CreateInstance(type)
                ?? throw new InvalidOperationException("Could not create SolidWorksE2EReleasePackageBuilder.");
            var method = type.GetMethod("BuildFromSourcesAsync", new[]
            {
                typeof(string), typeof(SolidWorksReleasePackageSourceSet), typeof(string), typeof(CancellationToken)
            }) ?? throw new MissingMethodException(type.FullName, "BuildFromSourcesAsync");
            var task = method.Invoke(builder, new object?[] { projectRoot, sources, outputDirectory, cancellationToken }) as Task
                ?? throw new InvalidOperationException("BuildFromSourcesAsync did not return Task.");
            await task;
            var result = task.GetType().GetProperty("Result")?.GetValue(task)
                ?? throw new InvalidOperationException("BuildFromSourcesAsync returned no result.");
            return new E2eReleasePackageResult(
                GetStringProperty(result, "Status") ?? "Failed",
                GetStringProperty(result, "ManifestPath"),
                GetStringProperty(result, "QualityReportPath"),
                GetStringProperty(result, "SummaryPath"),
                GetStringProperty(result, "FailureStage"),
                GetStringListProperty(result, "Issues"));
        }
        catch (Exception ex) when (ex is TypeLoadException or MissingMethodException or InvalidOperationException or TargetInvocationException)
        {
            return new E2eReleasePackageResult("Failed", null, null, null, "package_validation_failed", new[] { ex.GetBaseException().Message });
        }
    }

    private static List<string> GetE2eConfirmationIssues(
        SolidWorksMainWorkflowRequest request,
        SolidWorksRuntimeOptions options,
        SolidWorksLocalExecutionProfile localAuthorization)
    {
        var issues = new List<string>();
        if (!localAuthorization.IsAuthorized)
        {
            issues.AddRange(localAuthorization.Issues);
            issues.Add("LocalDevelopmentProfile authorization is required for V1.7 real CAD E2E execution.");
        }
        if (!request.AllowRealCadExecution) issues.Add("allow_real_cad_execution=true is required for V1.7 real CAD E2E execution.");
        if (request.DryRun) issues.Add("dry_run=false is required for V1.7 real CAD E2E execution.");
        if (!options.EnableRealExecution) issues.Add("SW_ENABLE_REAL_EXECUTION=true is required for V1.7 real CAD E2E execution.");
        if (!options.MainWorkflowExecutionEnabled) issues.Add("SW_REAL_MAIN_WORKFLOW_TEST=true is required for V1.7 real CAD E2E execution.");
        if (!request.GenerateDrawing || !request.GenerateDimensions || !request.GenerateTitleBlock || !request.GenerateReleasePackage)
            issues.Add("build_complete_drawing_package requires all generate_* flags to be true.");
        return issues;
    }

    private static bool StagePassedForE2e(SolidWorksMainWorkflowResult result) =>
        result.QualityGatePassed && result.RealCadExecuted && result.RealCadConnected && string.Equals(result.Status, "Completed", StringComparison.OrdinalIgnoreCase);

    private static string? FindArtifactPath(SolidWorksMainWorkflowResult result, string fileName) =>
        result.ArtifactPaths.FirstOrDefault(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase) && ExistingNonEmpty(path));

    private static string? FindArtifactPath(IReadOnlyList<(string Stage, SolidWorksMainWorkflowResult Result)> results, string stage, string fileName) =>
        results.FirstOrDefault(item => string.Equals(item.Stage, stage, StringComparison.OrdinalIgnoreCase)).Result is { } result
            ? FindArtifactPath(result, fileName)
            : null;

    private static SolidWorksReleaseExecutionEvidence ToExecutionEvidence((string Stage, SolidWorksMainWorkflowResult Result) item) =>
        new(item.Stage, item.Result.WorkerName, item.Result.ExecutionMode, item.Result.RealCadExecuted, item.Result.RealCadConnected, item.Result.QualityGatePassed, item.Result.FailureStage, FindStageReportPath(item.Stage, item.Result));

    private static string? FindStageReportPath(string stage, SolidWorksMainWorkflowResult result) =>
        FindArtifactPath(result, stage switch
        {
            "build" => "build_report.json",
            "drawing" => "drawing_report.json",
            "dimension" => "dimension_report.json",
            "title_block" => "title_block_report.json",
            _ => string.Empty
        });

    private static string? FirstFailureStage(IReadOnlyList<(string Stage, SolidWorksMainWorkflowResult Result)> stageResults)
    {
        // The stage report is emitted by the real worker and therefore preserves the
        // actionable CAD failure (for example, a drawing-view API failure).  The
        // enclosing workflow result intentionally uses broader orchestration labels
        // such as worker_execution_failed; prefer the worker's report for repair.
        var reportedFailure = stageResults
            .Select(item => ReadJsonString(FindStageReportPath(item.Stage, item.Result), "failure_stage"))
            .FirstOrDefault(stage => !string.IsNullOrWhiteSpace(stage));

        return reportedFailure ?? stageResults
            .Select(item => item.Result.FailureStage)
            .FirstOrDefault(stage => !string.IsNullOrWhiteSpace(stage));
    }

    private static E2ePackageOutcome ReadPackageOutcome(string? qualityReportPath) =>
        new(
            string.Equals(ReadJsonString(qualityReportPath, "all_source_reports_passed"), "true", StringComparison.OrdinalIgnoreCase),
            ReadJsonString(qualityReportPath, "deliverable_status") ?? "NotDeliverable",
            ReadJsonString(qualityReportPath, "final_status") ?? "Failed",
            ReadJsonString(qualityReportPath, "failure_stage"));

    private static string? ReadJsonString(string? path, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.ToString() : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static ArtifactInfo ToFileArtifact(string path, string type) =>
        new($"solidworks-e2e-{type}-{Guid.NewGuid():N}", type, type, path, InferMediaType(Path.GetExtension(path)), new Dictionary<string, string>
        {
            ["exists"] = File.Exists(path).ToString(),
            ["size_bytes"] = File.Exists(path) ? new FileInfo(path).Length.ToString() : "0"
        });

    private static bool ExistingNonEmpty(string? path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 0;

    private static string ToSafePathSegment(string value) =>
        string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

    private static string BuildLatestOutputsMarkdown(SolidWorksE2eExecutionReport report, string reportPath) =>
        $"# V1.7 最新真实主工作流输出\n\n- request_id：`{report.RequestId}`\n- final_status：`{report.FinalStatus}`\n- all_source_reports_passed：`{report.AllSourceReportsPassed}`\n- deliverable_status：`{report.DeliverableStatus}`\n- e2e_execution_report：`{reportPath}`\n- 标题栏语义：只验证自定义属性写入、读回与刷新；未验证 Sheet Format 可见渲染。\n";

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static string? GetStringProperty(object source, string propertyName) =>
        source.GetType().GetProperty(propertyName)?.GetValue(source)?.ToString();

    private static IReadOnlyList<string> GetStringListProperty(object source, string propertyName) =>
        source.GetType().GetProperty(propertyName)?.GetValue(source) is IEnumerable<string> values ? values.ToArray() : Array.Empty<string>();

    private sealed record E2eReleasePackageResult(string Status, string? ManifestPath, string? QualityReportPath, string? SummaryPath, string? FailureStage, IReadOnlyList<string> Issues);

    private sealed record E2ePackageOutcome(bool AllSourceReportsPassed, string DeliverableStatus, string FinalStatus, string? FailureStage);

    private sealed record E2eSourceReport(string Stage, string? Path, string? FinalStatus, string? FailureStage, bool RealCadExecuted, bool SolidWorksConnected, string ExecutionMode, bool QualityGatePassed);

    private sealed record E2eWorkflowStep(string Stage, string StepId, string Status, string? GateDecision, IReadOnlyList<string> Issues);

    private sealed record SolidWorksE2eExecutionReport(
        string RequestId,
        bool StructuredInputReceived,
        bool GatewayInvoked,
        bool ChiefEngineerInvoked,
        bool WorkflowEngineInvoked,
        IReadOnlyList<E2eWorkflowStep> WorkflowSteps,
        bool RealExecutionAuthorized,
        string ExecutionAuthorizationSource,
        [property: JsonPropertyName("solidworks_router_triggered")] bool SolidWorksRouterTriggered,
        [property: JsonPropertyName("solidworks_launch_attempted")] bool SolidWorksLaunchAttempted,
        bool RealWorkerInvoked,
        [property: JsonPropertyName("solidworks_connected")] bool SolidWorksConnected,
        bool RealCadExecuted,
        string QualityGateDecision,
        IReadOnlyList<E2eSourceReport> SourceReports,
        bool AllSourceReportsPassed,
        string DeliverableStatus,
        IReadOnlyList<string> GeneratedArtifacts,
        string? FailureStage,
        IReadOnlyList<string> Errors,
        IReadOnlyList<string> Warnings,
        string FinalStatus,
        string TitleBlockPopulationStrategy,
        bool TitleBlockFieldsVerifiedInSheetFormat);
}
