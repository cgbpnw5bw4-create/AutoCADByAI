namespace AgentContracts;

public interface IAgent
{
    string Id { get; }

    string Name { get; }

    AgentRole Role { get; }

    string Description { get; }

    AgentVisibility Visibility { get; }

    Task<AgentOutput> ExecuteAsync(AgentContext context);
}
