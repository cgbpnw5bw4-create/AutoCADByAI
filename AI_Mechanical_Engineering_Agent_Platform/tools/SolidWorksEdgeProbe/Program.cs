// 只读拓扑探查工具。
//
// 用途：采集 Feature 证据之前，先看清目标零件到底有哪些边，
// 以便写出能唯一命中的 EdgeSelectionCriteria。
//
// 纪律：
//   - 只打开、只读取、只关闭，不修改也不保存任何模型。
//   - 走生产代码 RealSolidWorksGeometryReader，因此探查到的就是
//     真实执行时会看到的同一份拓扑，不存在"工具能看见但生产看不见"。
//   - 默认不启动 SolidWorks：必须显式传入 --confirm-real-cad。
using System.Globalization;
using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker;
using SolidWorksWorker.Features;

const int SwDocPart = 1;

var options = ProbeOptions.Parse(args);
if (options.ShowUsage)
{
    Console.WriteLine("usage: SolidWorksEdgeProbe --input <path-to-sldprt> --confirm-real-cad [--json <out.json>]");
    Console.WriteLine();
    Console.WriteLine("  --confirm-real-cad  必须显式给出。缺少该开关时工具不会启动 SolidWorks。");
    return 1;
}

if (!options.ConfirmRealCad)
{
    Console.WriteLine("real_cad_not_confirmed: 缺少 --confirm-real-cad，未启动 SolidWorks。");
    return 1;
}

if (!File.Exists(options.InputPath))
{
    Console.WriteLine($"input_missing: {options.InputPath}");
    return 2;
}

// COM 只在 Windows 上存在。显式判一次，既让平台分析器满意，
// 也把"在别的系统上跑"变成一条可读的拒绝，而不是运行时异常。
if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("unsupported_platform: SolidWorks COM 只在 Windows 上可用。");
    return 3;
}

var com = new LateBoundSolidWorksComFacade();
var progId = Type.GetTypeFromProgID("SldWorks.Application");
if (progId is null)
{
    Console.WriteLine("solidworks_progid_missing: SldWorks.Application 未注册。");
    return 3;
}

object? application = null;
object? document = null;
try
{
    application = Activator.CreateInstance(progId);
    if (application is null)
    {
        Console.WriteLine("solidworks_activation_failed");
        return 3;
    }

    com.TrySetProperty(application, "Visible", options.Visible);

    // OpenDoc 没有 ref 参数，因此可以完全走后期绑定，
    // 不需要为一个诊断工具引入类型化 interop 依赖。
    document = com.TryInvoke(application, "OpenDoc", Path.GetFullPath(options.InputPath), SwDocPart);
    if (document is null)
    {
        Console.WriteLine("open_failed");
        return 4;
    }

    var result = new RealSolidWorksGeometryReader(com).Read(document);
    Console.WriteLine($"read_success={result.IsSuccess} failure_stage={result.FailureStage ?? "none"}");
    foreach (var issue in result.Issues)
    {
        Console.WriteLine($"  issue: {issue}");
    }

    var edges = result.Geometry?.Edges ?? Array.Empty<MeasuredEdge>();
    Console.WriteLine($"body_count={result.Geometry?.BodyCount} edge_count={edges.Count}");

    if (edges.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("idx | kind   | length_mm | anchor(x, y, z mm)              | radius | adjacent");
        Console.WriteLine(new string('-', 96));
        foreach (var edge in edges)
        {
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0,3} | {1,-6} | {2,9:F2} | ({3,8:F2},{4,8:F2},{5,8:F2}) | {6,6} | {7}",
                edge.Index,
                edge.Kind,
                edge.LengthMm,
                edge.AnchorXMm,
                edge.AnchorYMm,
                edge.AnchorZMm,
                edge.RadiusMm?.ToString("F2", CultureInfo.InvariantCulture) ?? "-",
                string.Join("+", edge.AdjacentSurfaceKinds)));
        }

        Console.WriteLine();
        Console.WriteLine("按相邻面组合分组（写判据时最常用的区分维度）：");
        foreach (var group in edges
                     .GroupBy(edge => $"{edge.Kind}/{string.Join("+", edge.AdjacentSurfaceKinds.Order(StringComparer.Ordinal))}")
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {group.Key,-28} count={group.Count()}");
        }
    }

    if (!string.IsNullOrWhiteSpace(options.JsonPath))
    {
        var json = JsonSerializer.Serialize(
            new { input = Path.GetFullPath(options.InputPath), edge_count = edges.Count, edges },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.GetFullPath(options.JsonPath), json);
        Console.WriteLine($"json_written={Path.GetFullPath(options.JsonPath)}");
    }

    Console.WriteLine("probe_done");
    return result.IsSuccess ? 0 : 5;
}
finally
{
    if (document is not null && application is not null)
    {
        // 只关闭，不保存。探查绝不改动被查零件。
        var title = com.TryInvoke(document, "GetTitle")?.ToString();
        if (!string.IsNullOrWhiteSpace(title))
        {
            com.TryInvoke(application, "CloseDoc", title);
        }

        com.ReleaseComObject(document);
    }
}

internal sealed record ProbeOptions(
    string InputPath,
    bool ConfirmRealCad,
    bool Visible,
    string? JsonPath,
    bool ShowUsage)
{
    public static ProbeOptions Parse(string[] args)
    {
        string? input = null;
        string? json = null;
        var confirm = false;
        var visible = true;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index].ToLowerInvariant())
            {
                case "--input" when index + 1 < args.Length:
                    input = args[++index];
                    break;
                case "--json" when index + 1 < args.Length:
                    json = args[++index];
                    break;
                case "--confirm-real-cad":
                    confirm = true;
                    break;
                case "--hidden":
                    visible = false;
                    break;
            }
        }

        return new(input ?? string.Empty, confirm, visible, json, string.IsNullOrWhiteSpace(input));
    }
}
