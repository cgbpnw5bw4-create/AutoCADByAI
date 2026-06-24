using AgentContracts;

namespace PlatformCore;

public sealed class PermissionManager
{
    public bool CanExposeToExternalGateway(IAgent agent) =>
        agent.Visibility == AgentVisibility.Public;

    public bool CanInvokeInternally(IAgent agent) =>
        agent.Visibility is AgentVisibility.Public or AgentVisibility.Internal or AgentVisibility.Protected;
}
