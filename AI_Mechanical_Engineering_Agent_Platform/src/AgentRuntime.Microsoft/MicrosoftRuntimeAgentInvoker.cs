using AgentContracts;
using DomainSchemas;
using PlatformCore;
using System.Text.Json;
using ModelRuntime;

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
    private readonly ModelRuntime.ModelRuntime _modelRuntime;
    private readonly MicrosoftAgentOutputMapper _mapper;
    private readonly InMemoryAuditLog _auditLog;
    private readonly string _systemPrompt;

    public MicrosoftRuntimeAgentInvoker(
        RuntimeConfiguration configuration,
        IRuntimeModelClient modelClient,
        InMemoryAuditLog auditLog,
        IEnumerable<string>? internalAgentIds = null,
        string? systemPrompt = null)
        : this(configuration, new ModelRuntime.ModelRuntime(new ConfiguredModelProvider(modelClient, configuration)),
            auditLog, internalAgentIds, systemPrompt)
    {
    }

    public MicrosoftRuntimeAgentInvoker(
        RuntimeConfiguration configuration,
        ModelRuntime.ModelRuntime modelRuntime,
        InMemoryAuditLog auditLog,
        IEnumerable<string>? internalAgentIds = null,
        string? systemPrompt = null)
    {
        _configuration = configuration;
        _modelRuntime = modelRuntime;
        _auditLog = auditLog;
        _mapper = new MicrosoftAgentOutputMapper(internalAgentIds ?? DefaultInternalAgentIds);
        _systemPrompt = systemPrompt ?? LoadChiefEngineerPrompt();
    }

    public async Task<AgentOutput> InvokeAsync(
        RuntimeAgentManifest manifest,
        AgentContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
            var modelText = await _modelRuntime.GenerateAsync(
                new ModelRequest(EngineeringModelPurpose.RequirementUnderstanding, _systemPrompt, context.Input.Message),
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RuntimeProviderException ex)
        {
            _auditLog.Record("agent-runtime", manifest.Id, "real_runtime_failed", ex.IssueType);
            return new AgentOutput(
                AgentOutputStatus.Failed,
                "Chief engineer real runtime failed before platform orchestration.",
                Array.Empty<ArtifactInfo>(),
                new[] { $"{ex.IssueType}: {ex.Message}" },
                new[] { "Runtime provider failure was converted to structured AgentOutput without exposing credentials." },
                null,
                RuntimeMetadata: RuntimeMetadata.MockMicrosoft(_configuration.Provider, _configuration.Model, ex.IssueType));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException or JsonException or System.Net.Sockets.SocketException or System.IO.IOException)
        {
            _auditLog.Record("agent-runtime", manifest.Id, "real_runtime_failed", ex.GetType().Name);
            return new AgentOutput(
                AgentOutputStatus.Failed,
                "Chief engineer real runtime failed before platform orchestration.",
                Array.Empty<ArtifactInfo>(),
                new[] { $"runtime_error: {ex.GetType().Name}" },
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

        return "你是 AI Mechanical Engineer Platform 的机械总工程师。负责工程需求理解、设计和特征规划、建模顺序、装配策略、异常恢复建议与工程决策。计划必须经过平台校验和 Worker 执行；不得直接调用 Worker 或 CAD，不得把模型生成的 API 名称或参数视为真实能力证据。";
    }
}
