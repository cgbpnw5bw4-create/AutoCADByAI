using AgentContracts;
using DomainSchemas;
using PlatformCore;
using System.Collections.Concurrent;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftAgentAdapter : IAgent, IHumanApprovalAgent
{
    private readonly IAgent? _platformAgent;
    private readonly RuntimeAgentManifest _manifest;
    private readonly AgentRuntimeMode _runtimeMode;
    private readonly InMemoryAuditLog? _auditLog;
    private readonly IMicrosoftRuntimeAgentInvoker? _microsoftInvoker;
    private readonly ConcurrentDictionary<string, AgentOutput> _pendingAdvisories = new(StringComparer.Ordinal);

    public MicrosoftAgentAdapter(IAgent platformAgent, object? microsoftAgentInstance = null)
        : this(
            RuntimeAgentManifest.Create(platformAgent.Id, platformAgent.Visibility),
            AgentRuntimeMode.Mock,
            null,
            null,
            platformAgent)
    {
        MicrosoftAgentInstance = microsoftAgentInstance;
    }

    public MicrosoftAgentAdapter(
        RuntimeAgentManifest manifest,
        AgentRuntimeMode runtimeMode,
        InMemoryAuditLog? auditLog = null,
        IMicrosoftRuntimeAgentInvoker? microsoftInvoker = null)
        : this(manifest, runtimeMode, auditLog, microsoftInvoker, null)
    {
    }

    public MicrosoftAgentAdapter(
        IAgent platformAgent,
        RuntimeAgentManifest manifest,
        AgentRuntimeMode runtimeMode,
        InMemoryAuditLog? auditLog,
        IMicrosoftRuntimeAgentInvoker? microsoftInvoker)
        : this(manifest, runtimeMode, auditLog, microsoftInvoker, platformAgent)
    {
    }

    private MicrosoftAgentAdapter(
        RuntimeAgentManifest manifest,
        AgentRuntimeMode runtimeMode,
        InMemoryAuditLog? auditLog,
        IMicrosoftRuntimeAgentInvoker? microsoftInvoker,
        IAgent? platformAgent)
    {
        _manifest = manifest;
        _runtimeMode = runtimeMode;
        _auditLog = auditLog;
        _microsoftInvoker = microsoftInvoker;
        _platformAgent = platformAgent;
    }

    public object? MicrosoftAgentInstance { get; }

    public string Id => _platformAgent?.Id ?? _manifest.Id;

    public string Name => _platformAgent?.Name ?? _manifest.Name;

    public AgentRole Role => _platformAgent?.Role ?? _manifest.Role;

    public string Description => _platformAgent?.Description ?? _manifest.Description;

    public AgentVisibility Visibility => _platformAgent?.Visibility ?? _manifest.Visibility;

    public async Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        _auditLog?.Record("agent-runtime", Id, "runtime_agent_invoked", $"Runtime mode {_runtimeMode} invoked for {Id}.");

        var output = _runtimeMode switch
        {
            AgentRuntimeMode.Microsoft when _platformAgent is not null && _microsoftInvoker is not null && string.Equals(Id, "chief-engineer", StringComparison.OrdinalIgnoreCase) => await ExecuteChiefEngineerRuntimeThenWorkflowAsync(context),
            AgentRuntimeMode.Mock => await ExecuteMockAsync(context),
            AgentRuntimeMode.Microsoft when _microsoftInvoker is not null => await _microsoftInvoker.InvokeAsync(_manifest, context),
            AgentRuntimeMode.Microsoft => MicrosoftRuntimeFallbackOutput(),
            _ => MicrosoftRuntimeFallbackOutput()
        };

        _auditLog?.Record("agent-runtime", Id, "runtime_agent_completed", $"Runtime mode {_runtimeMode} completed with status {output.Status}.");
        return output;
    }

    private Task<AgentOutput> ExecuteMockAsync(AgentContext context)
    {
        if (_platformAgent is not null)
        {
            return _platformAgent.ExecuteAsync(context);
        }

        return Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            $"MockRuntime Agent '{Id}' completed adapter execution.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "MockRuntime returned deterministic AgentOutput without model or CAD calls." },
            Id == "chief-engineer" ? "mechanical-designer" : null));
    }

    private async Task<AgentOutput> ExecuteChiefEngineerRuntimeThenWorkflowAsync(AgentContext context)
    {
        var runtimeOutput = await _microsoftInvoker!.InvokeAsync(_manifest, context);
        var workflowOutput = await _platformAgent!.ExecuteAsync(context);
        if (workflowOutput.InternalCollaborationReport?.HumanApprovalRequest is not null)
            _pendingAdvisories[context.TaskId] = runtimeOutput;
        return MergeRuntimeOutput(runtimeOutput, workflowOutput);
    }

    public async Task<AgentApprovalResult> ResumeHumanApprovalAsync(AgentContext context, WorkflowApprovalSubmission submission,
        CancellationToken cancellationToken = default)
    {
        if (_platformAgent is not IHumanApprovalAgent resumable)
            return new(false, null, "runtime_approval_resume_unsupported");
        AgentOutput? advisory = null;
        if (_runtimeMode == AgentRuntimeMode.Microsoft && !_pendingAdvisories.TryGetValue(context.TaskId, out advisory))
            return new(false, null, "runtime_approval_advisory_missing");
        var result = await resumable.ResumeHumanApprovalAsync(context, submission, cancellationToken);
        if (!result.Accepted || result.Output is null) return result;
        if (result.Output.InternalCollaborationReport?.HumanApprovalRequest is null)
            _pendingAdvisories.TryRemove(context.TaskId, out _);
        return result with { Output = advisory is null ? result.Output : MergeRuntimeOutput(advisory, result.Output) };
    }

    private AgentOutput MergeRuntimeOutput(AgentOutput runtimeOutput, AgentOutput workflowOutput)
    {
        var metadata = runtimeOutput.RuntimeMetadata ?? new RuntimeMetadata(
            "Microsoft",
            _manifest.Id,
            null,
            true,
            "Runtime output did not include metadata.",
            true);
        var runtimeIssuesForFinal = runtimeOutput.Status == AgentOutputStatus.Failed && metadata.RuntimeFallbackUsed
            ? Array.Empty<string>()
            : runtimeOutput.Issues;

        return new AgentOutput(
            workflowOutput.Status,
            $"{runtimeOutput.Message}\n\n{workflowOutput.Message}",
            workflowOutput.Artifacts,
            workflowOutput.Issues.Concat(runtimeIssuesForFinal).ToArray(),
            runtimeOutput.Logs.Concat(workflowOutput.Logs).Concat(new[] { "Chief engineer runtime advisory was followed by SequentialWorkflowEngine orchestration." }).ToArray(),
            runtimeOutput.NextRecommendedAgentId ?? workflowOutput.NextRecommendedAgentId,
            workflowOutput.InternalCollaborationReport,
            workflowOutput.ReviewReport,
            metadata);
    }

    private AgentOutput MicrosoftRuntimeFallbackOutput() =>
        new(
            AgentOutputStatus.Failed,
            $"MicrosoftRuntime adapter for '{Id}' is configured but no Microsoft runtime invoker is available.",
            Array.Empty<ArtifactInfo>(),
            new[] { "Microsoft Agent Framework package is isolated in AgentRuntime.Microsoft; real invoker is not configured." },
            new[] { "Fallback path returned without leaking Microsoft Agent Framework types." },
            null);
}
