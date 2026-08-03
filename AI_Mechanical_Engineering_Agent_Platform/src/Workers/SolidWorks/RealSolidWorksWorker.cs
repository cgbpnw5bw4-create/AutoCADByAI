using DomainSchemas;
using WorkerContracts;
using System.Reflection;
using SolidWorksWorker.Features;

namespace SolidWorksWorker;

public sealed class RealSolidWorksWorker : ISolidWorksWorker
{
    private readonly ISolidWorksSessionManager _sessionManager;
    private readonly ISolidWorksPlateBuilder _plateBuilder;
    private readonly ISolidWorksDrawingBuilder _drawingBuilder;
    private readonly ISolidWorksDrawingDimensionBuilder _drawingDimensionBuilder;
    private readonly ISolidWorksDrawingTitleBlockBuilder _drawingTitleBlockBuilder;
    private readonly PartFamilyBuilderRegistry _partFamilyBuilderRegistry;
    private readonly FeatureHandlerRegistry _featureHandlerRegistry;
    private readonly ISolidWorksExecutionEnvironmentProbe _executionEnvironmentProbe;
    private readonly SolidWorksRuntimeOptions? _options;

    public RealSolidWorksWorker()
        : this(null, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager = null,
        SolidWorksRuntimeOptions? options = null)
        : this(sessionManager, options, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder)
        : this(sessionManager, options, plateBuilder, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder,
        ISolidWorksDrawingBuilder? drawingBuilder)
        : this(sessionManager, options, plateBuilder, drawingBuilder, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder,
        ISolidWorksDrawingBuilder? drawingBuilder,
        ISolidWorksDrawingDimensionBuilder? drawingDimensionBuilder)
        : this(sessionManager, options, plateBuilder, drawingBuilder, drawingDimensionBuilder, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder,
        ISolidWorksDrawingBuilder? drawingBuilder,
        ISolidWorksDrawingDimensionBuilder? drawingDimensionBuilder,
        ISolidWorksDrawingTitleBlockBuilder? drawingTitleBlockBuilder)
        : this(
            sessionManager,
            options,
            plateBuilder,
            drawingBuilder,
            drawingDimensionBuilder,
            drawingTitleBlockBuilder,
            null,
            null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder,
        ISolidWorksDrawingBuilder? drawingBuilder,
        ISolidWorksDrawingDimensionBuilder? drawingDimensionBuilder,
        ISolidWorksDrawingTitleBlockBuilder? drawingTitleBlockBuilder,
        PartFamilyBuilderRegistry? partFamilyBuilderRegistry)
        : this(
            sessionManager,
            options,
            plateBuilder,
            drawingBuilder,
            drawingDimensionBuilder,
            drawingTitleBlockBuilder,
            partFamilyBuilderRegistry,
            null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager,
        SolidWorksRuntimeOptions? options,
        ISolidWorksPlateBuilder? plateBuilder,
        ISolidWorksDrawingBuilder? drawingBuilder,
        ISolidWorksDrawingDimensionBuilder? drawingDimensionBuilder,
        ISolidWorksDrawingTitleBlockBuilder? drawingTitleBlockBuilder,
        PartFamilyBuilderRegistry? partFamilyBuilderRegistry,
        ISolidWorksExecutionEnvironmentProbe? executionEnvironmentProbe,
        FeatureHandlerRegistry? featureHandlerRegistry = null)
    {
        _sessionManager = sessionManager ?? new SolidWorksSessionManager();
        _plateBuilder = plateBuilder ?? new LateBoundSolidWorksPlateBuilder();
        _drawingBuilder = drawingBuilder ?? new LateBoundSolidWorksDrawingBuilder();
        _drawingDimensionBuilder = drawingDimensionBuilder ?? new LateBoundSolidWorksDrawingDimensionBuilder();
        _drawingTitleBlockBuilder = drawingTitleBlockBuilder ?? new LateBoundSolidWorksDrawingTitleBlockBuilder();
        _partFamilyBuilderRegistry = partFamilyBuilderRegistry ?? PartFamilyBuilderRegistry.CreateDefault(_plateBuilder);
        _featureHandlerRegistry = featureHandlerRegistry ?? FeatureHandlerRegistry.CreateDefault();
        _executionEnvironmentProbe = executionEnvironmentProbe ?? new SolidWorksExecutionEnvironmentProbe();
        _options = options;
    }

