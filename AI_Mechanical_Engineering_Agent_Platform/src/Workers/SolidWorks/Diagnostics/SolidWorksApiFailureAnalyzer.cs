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
                    "孔草图已经创建，但切除特征调用的参数签名可能与当前 SolidWorks 版本不匹配。",
                    "孔草图可能没有处于可被 FeatureCut 识别的选择状态。",
                    "切除前可能没有清理或设置正确的选择对象。",
                    "FeatureCut4 长参数列表存在版本差异，late binding 下容易触发参数数量不匹配。",
                    "切除深度、方向和单位必须统一为米。"
                },
                new[]
                {
                    "SolidWorks API FeatureCut4 cut extrude",
                    "SolidWorks API CreateCircleByRadius SketchManager",
                    "SolidWorks API Cut Extrude Example VBA",
                    "SolidWorks API SelectByID2 sketch cut extrude",
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
