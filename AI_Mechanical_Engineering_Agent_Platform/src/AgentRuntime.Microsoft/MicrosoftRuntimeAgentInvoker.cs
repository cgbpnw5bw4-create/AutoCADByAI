using AgentContracts;
using DomainSchemas;
using PlatformCore;
using System.Text.Json;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftRuntimeAgentInvoker : IMicrosoftRuntimeAgentInvoker
{
    private static readonly string[] DefaultInternalAgentIds =
    [
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer",
        "code-engineer",
        "code-reviewer",
        "error-diagnosis"
    ];

    private readonly RuntimeConfiguration _configuration;
    private readonly IRuntimeModelClient _modelClient;
    private readonly MicrosoftAgentOutputMapper _mapper;
    private readonly InMemoryAuditLog _auditLog;
    private readonly string _systemPrompt;

    public MicrosoftRuntimeAgentInvoker(
        RuntimeConfiguration configuration,
        IRuntimeModelClient modelClient,
        InMemoryAuditLog auditLog,
        IEnumerable<string>? internalAgentIds = null,
        string? systemPrompt = null)
    {
        _configuration = configuration;
        _modelClient = modelClient;
        _auditLog = auditLog;
        _mapper = new MicrosoftAgentOutputMapper(internalAgentIds ?? DefaultInternalAgentIds);
        _systemPrompt = systemPrompt ?? LoadChiefEngineerPrompt();
    }

    public async Task<AgentOutput> InvokeAsync(
        RuntimeAgentManifest manifest,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(manifest.Id, "chief-engineer", StringComparison.OrdinalIgnoreCase))
        {
            return new AgentOutput(
                AgentOutputStatus.Failed,
                "Real Microsoft runtime is only enabled for chief-engineer.",
                Array.Empty<ArtifactInfo>(),
                new[] { $"Real runtime invocation is not allowed for agent '{manifest.Id}'." },
                Array.Empty<string>(),
                null,
                RuntimeMetadata: RuntimeMetadata.MockMicrosoft(_configuration.Provider, _configuration.Model, "Real runtime is restricted to chief-engineer."));
        }

        if (_configuration.EffectiveMode != AgentRuntimeMode.Microsoft)
        {
            _auditLog.Record("agent-runtime", manifest.Id, "runtime_fallback_to_mock", _configuration.FallbackReason ?? "Runtime mode is Mock.");
            return new AgentOutput(
                AgentOutputStatus.Completed,
                "Chief engineer runtime used Mock fallback; platform workflow remains authoritative.",
                Array.Empty<ArtifactInfo>(),
                Array.Empty<string>(),
                new[] { _configuration.FallbackReason ?? "Runtime mode is Mock." },
                "mechanical-designer",
                RuntimeMetadata: RuntimeMetadata.MockMicrosoft(_configuration.Provider, _configuration.Model, _configuration.FallbackReason ?? "Runtime mode is Mock."));
        }

        try
        {
            _auditLog.Record("agent-runtime", manifest.Id, "real_runtime_invoked", $"Provider '{_configuration.Provider}' model '{_configuration.Model}' invoked for chief-engineer.");
            var modelText = await _modelClient.GenerateTextAsync(
                _systemPrompt,
                context.Input.Message,
                _configuration,
                cancellationToken);
            var output = _mapper.Map(
                modelText,
                new RuntimeMetadata(
                    "Microsoft",
                    _configuration.Provider,
                    _configuration.Model,
                    false,
                    null,
                    true));
            _auditLog.Record("agent-runtime", manifest.Id, "real_runtime_completed", "Chief engineer real runtime returned mapped AgentOutput.");
            return output;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException or JsonException)
        {
            _auditLog.Record("agent-runtime", manifest.Id, "real_runtime_failed", ex.GetType().Name);
            return new AgentOutput(
                AgentOutputStatus.Failed,
                "Chief engineer real runtime failed before platform orchestration.",
                Array.Empty<ArtifactInfo>(),
                new[] { $"runtime_error: {ex.GetType().Name}: {ex.Message}" },
                new[] { "Runtime failure was converted to structured AgentOutput without exposing credentials." },
                null,
                RuntimeMetadata: RuntimeMetadata.MockMicrosoft(_configuration.Provider, _configuration.Model, ex.GetType().Name));
        }
    }

    private static string LoadChiefEngineerPrompt()
    {
        var localPath = Path.Combine(AppContext.BaseDirectory, "Prompts", "chief_engineer.system.md");
        if (File.Exists(localPath))
        {
            return File.ReadAllText(localPath);
        }

        return "You are the chief mechanical engineer agent. Understand the task, suggest internal agents, and never call workers or CAD directly.";
    }
}