    public string Name => nameof(RealSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public bool SupportsGenericRealBuild => true;

    public bool SupportsPlateBasicFourHolesBuild => true;

    public bool SupportsBasicViewsDrawing => true;

    public bool SupportsDrawingDimensions => true;

    public bool SupportsDrawingTitleBlock => true;

    public async Task<SolidWorksWorkerResult> ExecuteAsync(
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var options = _options ?? SolidWorksRuntimeOptions.FromEnvironment();
        var preflight = CreatePreflightReport(request, options);
        var issues = preflight.Issues.ToList();
        var logs = new List<string>
        {
            "operation_executed: real_build_request_received",
            "RealSolidWorksWorker executed preflight boundary checks.",
            "Real part-family builds are resolved through PartFamilyBuilderRegistry."
        };

        var isPartFamilyBuild =
            !request.ConnectionSmokeTestOnly &&
            !request.DrawingSmokeTestOnly &&
            !request.DrawingDimensionSmokeTestOnly &&
            !request.DrawingTitleBlockSmokeTestOnly;
        IPartFamilyBuilder? partFamilyBuilder = null;
        var usesFeatureHandlerGraph =
            isPartFamilyBuild &&
            request.BuildPlan.ExecutionStrategy.Equals(
                SolidWorksBuildExecutionStrategies.FeatureHandlerGraph,
                StringComparison.OrdinalIgnoreCase);

        if (ShouldRejectBeforeConnection(request, options))
        {
            logs.Add("COM connection was not attempted because real execution safety switches were not satisfied.");
            return Result(
                request,
                "Rejected",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight);
        }

        if (usesFeatureHandlerGraph)
        {
            var handlerPreflight = _featureHandlerRegistry.ValidateForRealExecution(request.BuildPlan);
            if (!handlerPreflight.IsPassed)
            {
                logs.Add("COM connection was not attempted because the complete FeatureGraph handler/evidence preflight failed.");
                issues.AddRange(handlerPreflight.Issues);
                var genericBuilder = new SolidWorksFeatureGraphPartFamilyBuilder(
                    request.BuildPlan.PartType,
                    _featureHandlerRegistry);
                return RealBuildFailureResult(
                    request,
                    "Rejected",
                    logs,
                    issues,
                    realCadConnected: false,
                    preflight,
                    options,
                    handlerPreflight.FailureStage ?? PartFamilyFailureStages.FeatureApiUnverified,
                    genericBuilder);
            }

            if (RequiresGeometryValidationReport(request.BuildPlan))
            {
                var v20dProfileEvidence = V20DThreeCircleCutEvidencePolicy.ValidateForRealExecution(
                    request.BuildPlan);
                if (!v20dProfileEvidence.IsPassed)
                {
                    logs.Add(
                        "COM connection was not attempted because the V2.0-D exact three-circle cut evidence preflight failed after FeatureHandlerRegistry passed.");
                    issues.AddRange(v20dProfileEvidence.Issues);
                    var genericBuilder = new SolidWorksFeatureGraphPartFamilyBuilder(
                        request.BuildPlan.PartType,
                        _featureHandlerRegistry);
                    return RealBuildFailureResult(
                        request,
                        "Rejected",
                        logs,
                        issues,
                        realCadConnected: false,
                        preflight,
                        options,
                        v20dProfileEvidence.FailureStage ?? PartFamilyFailureStages.FeatureApiUnverified,
                        genericBuilder);
                }

                logs.Add(
                    $"v2_0_d_three_circle_cut_evidence_verified:{V20DThreeCircleCutEvidencePolicy.EvidenceId};" +
                    $"source_revision={V20DThreeCircleCutEvidencePolicy.SourceRevision}");
            }

            partFamilyBuilder = RequiresGeometryValidationReport(request.BuildPlan)
                ? new V20DFeatureGraphPartFamilyBuilder(
                    request.BuildPlan.PartType,
                    _featureHandlerRegistry,
                    request,
                    options)
                : new SolidWorksFeatureGraphPartFamilyBuilder(
                    request.BuildPlan.PartType,
                    _featureHandlerRegistry);
            logs.Add("Real feature execution was resolved through FeatureHandlerRegistry.");
        }
        else if (isPartFamilyBuild &&
                 !_partFamilyBuilderRegistry.TryGetBuilder(request.BuildPlan.PartType, out partFamilyBuilder))
        {
            logs.Add("COM connection was not attempted because no part-family builder is registered.");
            issues.Add($"part_family_builder_missing: {request.BuildPlan.PartType} has no registered real SolidWorks builder.");
            return Result(
                request,
                "Rejected",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight,
                PartFamilyFailureStages.PartFamilyBuilderMissing);
        }

        if (isPartFamilyBuild && partFamilyBuilder is not null && !partFamilyBuilder.SupportsRealExecution)
        {
            logs.Add("COM connection was not attempted because the registered builder lacks sufficient API evidence.");
            issues.Add($"{PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient}: {partFamilyBuilder.ApiEvidence}.");
            return Result(
                request,
                "Rejected",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight,
                PartFamilyFailureStages.PartFamilyApiEvidenceInsufficient);
        }

        var environmentProbe = _executionEnvironmentProbe.Probe();
        if (!environmentProbe.CanAttemptRealExecution)
        {
            issues.AddRange(environmentProbe.Issues);
            logs.Add("COM connection was not attempted because the real execution environment probe failed.");
            var failedPreflight = preflight with
            {
                FinalStatus = "Failed",
                Issues = issues
            };

            return request.ConnectionSmokeTestOnly
                ? Result(
                    request,
                    "Failed",
                    "RealPreflightOnly",
                    logs,
                    issues,
                    realCadConnected: false,
                    failedPreflight,
                    SolidWorksExecutionEnvironmentProbe.FailureStage)
                : RealBuildFailureResult(
                    request,
                    "Failed",
                    logs,
                    issues,
                    realCadConnected: false,
                    failedPreflight,
                    options,
                    SolidWorksExecutionEnvironmentProbe.FailureStage,
                    partFamilyBuilder);
        }

        if (!request.ConnectionSmokeTestOnly &&
            !request.DrawingSmokeTestOnly &&
            !request.DrawingDimensionSmokeTestOnly &&
            !request.DrawingTitleBlockSmokeTestOnly &&
            (string.IsNullOrWhiteSpace(options.TemplatePartPath) || !File.Exists(options.TemplatePartPath)))
        {
            logs.Add("COM connection was not attempted because a valid SolidWorks part template is required for real build.");
            issues.Add("template_part_path_required_for_real_build: SW_TEMPLATE_PART_PATH must point to an existing part template.");
            return RealBuildFailureResult(
                request,
                "Failed",
                logs,
                issues,
                realCadConnected: false,
                preflight,
                options,
                "preflight_failed",
                partFamilyBuilder);
        }

        if (preflight.FinalStatus == "Failed")
        {
            logs.Add("COM connection was not attempted because preflight failed.");
            return request.ConnectionSmokeTestOnly
                ? Result(
                request,
                "Failed",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                    preflight)
                : RealBuildFailureResult(
                    request,
                    "Failed",
                    logs,
                    issues,
                    realCadConnected: false,
                    preflight,
                    options,
                    "preflight_failed",
                    partFamilyBuilder);
        }

        var coordinationLease = await RealSolidWorksExecutionCoordinator.TryAcquireAsync(
            options.ExecutionTimeoutSeconds,
            cancellationToken);
        if (coordinationLease is null)
        {
            issues.Add("real_cad_execution_coordination_timeout: process-wide SolidWorks execution gate timed out.");
            return Result(
                request,
                "Failed",
                partFamilyBuilder?.RealExecutionMode ?? "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight,
                PartFamilyFailureStages.QualityGateRejected);
        }

        using (coordinationLease)
        {

        logs.Add("operation_executed: connection_started");
        var connection = await _sessionManager.ConnectAsync(options, cancellationToken);
        logs.AddRange(connection.Logs);
        issues.AddRange(connection.Issues);
        var connectedPreflight = preflight with
        {
            SolidWorksApplicationConnectable = connection.Connected,
            SolidWorksVersion = connection.SolidWorksVersion,
            FinalStatus = connection.Connected ? "Passed" : "Failed",
            Issues = issues
        };

        try
        {
            if (!connection.Connected)
            {
                return request.ConnectionSmokeTestOnly
                    ? Result(
                    request,
                    "Failed",
                    "RealConnectionSmokeTest",
                    logs,
                    issues,
                    realCadConnected: false,
                        connectedPreflight)
                    : RealBuildFailureResult(
                        request,
                        "Failed",
                        logs,
                        issues,
                        realCadConnected: false,
                        connectedPreflight,
                        options,
                        "solidworks_connection_failed",
                        partFamilyBuilder);
            }

            if (usesFeatureHandlerGraph)
            {
                var runtimeEvidence = _featureHandlerRegistry.ValidateRuntimeForRealExecution(
                    request.BuildPlan,
                    connection.SolidWorksVersion);
                if (!runtimeEvidence.IsPassed)
                {
                    issues.AddRange(runtimeEvidence.Issues);
                    logs.Add(
                        "No modeling API was invoked because the connected SolidWorks version " +
                        "does not match the per-handler evidence.");
                    return RealBuildFailureResult(
                        request,
                        "Rejected",
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight with
                        {
                            FinalStatus = "Failed",
                            Issues = issues
                        },
                        options,
                        runtimeEvidence.FailureStage ??
                        PartFamilyFailureStages.FeatureApiUnverified,
                        partFamilyBuilder);
                }
            }

            logs.Add("operation_executed: connection_success");
            if (request.ConnectionSmokeTestOnly)
            {
                logs.Add("Connection smoke test requested; no CAD modeling command was executed.");
                return Result(
                    request,
                    "Completed",
                    "RealConnectionSmokeTest",
                    logs,
                    issues,
                    realCadConnected: true,
                    connectedPreflight);
            }

            if (request.DrawingSmokeTestOnly)
            {
                SolidWorksDrawingBuildResult drawingResult;
                try
                {
                    drawingResult = await ExecuteWithControlledDocumentCleanupAsync(
                        _sessionManager,
                        request,
                        options,
                        logs,
                        (application, token) => _drawingBuilder.CreateBasicViewsDrawingAsync(
                            application,
                            request,
                            options,
                            connection.SolidWorksVersion,
                            token),
                        cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("solidworks_application_missing:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }
                catch (TimeoutException ex) when (ex.Message.StartsWith("solidworks_execution_timeout:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }

                logs.AddRange(drawingResult.Logs);
                issues.AddRange(drawingResult.Issues);

                return new SolidWorksWorkerResult(
                    request.RequestId,
                    drawingResult.Status,
                    drawingResult.GeneratedArtifacts,
                    logs,
                    issues,
                    SolidWorksDrawingBuildOutput.ExecutionMode,
                    RealCadExecuted: drawingResult.RealCadExecuted,
                    RealCadConnected: true,
                    PreflightReport: connectedPreflight with { Issues = issues });
            }

            if (request.DrawingDimensionSmokeTestOnly)
            {
                SolidWorksDrawingDimensionBuildResult dimensionResult;
                try
                {
                    dimensionResult = await ExecuteWithControlledDocumentCleanupAsync(
                        _sessionManager,
                        request,
                        options,
                        logs,
                        (application, token) => _drawingDimensionBuilder.CreateDimensionedDrawingAsync(
                            application,
                            request,
                            options,
                            connection.SolidWorksVersion,
                            token),
                        cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("solidworks_application_missing:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingDimensionBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }
                catch (TimeoutException ex) when (ex.Message.StartsWith("solidworks_execution_timeout:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingDimensionBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }

                logs.AddRange(dimensionResult.Logs);
                issues.AddRange(dimensionResult.Issues);

                return new SolidWorksWorkerResult(
                    request.RequestId,
                    dimensionResult.Status,
                    dimensionResult.GeneratedArtifacts,
                    logs,
                    issues,
                    SolidWorksDrawingDimensionBuildOutput.ExecutionMode,
                    RealCadExecuted: dimensionResult.RealCadExecuted,
                    RealCadConnected: true,
                    PreflightReport: connectedPreflight with { Issues = issues });
            }

            if (request.DrawingTitleBlockSmokeTestOnly)
            {
                SolidWorksDrawingTitleBlockBuildResult titleBlockResult;
                try
                {
                    titleBlockResult = await ExecuteWithControlledDocumentCleanupAsync(
                        _sessionManager,
                        request,
                        options,
                        logs,
                        (application, token) => _drawingTitleBlockBuilder.ApplyTitleBlockAsync(
                            application,
                            request,
                            options,
                            connection.SolidWorksVersion,
                            token),
                        cancellationToken);
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("solidworks_application_missing:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingTitleBlockBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }
                catch (TimeoutException ex) when (ex.Message.StartsWith("solidworks_execution_timeout:", StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(ex.Message);
                    return Result(
                        request,
                        "Failed",
                        SolidWorksDrawingTitleBlockBuildOutput.ExecutionMode,
                        logs,
                        issues,
                        realCadConnected: true,
                        connectedPreflight);
                }

                logs.AddRange(titleBlockResult.Logs);
                issues.AddRange(titleBlockResult.Issues);

                return new SolidWorksWorkerResult(
                    request.RequestId,
                    titleBlockResult.Status,
                    titleBlockResult.GeneratedArtifacts,
                    logs,
                    issues,
                    SolidWorksDrawingTitleBlockBuildOutput.ExecutionMode,
                    RealCadExecuted: titleBlockResult.RealCadExecuted,
                    RealCadConnected: true,
                    PreflightReport: connectedPreflight with { Issues = issues });
            }

            PartFamilyBuildResult buildResult;
            try
            {
                buildResult = await ExecuteWithControlledDocumentCleanupAsync(
                    _sessionManager,
                    request,
                    options,
                    logs,
                    (application, token) => partFamilyBuilder!.BuildAsync(
                        new PartFamilyBuildContext(
                            application,
                            request,
                            options,
                            connection.SolidWorksVersion),
                        token),
                    cancellationToken);
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("solidworks_application_missing:", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(ex.Message);
                return Result(
                    request,
                    "Failed",
                    partFamilyBuilder!.RealExecutionMode,
                    logs,
                    issues,
                    realCadConnected: true,
                    connectedPreflight);
            }
            catch (TimeoutException ex) when (ex.Message.StartsWith("solidworks_execution_timeout:", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(ex.Message);
                return Result(
                    request,
                    "Failed",
                    partFamilyBuilder!.RealExecutionMode,
                    logs,
                    issues,
                    realCadConnected: true,
                    connectedPreflight);
            }

            logs.AddRange(buildResult.Logs);
            issues.AddRange(buildResult.Issues);

            var generatedArtifacts = buildResult.Artifacts.ToList();
            if (RequiresGeometryValidationReport(request.BuildPlan))
            {
                var geometryReportPath = Path.Combine(
                    SolidWorksPartFamilyBuildOutput.ResolveOutputDirectory(
                        request,
                        options,
                        request.BuildPlan.PartType),
                    "geometry_validation_report.json");
                generatedArtifacts.Add(SolidWorksPartFamilyBuildOutput.Artifact(
                    "geometry-validation-report",
                    "GeometryValidationReport",
                    geometryReportPath,
                    ".json",
                    "V2.0-D SolidWorks geometry validation report."));
            }

            return new SolidWorksWorkerResult(
                request.RequestId,
                buildResult.Status,
                generatedArtifacts,
                logs,
                issues,
                buildResult.ExecutionMode,
                RealCadExecuted: buildResult.RealCadExecuted,
                RealCadConnected: true,
                PreflightReport: connectedPreflight with { Issues = issues },
                FailureStage: buildResult.FailureStage);
        }
        finally
        {
            try
            {
                await DisconnectWithTimeoutAsync(_sessionManager, options, logs);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
            {
                logs.Add($"solidworks_disconnect_warning: {ex.Message}");
            }
        }
        }
    }

    public Task<WorkerOutput> ExecuteAsync(WorkerInput input) =>
        ExecuteAsync(input, CancellationToken.None);

    public async Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (input.Payload is not SolidWorksWorkerRequest request)
        {
            return new WorkerOutput(
                WorkerOutputStatus.Rejected,
                Array.Empty<ArtifactInfo>(),
                "RealSolidWorksWorker requires SolidWorksWorkerRequest payload.",
                new[] { "payload must be SolidWorksWorkerRequest." });
        }

        var result = await ExecuteAsync(request, cancellationToken);

        return new WorkerOutput(
            result.Status == "Completed" ? WorkerOutputStatus.Completed :
            result.Status == "Failed" ? WorkerOutputStatus.Failed :
            WorkerOutputStatus.Rejected,
            Array.Empty<ArtifactInfo>(),
            string.Join(Environment.NewLine, result.Logs),
            result.Issues);
    }

    private static bool ShouldRejectBeforeConnection(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options) =>
        !options.ShouldUseRealWorker(request.DryRun);

    private static bool RequiresGeometryValidationReport(SolidWorksBuildPlan plan) =>
        plan.OutputRequirements?.Contains(
            "geometry_validation_report.json",
            StringComparer.OrdinalIgnoreCase) == true;

    private static SolidWorksWorkerResult Result(
        SolidWorksWorkerRequest request,
        string status,
        string executionMode,
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        bool realCadConnected,
        SolidWorksPreflightReport preflight,
        string? failureStage = null) =>
        new(
            request.RequestId,
            status,
            Array.Empty<SolidWorksArtifact>(),
            logs,
            issues,
            executionMode,
            RealCadExecuted: false,
            RealCadConnected: realCadConnected,
            PreflightReport: preflight,
            FailureStage: failureStage);

    private static SolidWorksWorkerResult RealBuildFailureResult(
        SolidWorksWorkerRequest request,
        string status,
        IReadOnlyList<string> logs,
        IReadOnlyList<string> issues,
        bool realCadConnected,
        SolidWorksPreflightReport preflight,
        SolidWorksRuntimeOptions options,
        string failedStage,
        IPartFamilyBuilder? partFamilyBuilder = null)
    {
        if (partFamilyBuilder is not null)
        {
            var familyOutputDirectory = SolidWorksPartFamilyBuildOutput.ResolveOutputDirectory(
                request,
                options,
                partFamilyBuilder.PartType);
            var familyReportPath = Path.Combine(familyOutputDirectory, "build_report.json");
            var familyDiagnostics = new SolidWorksPartFamilyBuildDiagnostics
            {
                FailureStage = failedStage,
                RealCadExecuted = false
            };
            familyDiagnostics.OperationsExecuted.Add("real_build_request_received");
            familyDiagnostics.OperationsExecuted.Add("safety_flags_checked");
            familyDiagnostics.OperationsExecuted.Add("preflight_started");
            familyDiagnostics.OperationsExecuted.Add(failedStage);
            familyDiagnostics.Issues.AddRange(issues);
            var familyIssues = issues.ToList();
            var familyArtifacts = Array.Empty<SolidWorksArtifact>();
            try
            {
                SolidWorksPartFamilyBuildReportWriter.Write(
                    familyReportPath,
                    new PartFamilyBuildContext(
                        new object(),
                        request,
                        options,
                        preflight.SolidWorksVersion,
                        RealCadConnected: realCadConnected),
                    partFamilyBuilder,
                    familyOutputDirectory,
                    familyDiagnostics,
                    "Failed");
                familyArtifacts =
                [
                    SolidWorksPartFamilyBuildOutput.Artifact(
                        "real-build-report", "BuildReport", familyReportPath, ".json", "Real part-family preflight failure report.")
                ];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                familyIssues.Add($"build_report_write_failed: {ex.Message}");
            }

            return new SolidWorksWorkerResult(
                request.RequestId,
                status,
                familyArtifacts,
                logs,
                familyIssues,
                partFamilyBuilder.RealExecutionMode,
                RealCadExecuted: false,
                RealCadConnected: realCadConnected,
                PreflightReport: preflight with { Issues = familyIssues },
                FailureStage: failedStage);
        }

        var outputDirectory = SolidWorksPlateBuildOutput.ResolveOutputDirectory(request, options);
        var reportPath = Path.Combine(outputDirectory, "build_report.json");
        var diagnostics = new SolidWorksPlateBuildDiagnostics();
        diagnostics.OperationsExecuted.Add("real_build_request_received");
        diagnostics.OperationsExecuted.Add("safety_flags_checked");
        diagnostics.OperationsExecuted.Add("preflight_started");
        diagnostics.OperationsExecuted.Add(failedStage);
        diagnostics.Issues.AddRange(issues);

        var generatedArtifacts = Array.Empty<SolidWorksArtifact>();
        var allIssues = issues.ToList();
        try
        {
            SolidWorksPlateBuildReportWriter.Write(
                reportPath,
                request,
                SolidWorksPlateBuildOutput.ExecutionMode,
                realCadExecuted: false,
                realCadConnected,
                preflight.SolidWorksVersion,
                outputDirectory,
                Array.Empty<string>(),
                diagnostics,
                "Failed");
            generatedArtifacts = new[]
            {
                SolidWorksPlateBuildOutput.Artifact("real-build-report", "BuildReport", reportPath, ".json", "Real build failure report.")
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            allIssues.Add($"build_report_write_failed: {ex.Message}");
        }

        return new SolidWorksWorkerResult(
            request.RequestId,
            status,
            generatedArtifacts,
            logs,
            allIssues,
            SolidWorksPlateBuildOutput.ExecutionMode,
            RealCadExecuted: false,
            RealCadConnected: realCadConnected,
            PreflightReport: preflight with { Issues = allIssues },
            FailureStage: failedStage);
    }

    private static SolidWorksPreflightReport CreatePreflightReport(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options) =>
        SolidWorksPreflightEvaluator.Evaluate(request, options);

    // Run cleanup within the same controlled application callback as the worker.
    // A second callback would change timeout/connection semantics and would make
    // test doubles appear to execute CAD twice.
    private static async Task<T> ExecuteWithControlledDocumentCleanupAsync<T>(
        ISolidWorksSessionManager sessionManager,
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options,
        List<string> logs,
        Func<object, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var result = await sessionManager.ExecuteWithApplicationAsync(
            async (application, token) =>
            {
                try
                {
                    return await action(application, token);
                }
                finally
                {
                    CloseWorkflowDocuments(application, request, logs);
                }
            },
            cancellationToken,
            options.ExecutionTimeoutSeconds);

        // Give SolidWorks a short opportunity to release the closed file handles
        // before the next workflow stage copies validated artifacts.
        await Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
        return result;
    }

    // ISldWorks.CloseDoc(string) is the documented API for releasing a named document after save/export.
    // Closing only the controlled workflow document names preserves a visible user-owned SOLIDWORKS session.
    private static void CloseWorkflowDocuments(
        object application,
        SolidWorksWorkerRequest request,
        List<string> logs)
    {
        var names = new[]
        {
            Path.GetFileName(request.SourcePartPath),
            Path.GetFileName(request.SourceDrawingPath),
            Path.GetFileName(request.SourceDimensionedDrawingPath),
            request.DrawingSmokeTestOnly ? $"{request.BuildPlan.PartType}.SLDDRW" : null,
            request.DrawingDimensionSmokeTestOnly ? $"{request.BuildPlan.PartType}_dimensioned.SLDDRW" : null,
            request.DrawingTitleBlockSmokeTestOnly ? $"{request.BuildPlan.PartType}_title_block.SLDDRW" : null,
            !request.DrawingSmokeTestOnly && !request.DrawingDimensionSmokeTestOnly && !request.DrawingTitleBlockSmokeTestOnly
                ? $"{request.BuildPlan.PartType}.SLDPRT"
                : null
        }
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Cast<string>()
        .ToArray();
        if (names.Length == 0)
        {
            return;
        }

        foreach (var name in names)
        {
            try
            {
                application.GetType().InvokeMember(
                    "CloseDoc",
                    BindingFlags.InvokeMethod,
                    binder: null,
                    target: application,
                    args: new object[] { name });
                logs.Add($"solidworks_document_closed: {name}");
            }
            catch (Exception ex) when (ex is MissingMethodException or TargetInvocationException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                logs.Add($"solidworks_document_close_warning: {name}: {ex.GetBaseException().Message}");
            }
        }
    }

    private static async Task DisconnectWithTimeoutAsync(
        ISolidWorksSessionManager sessionManager,
        SolidWorksRuntimeOptions options,
        List<string> logs)
    {
        var timeoutSeconds = Math.Max(
            SolidWorksRuntimeOptions.MinimumConnectTimeoutSeconds,
            options.ConnectTimeoutSeconds);
        using var disconnectCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

        Task? disconnectTask = null;
        try
        {
            disconnectTask = sessionManager.DisconnectAsync(disconnectCts.Token);
            await disconnectTask.WaitAsync(disconnectCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (disconnectTask is not null)
            {
                _ = disconnectTask.ContinueWith(
                    static task => _ = task.Exception,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }

            logs.Add("solidworks_disconnect_timeout: COM release timed out.");
        }
    }
}
