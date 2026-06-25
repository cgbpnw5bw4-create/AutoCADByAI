using AgentContracts;
using DomainSchemas;

namespace PlatformCore.Modules.DrawingReview.Agents;

public sealed class DrawingReviewerAgent : IAgent
{
    public string Id => "drawing-reviewer";

    public string Name => "出图复审工程师";

    public AgentRole Role { get; } = new(
        "drawing-reviewer",
        "出图复审工程师",
        "PDF、尺寸、视图、标题栏复审");

    public string Description => "Internal drawing reviewer.";

    public AgentVisibility Visibility => AgentVisibility.Internal;

    public Task<AgentOutput> ExecuteAsync(AgentContext context)
    {
        if (IsScenario(context, "drawing_reviewer_needs_human_approval"))
        {
            var approvalReview = new ReviewReport(
                $"review-{Guid.NewGuid():N}",
                Id,
                IsPassed: false,
                Score: 0.5,
                Issues: new[] { "needs_human_approval issue: drawing review requires manual approval" },
                RequiresHumanApproval: true,
                HasFatalError: false);

            return Task.FromResult(new AgentOutput(
                AgentOutputStatus.NeedsHumanApproval,
                "Drawing review requires simulated human approval.",
                Array.Empty<ArtifactInfo>(),
                approvalReview.Issues,
                new[] { "DrawingReviewerAgent simulated human approval request." },
                null,
                null,
                approvalReview));
        }

        var review = new ReviewReport(
            $"review-{Guid.NewGuid():N}",
            Id,
            IsPassed: true,
            Score: 0.92,
            Issues: Array.Empty<string>(),
            RequiresHumanApproval: false,
            HasFatalError: false);

        return Task.FromResult(new AgentOutput(
            AgentOutputStatus.Completed,
            "Drawing review completed: simulated ReviewReport passed QualityGate threshold.",
            Array.Empty<ArtifactInfo>(),
            Array.Empty<string>(),
            new[] { "ReviewReport generated in module agent mode." },
            null,
            null,
            review));
    }

    private static bool IsScenario(AgentContext context, string scenario) =>
        context.Input.Context.TryGetValue("test_scenario", out var value) &&
        string.Equals(value, scenario, StringComparison.OrdinalIgnoreCase);
}
