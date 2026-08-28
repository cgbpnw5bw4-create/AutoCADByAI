using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainSchemas;

/// <summary>
/// 把 FeatureGraph 中的 <c>edge_selection</c> 参数解析为结构化判据。
/// <para>
/// 之所以用 JSON 而不是自由文本：边选择必须是**可机械求解**的声明，
/// 自由文本（例如 "top_outer_edges"）无法对实测拓扑求解，只能靠人理解，
/// 那等于把选择集交给运气。
/// </para>
/// </summary>
public static class EdgeSelectionCriteriaParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static bool TryParse(string? json, out EdgeSelectionCriteria? criteria, out string? issue)
    {
        criteria = null;
        issue = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            issue = "edge_selection must not be empty.";
            return false;
        }

        EdgeSelectionCriteriaDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<EdgeSelectionCriteriaDocument>(json, Options);
        }
        catch (JsonException exception)
        {
            issue = $"edge_selection is not valid JSON: {exception.Message}";
            return false;
        }

        if (document is null)
        {
            issue = "edge_selection deserialized to null.";
            return false;
        }

        var parsed = new EdgeSelectionCriteria(
            document.Kind,
            document.LengthMm,
            document.RadiusMm,
            document.AdjacentSurfaceKinds,
            document.AnchorXMm,
            document.AnchorYMm,
            document.AnchorZMm,
            document.ToleranceMm ?? 0.05d,
            document.ExpectedCount ?? 1);

        // 用与执行时相同的解析器做一次空拓扑试算，把"判据本身非法"
        // 这类错误提前到 Validate 阶段，而不是等到连上 COM 才发现。
        var dryRun = EdgeSelectionResolver.Resolve(Array.Empty<MeasuredEdge>(), parsed);
        if (dryRun.FailureStage == PartFamilyFailureStages.EdgeSelectionInvalidCriteria)
        {
            issue = dryRun.Issues.FirstOrDefault() ?? "edge_selection criteria are invalid.";
            return false;
        }

        criteria = parsed;
        return true;
    }

    private sealed record EdgeSelectionCriteriaDocument
    {
        public string? Kind { get; init; }

        public double? LengthMm { get; init; }

        public double? RadiusMm { get; init; }

        public IReadOnlyList<string>? AdjacentSurfaceKinds { get; init; }

        public double? AnchorXMm { get; init; }

        public double? AnchorYMm { get; init; }

        public double? AnchorZMm { get; init; }

        public double? ToleranceMm { get; init; }

        public int? ExpectedCount { get; init; }
    }
}
