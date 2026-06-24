using AgentContracts;
using DomainSchemas;
using PlatformCore.Modules.RequirementUnderstanding.Agents;

namespace PlatformCore;

public sealed class PlaceholderAgent : IAgent
{
    private readonly ChiefEngineerOrchestrator? _chiefEngineerOrchestrator;

    public PlaceholderAgent(
        string id,
        string name,
        AgentRole role,
        string description,
        AgentVisibility visibility,
        string? nextRecommendedAgentId = null,
        ChiefEngineerOrchestrator? chiefEngineerOrchestrator = null)
    {
        Id = id;
        Name = name;
        Role = role;
        Description = description;
        Visibility = visibility;
        NextRecommendedAgentId = nextRecommendedAgentId;
        _chiefEngineerOrchestrator = chiefEngineerOrchestrator;
    }

    public string Id { get; }

    public string Name { get; }

    public AgentRole Role { get; }

    public string Description { get; }

    public AgentVisibility Visibility { get; }

    private string? NextRecommendedAgentId { get; }

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (Id == "chief-engineer" && _chiefEngineerOrchestrator is not null)
        {
            return _chiefEngineerOrchestrator.ExecuteAsync(context, Id, Name);
        }

        return Task.FromResult(Id switch
        {
            "mechanical-designer" => MechanicalDesignerOutput(),
            "cad-modeler" => CadModelerOutput(),
            "drawing-engineer" => DrawingEngineerOutput(),
            "drawing-reviewer" => DrawingReviewerOutput(),
            "error-diagnosis" => ErrorDiagnosisOutput(),
            _ => GenericOutput(context)
        });
    }

    private AgentOutput MechanicalDesignerOutput() =>
        Completed(
            "Mechanical design review completed: structure assumptions are reasonable for V0.2 planning.",
            [
                "Checked structure feasibility at planning level.",
                "Captured parameter and risk notes without calling CAD tools."
            ]);

    private AgentOutput CadModelerOutput() =>
        Completed(
            "CAD modeling plan completed: BuildSpec placeholder is ready for future worker handoff.",
            [
                "Prepared BuildSpec planning notes.",
                "No SolidWorks, AutoCAD, API, SDK, COM or Worker call was executed."
            ]);

    private AgentOutput DrawingEngineerOutput() =>
        Completed(
            "Drawing generation plan completed: DrawingSpec placeholder is ready for review.",
            [
                "Prepared required views and drawing planning notes.",
                "No drawing file was generated in V0.2."
            ]);

    private AgentOutput DrawingReviewerOutput()
    {
        var review = new ReviewReport(
            $"review-{Guid.NewGuid():N}",
            Id,
            IsPassed: true,
            Score: 0.92,
            Issues: Array.Empty<string>(),
            RequiresHumanApproval: false,
            HasFatalError: false);

        return new AgentOutput(
            AgentOutputStatus.Completed,
            "Drawing review completed: simulated ReviewReport passed QualityGate threshold.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "ReviewReport generated in placeholder mode." },
            NextRecommendedAgentId,
            null,
            review);
    }

    private AgentOutput ErrorDiagnosisOutput() =>
        Completed(
            "Error diagnosis placeholder is available for future rejected or failed workflows.",
            ["No failure diagnosis was needed in this route."]);

    private AgentOutput GenericOutput(AgentContext context) =>
        Completed(
            $"{Id} received task {context.TaskId} and returned placeholder output.",
            ["Placeholder agent returned structured platform response without CAD execution."]);

    private AgentOutput Completed(string message, IReadOnlyList<string> logs) =>
        new(
            AgentOutputStatus.Completed,
            message,
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            logs,
            NextRecommendedAgentId);
}
