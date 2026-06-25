using System.Text.Json;
using AgentContracts;
using DomainSchemas;

namespace AgentRuntime.Microsoft;

public sealed class MicrosoftAgentOutputMapper
{
    private readonly HashSet<string> _internalAgentIds;

    public MicrosoftAgentOutputMapper(IEnumerable<string> internalAgentIds)
    {
        _internalAgentIds = new HashSet<string>(internalAgentIds, StringComparer.OrdinalIgnoreCase);
    }

    public AgentOutput Map(string modelOutput, RuntimeMetadata metadata)
    {
        try
        {
            using var document = JsonDocument.Parse(modelOutput);
            var root = document.RootElement;
            var issues = new List<string>();
            var logs = new List<string> { "Microsoft runtime model output parsed as JSON." };
            var message = GetString(root, "message") ?? modelOutput;
            var status = ParseStatus(GetString(root, "status"));
            var nextRecommendedAgentId = ValidateNextAgent(GetString(root, "next_recommended_agent_id"), issues);

            if (HasPermissionEscalation(root))
            {
                issues.Add("Model attempted permission escalation or Agent visibility change; request ignored.");
            }

            foreach (var agentId in GetStringArray(root, "recommended_internal_agents"))
            {
                ValidateRecommendedAgent(agentId, issues);
            }

            foreach (var tool in GetStringArray(root, "tools"))
            {
                if (IsWorkerReference(tool))
                {
                    issues.Add($"Model attempted direct Worker call '{tool}'; request ignored.");
                }
            }

            var taskSummary = GetString(root, "task_summary");
            if (!string.IsNullOrWhiteSpace(taskSummary))
            {
                logs.Add($"Task summary: {taskSummary}");
            }

            foreach (var risk in GetStringArray(root, "risks"))
            {
                logs.Add($"Risk: {risk}");
            }

            return new AgentOutput(
                status,
                message,
                Array.Empty<ArtifactInfo>(),
                issues,
                logs,
                nextRecommendedAgentId,
                RuntimeMetadata: metadata);
        }
        catch (JsonException)
        {
            return new AgentOutput(
                AgentOutputStatus.Completed,
                modelOutput,
                Array.Empty<ArtifactInfo>(),
                Array.Empty<string>(),
                new[] { "Invalid model JSON; fallback converted raw text to AgentOutput." },
                null,
                RuntimeMetadata: metadata);
        }
    }

    private string? ValidateNextAgent(string? nextRecommendedAgentId, List<string> issues)
    {
        if (string.IsNullOrWhiteSpace(nextRecommendedAgentId))
        {
            return null;
        }

        if (IsWorkerReference(nextRecommendedAgentId))
        {
            issues.Add($"Model attempted direct Worker call '{nextRecommendedAgentId}' through next_recommended_agent_id; request ignored.");
            return null;
        }

        if (!_internalAgentIds.Contains(nextRecommendedAgentId))
        {
            issues.Add($"Model recommended unknown or non-internal agent '{nextRecommendedAgentId}'; request ignored.");
            return null;
        }

        return nextRecommendedAgentId;
    }

    private void ValidateRecommendedAgent(string agentId, List<string> issues)
    {
        if (IsWorkerReference(agentId))
        {
            issues.Add($"Model attempted direct Worker call '{agentId}'; request ignored.");
            return;
        }

        if (!_internalAgentIds.Contains(agentId))
        {
            issues.Add($"Model recommended unknown or non-internal agent '{agentId}'; request ignored.");
        }
    }

    private static bool HasPermissionEscalation(JsonElement root) =>
        string.Equals(GetString(root, "visibility"), "Public", StringComparison.OrdinalIgnoreCase) ||
        (root.TryGetProperty("expose_internal_agents", out var expose) &&
         expose.ValueKind == JsonValueKind.True);

    private static bool IsWorkerReference(string value) =>
        value.Contains("worker", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("SolidWorks", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("AutoCAD", StringComparison.OrdinalIgnoreCase);

    private static AgentOutputStatus ParseStatus(string? value) =>
        Enum.TryParse<AgentOutputStatus>(value, ignoreCase: true, out var status)
            ? status
            : AgentOutputStatus.Completed;

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();
    }
}
