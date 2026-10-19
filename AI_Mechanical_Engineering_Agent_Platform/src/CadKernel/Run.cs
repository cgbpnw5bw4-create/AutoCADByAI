using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CadKernel
{
    /// <summary>
    /// 一次运行 = output/&lt;时间&gt;_&lt;任务&gt;/ 下的一套完整产物:
    /// <code>
    ///   run.json   流程清单 (输入、步骤、耗时、校核结论、产物计数)      run.log  完整日志
    ///   input/     输入快照        models/    零件与装配体        drawings/  SLDDRW + PDF + 图纸目录
    ///   exports/   STEP、dxf/      bom/       物料清单 (md + csv)  checks/    校核报告        images/  视图截图
    /// </code>
    /// 同一次运行内的所有文件互相引用, 整个目录可以原样拷走。
    /// </summary>
    public static class Run
    {
        public static string PlatformRoot { get; private set; }
        public static string Dir { get; private set; }

        public static string Models { get { return Path.Combine(Dir, "models"); } }
        public static string Drawings { get { return Path.Combine(Dir, "drawings"); } }
        public static string Exports { get { return Path.Combine(Dir, "exports"); } }
        public static string Dxf { get { return Path.Combine(Exports, "dxf"); } }
        public static string Bom { get { return Path.Combine(Dir, "bom"); } }
        public static string Checks { get { return Path.Combine(Dir, "checks"); } }
        public static string Images { get { return Path.Combine(Dir, "images"); } }
        public static string InputSnapshot { get { return Path.Combine(Dir, "input"); } }
        public static string OutputRoot { get { return Path.Combine(PlatformRoot, "output"); } }
        /// <summary>机器级状态 (专用 SolidWorks 实例 PID、被临时修改的用户选项原值), 不属于任何一次运行</summary>
        public static string StateDir { get { return Path.Combine(PlatformRoot, ".cad"); } }

        public static RunManifest Manifest { get; private set; } = new RunManifest();
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        /// <summary>只记录平台根目录; 目录在真正写入时才创建 (list / doctor 等只读命令不在平台里留下文件)</summary>
        public static void Init(string platformRoot)
        {
            PlatformRoot = platformRoot;
        }

        /// <summary>新运行目录的名称 = yyyyMMdd_HHmmss_任务名 (长度固定, 运行前估算路径长度也用它)</summary>
        public static string NewDirName(string task) { return DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Safe(task); }

        /// <summary>新建运行目录并拷贝输入快照</summary>
        public static string Begin(string task, string projectId, string title, string inputDir)
        {
            Directory.CreateDirectory(OutputRoot);
            Dir = Path.Combine(OutputRoot, NewDirName(task));
            if (Directory.Exists(Dir)) throw new IOException("运行目录已存在: " + Dir);
            foreach (string d in new[] { Dir, Models, Drawings, Exports, Bom, Checks, Images, InputSnapshot }) Directory.CreateDirectory(d);
            // 含子目录 (导入设计的 geometry/): 续跑读的是快照, 运行目录也须能整体拷走
            if (inputDir != null && Directory.Exists(inputDir))
                foreach (string f in Directory.GetFiles(inputDir, "*", SearchOption.AllDirectories))
                {
                    string to = Path.Combine(InputSnapshot, Path.GetRelativePath(inputDir, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(f, to);
                }
            Manifest = new RunManifest
            {
                Task = task, Project = projectId, Title = title, RunDir = Dir, Input = inputDir,
                StartedAt = Now(), Status = "running", Machine = Environment.MachineName,
            };
            StartLog();
            Save();
            return Dir;
        }

        /// <summary>续跑已有运行目录 (如零件和装配已完成, 只补出图)</summary>
        public static void Resume(string runDir)
        {
            Dir = Path.GetFullPath(runDir);
            string mf = Path.Combine(Dir, "run.json");
            if (!File.Exists(mf)) throw new FileNotFoundException("不是运行目录 (缺 run.json): " + Dir);
            Manifest = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(mf), Json);
            Manifest.Status = "running";
            Manifest.FinishedAt = null;
            StartLog();
            Save();
        }

        static TextWriter consoleOut;
        static StreamWriter logFile;

        static void StartLog()
        {
            CloseLog();
            consoleOut = Console.Out;
            logFile = new StreamWriter(Path.Combine(Dir, "run.log"), true, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new Tee(consoleOut, logFile));
        }

        /// <summary>停止向 run.log 复制控制台输出并释放文件</summary>
        public static void CloseLog()
        {
            if (logFile == null) return;
            Console.SetOut(consoleOut);
            logFile.Dispose();
            logFile = null;
        }

        /// <summary>记录一个流程步骤的起止、耗时与结论; 抛出的异常照常向上传播 (失败即中止)</summary>
        public static void Step(string name, Action body)
        {
            var rec = new StepRecord { Name = name, StartedAt = Now(), Status = "running" };
            Manifest.Steps.RemoveAll(s => s.Name == name);
            Manifest.Steps.Add(rec);
            Save();
            Console.WriteLine();
            Console.WriteLine("##### 步骤: " + name + " #####");
            var sw = Stopwatch.StartNew();
            try
            {
                body();
                rec.Status = "passed";
            }
            catch (Exception e)
            {
                rec.Status = "failed";
                rec.Message = e.Message;
                throw;
            }
            finally
            {
                rec.Seconds = Math.Round(sw.Elapsed.TotalSeconds, 1);
                Save();
            }
        }

        /// <summary>登记一项校核结论。passed=false 不中止运行, 但运行最终状态为 failed</summary>
        public static void Check(string name, bool passed, string summary, string file = null)
        {
            Manifest.Checks.RemoveAll(c => c.Name == name);
            Manifest.Checks.Add(new CheckRecord { Name = name, Passed = passed, Summary = summary, File = file == null ? null : Rel(file) });
            Console.WriteLine(string.Format("  [校核] {0}: {1}  {2}", name, passed ? "通过" : "不通过", summary));
            Save();
        }

        /// <summary>
        /// 登记一项可比对的数值指标 (零件数、外形、质量、残差…), 写入 run.json 的 metrics, 供回归基准 (Baseline) 比对。
        /// tol = 绝对允差, tolPct = 相对允差 (%), 都为 0 表示必须相等; 生成基准时允差一并写入 expected.json。
        /// </summary>
        public static void Metric(string key, double value, double tol = 0, double tolPct = 0)
        {
            Manifest.Metrics[key] = new MetricRecord { Value = Math.Round(value, 6), Tol = tol, TolPct = tolPct };
            Save();
        }

        public static void Finish(bool ok, string error = null)
        {
            Manifest.FinishedAt = Now();
            Manifest.Error = error;
            Manifest.Artifacts = CountArtifacts();
            bool checksOk = Manifest.Checks.All(c => c.Passed);
            Manifest.Status = ok && checksOk ? "passed" : "failed";
            Save();
        }

        public static void Save()
        {
            if (Dir == null) return;
            File.WriteAllText(Path.Combine(Dir, "run.json"), JsonSerializer.Serialize(Manifest, Json), new UTF8Encoding(false));
        }

        public static string Rel(string path)
        {
            string full = Path.GetFullPath(path);
            return full.StartsWith(Dir, StringComparison.OrdinalIgnoreCase) ? full.Substring(Dir.Length).TrimStart('\\', '/').Replace('\\', '/') : full;
        }

        static Dictionary<string, int> CountArtifacts()
        {
            // 不计 SolidWorks 锁文件 (~$) 与版面审核用的空白图框 (drawings/_formats)
            Func<string, string, int> n = (dir, pat) => Directory.Exists(dir) ? Directory.GetFiles(dir, pat, SearchOption.AllDirectories)
                .Count(f => !Path.GetFileName(f).StartsWith("~$") && !f.Contains(Path.DirectorySeparatorChar + "_formats" + Path.DirectorySeparatorChar)) : 0;
            return new Dictionary<string, int>
            {
                { "parts", n(Models, "*.SLDPRT") },
                { "assemblies", n(Models, "*.SLDASM") },
                { "drawings", n(Drawings, "*.SLDDRW") },
                { "pdf", n(Drawings, "*.pdf") },
                { "step", n(Exports, "*.step") },
                { "dxf", n(Exports, "*.dxf") },
                { "bom_csv", n(Bom, "*.csv") },
            };
        }

        static string Now() { return DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"); }

        public static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        sealed class Tee : TextWriter
        {
            readonly TextWriter a, b;
            public Tee(TextWriter a, TextWriter b) { this.a = a; this.b = b; }
            public override Encoding Encoding { get { return Encoding.UTF8; } }
            public override void Write(char value) { a.Write(value); b.Write(value); }
            public override void Write(string value) { a.Write(value); b.Write(value); }
            public override void WriteLine(string value) { a.WriteLine(value); b.WriteLine(value); }
            public override void Flush() { a.Flush(); b.Flush(); }
        }
    }

    public sealed class RunManifest
    {
        public string Task { get; set; }
        public string Project { get; set; }
        public string Title { get; set; }
        public string RunDir { get; set; }
        public string Input { get; set; }
        public string StartedAt { get; set; }
        public string FinishedAt { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public string Machine { get; set; }
        public string SolidWorks { get; set; }
        public List<StepRecord> Steps { get; set; } = new List<StepRecord>();
        public List<CheckRecord> Checks { get; set; } = new List<CheckRecord>();
        public Dictionary<string, MetricRecord> Metrics { get; set; } = new Dictionary<string, MetricRecord>();
        public Dictionary<string, int> Artifacts { get; set; }
    }

    public sealed class MetricRecord
    {
        public double Value { get; set; }
        public double Tol { get; set; }
        public double TolPct { get; set; }
    }

    public sealed class StepRecord
    {
        public string Name { get; set; }
        public string Status { get; set; }
        public string StartedAt { get; set; }
        public double Seconds { get; set; }
        public string Message { get; set; }
    }

    public sealed class CheckRecord
    {
        public string Name { get; set; }
        public bool Passed { get; set; }
        public string Summary { get; set; }
        public string File { get; set; }
    }
}
