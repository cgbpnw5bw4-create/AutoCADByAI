using AgentContracts;
using DomainSchemas;
using PlatformCore;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftAgentAdapter : IAgent
{
    private readonly IAgent? _platformAgent;
    private readonly RuntimeAgentManifest _manifest;
    private readonly AgentRuntimeMode _runtimeMode;
    private readonly InMemoryAuditLog? _auditLog;
    private readonly IMicrosoftRuntimeAgentInvoker? _microsoftInvoker;

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

    private AgentOutput MicrosoftRuntimeFallbackOutput() =>
        new(
            AgentOutputStatus.Failed,
            $"MicrosoftRuntime adapter for '{Id}' is configured but no Microsoft runtime invoker is available.",
            Array.Empty<ArtifactInfo>(),
            new[] { "Microsoft Agent Framework package is isolated in AgentRuntime.Microsoft; real invoker is not configured." },
            new[] { "Fallback path returned without leaking Microsoft Agent Framework types." },
            null);
}
