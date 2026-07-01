using System.Text.Json;

namespace SolidWorksWorker.Diagnostics;

public sealed record SolidWorksApiFailureAnalysis(
    string FailureStage,
    string TargetOperation,
    IReadOnlyList<string> SuspectedCauses,
    IReadOnlyList<string> RecommendedApiSearchTerms);

public sealed class SolidWorksApiFailureAnalyzer
{
    public SolidWorksApiFailureAnalysis AnalyzeDiagnosticReport(string diagnosticReportPath)
    {
        if (!File.Exists(diagnosticReportPath))
        {
            throw new FileNotFoundException("diagnostic_report.json was not found.", diagnosticReportPath);
        }

        using var stream = File.OpenRead(diagnosticReportPath);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var failureStage = ReadString(root, "failure_stage") ?? "unknown_failed";

        return AnalyzeFailureStage(failureStage);
    }

    public SolidWorksApiFailureAnalysis AnalyzeFailureStage(string failureStage)
    {
        if (failureStage.Equals("cut_holes_failed", StringComparison.OrdinalIgnoreCase))
        {
            return new SolidWorksApiFailureAnalysis(
                "cut_holes_failed",
                "plate_basic_4holes through-hole cut extrude",
                new[]
                {
                    "用户第二份录制宏显示，FeatureExtrusion2 用于板件基体拉伸，FeatureCut4 用于后续活动孔草图切除。",
                    "切孔失败可能来自 FeatureCut4 长参数顺序、活动草图状态或切除方向，而不是 FeatureExtrusion2 本身。",
                    "孔圆需要用 CreateCircle 的圆心加圆上一点形式创建，并保持孔草图活动状态后再切除。",
                    "FeatureCut4 长参数列表存在版本差异，late binding 下容易触发参数数量不匹配。",
                    "切除深度、方向和单位必须统一为米。"
                },
                new[]
                {
                    "SolidWorks API FeatureExtrusion2 base extrude",
                    "SolidWorks API CreateCircle SketchManager",
                    "SolidWorks API FeatureCut4 active sketch cut extrude",
                    "SolidWorks API FeatureCut4 cut extrude",
                    "SolidWorks API CreateCircleByRadius SketchManager",
                    "SolidWorks API Cut Extrude Example VBA",
                    "SolidWorks API SelectByID2 sketch cut extrude",
                    "SolidWorks API FeatureManager FeatureExtrusion2 parameters",
                    "SolidWorks API FeatureManager FeatureCut4 parameters"
                });
        }

        return new SolidWorksApiFailureAnalysis(
            failureStage,
            "unknown SolidWorks API operation",
            new[] { "当前失败阶段尚未建立专用 API 分析规则。" },
            new[] { $"SolidWorks API {failureStage}" });
    }

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
