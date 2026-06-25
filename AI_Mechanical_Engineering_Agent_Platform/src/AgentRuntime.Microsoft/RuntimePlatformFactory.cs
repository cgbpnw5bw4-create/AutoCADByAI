using PlatformCore;

namespace AgentRuntime.Microsoft;

public static class RuntimePlatformFactory
{
    public static PlatformKernel CreateDefault(string? projectRoot = null)
    {
        var platform = PlatformBootstrapper.CreateDefault(projectRoot);
        ApplyRuntimeConfiguration(platform, RuntimeConfiguration.FromEnvironment());
        return platform;
    }

    public static void ApplyRuntimeConfiguration(
        PlatformKernel platform,
        RuntimeConfiguration configuration,
        IRuntimeModelClient? modelClient = null)
    {
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer")
            ?? throw new InvalidOperationException("chief-engineer is not registered.");
        var factory = new AgentFactory(platform.AuditLog);
        var runtimeAwareChief = factory.CreateRuntimeAwareAgent(
            chiefEngineer,
            platform.AgentRegistry,
            configuration,
            modelClient);

        platform.AgentRegistry.Register(runtimeAwareChief);
    }
}
