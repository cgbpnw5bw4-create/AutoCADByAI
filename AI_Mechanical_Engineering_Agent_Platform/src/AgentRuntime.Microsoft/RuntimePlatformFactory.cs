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
        IRuntimeModelClient? modelClient = null,
        ModelRuntime.IModelProvider? modelProvider = null)
    {
        var chiefEngineer = platform.AgentRegistry.GetById("chief-engineer");
        if (chiefEngineer is null)
        {
            platform.AuditLog.Record("agent-runtime", "RuntimePlatformFactory", "chief_engineer_missing", "chief-engineer is not registered.");
            throw new InvalidOperationException("chief-engineer is not registered.");
        }

        var factory = new AgentFactory(platform.AuditLog);
        var runtimeAwareChief = factory.CreateRuntimeAwareAgent(
            chiefEngineer,
            platform.AgentRegistry,
            configuration,
            modelClient,
            modelProvider);

        platform.AgentRegistry.Register(runtimeAwareChief);
        platform.EngineeringPlanningRuntime = configuration.EffectiveMode == AgentRuntimeMode.Microsoft
            ? new ModelRuntime.ModelRuntime(modelProvider ?? new ConfiguredModelProvider(
                modelClient ?? new RuntimeModelClientFactory().Create(configuration), configuration))
            : null;
    }
}
