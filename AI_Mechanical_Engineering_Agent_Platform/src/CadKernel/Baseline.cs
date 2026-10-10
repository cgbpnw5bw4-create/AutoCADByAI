using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadKernel
{
    /// <summary>
    /// 回归基准: input/&lt;任务&gt;/expected.json 记录一次经人工确认无误的运行结果, 运行结束时逐项比对:
    /// <code>
    ///   checks    必须出现且通过的校核
    ///   metrics   数值指标 (零件数、外形、质量、STEP 实体数、运动学残差…) 及允差
    ///   drawings  图纸清单, 每张的图幅、比例、尺寸数、孔标注数、序号数
    /// </code>
    /// 校核只能说明"没有报错", 基准才能说明"和以前一样对" —— 比例变了、尺寸少了、多出伪直径, 版面审核都不会报。
    /// 结果有意改变 (改进出图、修改设计) 后, 看图确认无误再用 `cad baseline &lt;运行目录&gt;` 重新生成, 并在提交说明里写明原因。
    /// 本类只读写文本, 不连接 SolidWorks。
    /// </summary>
    public static class Baseline
    {
        public const string FileName = "expected.json";
        public const string CheckName = "回归基准";

        public sealed class Expected
        {
            public string Note { get; set; }
            public string SourceRun { get; set; }
            public List<string> Checks { get; set; } = new List<string>();
            public Dictionary<string, MetricRecord> Metrics { get; set; } = new Dictionary<string, MetricRecord>();
            public List<DrawingRow> Drawings { get; set; } = new List<DrawingRow>();
        }

        public sealed class DrawingRow
        {
            public string File { get; set; }
            public string Size { get; set; }
            public string Scale { get; set; }
            public int? Dims { get; set; }
            public int? Holes { get; set; }
            public int? Balloons { get; set; }
        }

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>drawings/index.json → 比对用的图纸行 (按文件名排序)</summary>
        public static List<DrawingRow> ReadIndex(string indexJson)
        {
            if (string.IsNullOrWhiteSpace(indexJson)) return new List<DrawingRow>();
            return (JsonSerializer.Deserialize<List<DrawingRow>>(indexJson, Json) ?? new List<DrawingRow>())
                .OrderBy(r => r.File, StringComparer.Ordinal).ToList();
        }

        /// <summary>由一次运行生成基准: 已通过的校核 (不含回归基准本身)、全部指标、图纸目录</summary>
        public static string Generate(RunManifest m, string indexJson, string runName)
        {
            var e = new Expected
            {
                Note = "回归基准, 由 cad baseline 从人工确认无误的运行生成; 结果有意改变时重新生成并在提交说明里写明原因",
                SourceRun = runName,
                Checks = m.Checks.Where(c => c.Passed && c.Name != CheckName).Select(c => c.Name).ToList(),
                Metrics = m.Metrics.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value),
                Drawings = ReadIndex(indexJson),
            };
            return JsonSerializer.Serialize(e, Json);
        }

        public static Expected Parse(string expectedJson)
        {
            return JsonSerializer.Deserialize<Expected>(expectedJson, Json) ?? throw new Exception("基准文件为空");
        }

        /// <summary>与基准逐项比对, 返回偏离清单 (空 = 一致)。运行里多出的指标不算偏离, 多出或缺少的图纸都算</summary>
        public static List<string> Compare(Expected e, RunManifest m, string indexJson)
        {
            var d = new List<string>();
            foreach (string c in e.Checks)
            {
                var r = m.Checks.FirstOrDefault(x => x.Name == c);
                if (r == null) d.Add("校核缺失: " + c);
                else if (!r.Passed) d.Add("校核未通过: " + c + " (" + r.Summary + ")");
            }
            foreach (var kv in e.Metrics)
            {
                MetricRecord a;
                if (!m.Metrics.TryGetValue(kv.Key, out a)) { d.Add("指标缺失: " + kv.Key); continue; }
                double tol = kv.Value.Tol + Math.Abs(kv.Value.Value) * kv.Value.TolPct / 100;
                if (Math.Abs(a.Value - kv.Value.Value) > tol + 1e-9)
                    d.Add(string.Format("指标 {0}: {1}, 基准 {2}{3}", kv.Key, F(a.Value), F(kv.Value.Value), tol > 0 ? " ±" + F(tol) : ""));
            }
            var act = ReadIndex(indexJson);
            var byFile = new Dictionary<string, DrawingRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var y in act) byFile[y.File] = y;
            var expFiles = new HashSet<string>(e.Drawings.Select(x => x.File), StringComparer.OrdinalIgnoreCase);
            foreach (var x in e.Drawings)
            {
                DrawingRow y;
                if (!byFile.TryGetValue(x.File, out y)) { d.Add("图纸缺失: " + x.File); continue; }
                if (x.Size != y.Size || x.Scale != y.Scale) d.Add(string.Format("图纸 {0}: {1} {2}, 基准 {3} {4}", x.File, y.Size, y.Scale, x.Size, x.Scale));
                Count(d, x.File, "尺寸", x.Dims, y.Dims);
                Count(d, x.File, "孔标注", x.Holes, y.Holes);
                Count(d, x.File, "序号", x.Balloons, y.Balloons);
            }
            foreach (var y in act) if (!expFiles.Contains(y.File)) d.Add("基准中没有的图纸: " + y.File);
            return d;
        }

        static void Count(List<string> d, string file, string what, int? exp, int? act)
        {
            if (exp.HasValue && exp != act) d.Add(string.Format("图纸 {0}: {1} {2} 处, 基准 {3} 处", file, what, act.HasValue ? act.ToString() : "无", exp));
        }

        static string F(double v) { return v.ToString("0.#####"); }
    }
}
