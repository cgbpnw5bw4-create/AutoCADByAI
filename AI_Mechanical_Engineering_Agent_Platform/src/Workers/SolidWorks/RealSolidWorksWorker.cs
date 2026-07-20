using DomainSchemas;
using WorkerContracts;
using System.Reflection;

namespace SolidWorksWorker;

public sealed class RealSolidWorksWorker : ISolidWorksWorker
{
    private readonly ISolidWorksSessionManager _sessionManager;
    private readonly ISolidWorksPlateBuilder _plateBuilder;
    private readonly ISolidWorksDrawingBuilder _drawingBuilder;
    private readonly ISolidWorksDrawingDimensionBuilder _drawingDimensionBuilder;
    private readonly ISolidWorksDrawingTitleBlockBuilder _drawingTitleBlockBuilder;
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
    {
        _sessionManager = sessionManager ?? new SolidWorksSessionManager();
        _plateBuilder = plateBuilder ?? new LateBoundSolidWorksPlateBuilder();
        _drawingBuilder = drawingBuilder ?? new LateBoundSolidWorksDrawingBuilder();
        _drawingDimensionBuilder = drawingDimensionBuilder ?? new LateBoundSolidWorksDrawingDimensionBuilder();
        _drawingTitleBlockBuilder = drawingTitleBlockBuilder ?? new LateBoundSolidWorksDrawingTitleBlockBuilder();
        _options = options;
    }

    public string Name => nameof(RealSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public bool SupportsGenericRealBuild => false;

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
            "RealSolidWorksWorker V1.0-B executed preflight boundary checks.",
            "RealBuild generic mode is not implemented.",
            "V1.0-B only supports RealBuildPlateBasic4Holes under explicit safety switches."
        };

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

        if (!request.ConnectionSmokeTestOnly &&
            !request.DrawingSmokeTestOnly &&
            !request.DrawingDimensionSmokeTestOnly &&
            !request.DrawingTitleBlockSmokeTestOnly &&
            !string.Equals(request.BuildPlan.PartType, "plate_basic_4holes", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("COM connection was not attempted because the real build plan is unsupported.");
            issues.Add($"part_family_builder_missing: {request.BuildPlan.PartType} has no independently smoke-tested real SolidWorks builder; only plate_basic_4holes is enabled.");
            issues.Add("unsupported_real_build_plan: RealSolidWorksWorker only enables the independently validated plate_basic_4holes real path.");
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
                "preflight_failed");
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
                    "preflight_failed");
        }

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
                        "solidworks_connection_failed");
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

            SolidWorksPlateBuildResult buildResult;
            try
            {
                buildResult = await ExecuteWithControlledDocumentCleanupAsync(
                    _sessionManager,
                    request,
                    options,
                    logs,
                    (application, token) => _plateBuilder.BuildPlateBasicFourHolesAsync(
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
                    SolidWorksPlateBuildOutput.ExecutionMode,
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
                    SolidWorksPlateBuildOutput.ExecutionMode,
                    logs,
                    issues,
                    realCadConnected: true,
                    connectedPreflight);
            }

            logs.AddRange(buildResult.Logs);
            issues.AddRange(buildResult.Issues);

            return new SolidWorksWorkerResult(
                request.RequestId,
                buildResult.Status,
                buildResult.GeneratedArtifacts,
                logs,
                issues,
                SolidWorksPlateBuildOutput.ExecutionMode,
                RealCadExecuted: buildResult.RealCadExecuted,
                RealCadConnected: true,
                PreflightReport: connectedPreflight with { Issues = issues });
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
        request.DryRun ||
        !request.AllowRealCadExecution ||
        !options.EnableRealExecution;

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
        string failedStage)
    {
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
            PreflightReport: preflight with { Issues = allIssues });
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
            request.DrawingSmokeTestOnly ? "plate_basic_4holes.SLDDRW" : null,
            request.DrawingDimensionSmokeTestOnly ? "plate_basic_4holes_dimensioned.SLDDRW" : null,
            request.DrawingTitleBlockSmokeTestOnly ? "plate_basic_4holes_title_block.SLDDRW" : null,
            !request.DrawingSmokeTestOnly && !request.DrawingDimensionSmokeTestOnly && !request.DrawingTitleBlockSmokeTestOnly
                ? "plate_basic_4holes.SLDPRT"
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
