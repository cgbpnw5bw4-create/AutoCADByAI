using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public sealed class RealSolidWorksWorker : ISolidWorksWorker
{
    private readonly ISolidWorksSessionManager _sessionManager;
    private readonly SolidWorksRuntimeOptions? _options;

    public RealSolidWorksWorker()
        : this(null, null)
    {
    }

    public RealSolidWorksWorker(
        ISolidWorksSessionManager? sessionManager = null,
        SolidWorksRuntimeOptions? options = null)
    {
        _sessionManager = sessionManager ?? new SolidWorksSessionManager();
        _options = options;
    }

    public string Name => nameof(RealSolidWorksWorker);

    public string TargetSystem => "SolidWorks";

    public bool SupportsRealBuild => false;

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
            "RealSolidWorksWorker V1.0-A executed preflight boundary checks.",
            "RealBuild is not implemented in V1.0-A.",
            "No CAD modeling command was executed."
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
        try
        {
            await _sessionManager.DisconnectAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            logs.Add($"solidworks_disconnect_warning: {ex.Message}");
        }

        var connectedPreflight = preflight with
        {
            SolidWorksApplicationConnectable = connection.Connected,
            SolidWorksVersion = connection.SolidWorksVersion,
            FinalStatus = connection.Connected ? "Passed" : "Failed",
            Issues = issues
        };

        return Result(
            request,
            connection.Connected ? "Completed" : "Failed",
            "RealConnectionSmokeTest",
            logs,
            issues,
            realCadConnected: connection.Connected,
            connectedPreflight);
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
}
