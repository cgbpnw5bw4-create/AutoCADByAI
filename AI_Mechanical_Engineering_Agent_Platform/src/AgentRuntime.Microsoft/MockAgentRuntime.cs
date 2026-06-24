using AgentContracts;
using PlatformCore;

namespace AgentRuntime.Microsoft;

public sealed class MockAgentRuntime
{
    private static readonly string[] AgentIds =
    [
        "chief-engineer",
        "mechanical-designer",
        "cad-modeler",
        "drawing-engineer",
        "drawing-reviewer",
        "error-diagnosis"
    ];

    private readonly AgentFactory _agentFactory;

    public MockAgentRuntime(InMemoryAuditLog? auditLog = null)
    {
        _agentFactory = new AgentFactory(auditLog ?? new InMemoryAuditLog());
    }

    public IAgent CreateAgent(string agentId) => _agentFactory.CreateMockAgent(agentId);

    public IReadOnlyList<IAgent> CreateDefaultAgents() =>
        AgentIds.Select(CreateAgent).ToArray();
}
