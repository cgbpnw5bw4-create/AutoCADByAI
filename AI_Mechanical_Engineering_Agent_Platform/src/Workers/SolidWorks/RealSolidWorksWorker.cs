using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public sealed class RealSolidWorksWorker : ISolidWorksWorker
{
    private readonly ISolidWorksSessionManager _sessionManager;
    private readonly ISolidWorksPlateBuilder _plateBuilder;
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
    {
        _sessionManager = sessionManager ?? new SolidWorksSessionManager();
        _plateBuilder = plateBuilder ?? new LateBoundSolidWorksPlateBuilder();
        _options = options;
    }

    public string Name => nameof(RealSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public bool SupportsGenericRealBuild => false;

    public bool SupportsPlateBasicFourHolesBuild => true;

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
            !string.Equals(request.BuildPlan.PartType, "plate_basic_4holes", StringComparison.OrdinalIgnoreCase))
        {
            logs.Add("COM connection was not attempted because the real build plan is unsupported.");
            issues.Add("unsupported_real_build_plan: RealSolidWorksWorker V1.0-B only supports plate_basic_4holes.");
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
            (string.IsNullOrWhiteSpace(options.TemplatePartPath) || !File.Exists(options.TemplatePartPath)))
        {
            logs.Add("COM connection was not attempted because a valid SolidWorks part template is required for real build.");
            issues.Add("template_part_path_required_for_real_build: SW_TEMPLATE_PART_PATH must point to an existing part template.");
            return Result(
                request,
                "Failed",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight);
        }

        if (preflight.FinalStatus == "Failed")
        {
            logs.Add("COM connection was not attempted because preflight failed.");
            return Result(
                request,
                "Failed",
                "RealPreflightOnly",
                logs,
                issues,
                realCadConnected: false,
                preflight);
        }

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
                return Result(
                    request,
                    "Failed",
                    "RealConnectionSmokeTest",
                    logs,
                    issues,
                    realCadConnected: false,
                    connectedPreflight);
            }

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

            SolidWorksPlateBuildResult buildResult;
            try
            {
                buildResult = await _sessionManager.ExecuteWithApplicationAsync(
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
        SolidWorksPreflightReport preflight) =>
        new(
            request.RequestId,
            status,
            Array.Empty<SolidWorksArtifact>(),
            logs,
            issues,
            executionMode,
            RealCadExecuted: false,
            RealCadConnected: realCadConnected,
            PreflightReport: preflight);

    private static SolidWorksPreflightReport CreatePreflightReport(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions options) =>
        SolidWorksPreflightEvaluator.Evaluate(request, options);

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
