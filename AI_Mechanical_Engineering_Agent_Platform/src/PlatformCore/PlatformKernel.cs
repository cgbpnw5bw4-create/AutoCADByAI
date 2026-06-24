namespace PlatformCore;

public sealed class PlatformKernel
{
    public PlatformKernel()
    {
        TaskStore = new TaskStore();
        WorkflowEngine = new SequentialWorkflowEngine();
        AgentRegistry = new AgentRegistry();
        SkillRegistry = new SkillRegistry();
        ModuleRegistry = new ModuleRegistry();
        WorkerRegistry = new WorkerRegistry();
        ContextManager = new ContextManager();
        PermissionManager = new PermissionManager();
        EventBus = new InMemoryEventBus();
        AuditLog = new InMemoryAuditLog();
    }

    public TaskStore TaskStore { get; }

    public SequentialWorkflowEngine WorkflowEngine { get; }

    public AgentRegistry AgentRegistry { get; }

    public SkillRegistry SkillRegistry { get; }

    public ModuleRegistry ModuleRegistry { get; }

    public WorkerRegistry WorkerRegistry { get; }

    public ContextManager ContextManager { get; }

    public PermissionManager PermissionManager { get; }

    public InMemoryEventBus EventBus { get; }

    public InMemoryAuditLog AuditLog { get; }
}
