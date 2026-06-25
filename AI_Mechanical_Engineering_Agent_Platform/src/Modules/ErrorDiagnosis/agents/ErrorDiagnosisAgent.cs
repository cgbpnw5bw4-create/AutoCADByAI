using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.ErrorDiagnosis.Agents;

public sealed class ErrorDiagnosisAgent : IAgent
{
    public string Id => "error-diagnosis";

    public string Name => "异常诊断工程师";

    public AgentRole Role { get; } = new(
        "error-diagnosis",
        "异常诊断工程师",
        "失败原因分析和修复建议");

    public string Description => "Internal failure analysis agent.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context) =>
        Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Error diagnosis placeholder is available for future rejected or failed workflows.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "No failure diagnosis was needed in this route." },
            null));
}
