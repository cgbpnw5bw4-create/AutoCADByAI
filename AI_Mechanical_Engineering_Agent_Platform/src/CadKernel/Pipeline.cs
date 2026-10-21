using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 统一流水线: 参数校验 → 零件 → 装配 → 校核 → 工程图 → 导出 → 后处理。
    /// 每一步写入 run.json; 失败即中止并保留已完成的产物, 可用 resume 从指定步骤续跑。
    /// 校核不通过不中止 (产物仍可查看), 但运行最终状态为 failed。
    /// </summary>
    public static class Pipeline
    {
        public static readonly string[] AllSteps = { "parts", "assembly", "checks", "drawings", "export", "post" };
        static readonly Dictionary<string, string> StepCn = new Dictionary<string, string>
        {
            { "parts", "零件" }, { "assembly", "装配" }, { "checks", "校核" }, { "drawings", "工程图" }, { "export", "导出" }, { "post", "后处理" },
        };

        /// <summary>执行一次运行。resumeDir 非空时在已有运行目录上续跑 fromStep 及之后的步骤</summary>
        public static int Execute(CadProject project, DesignInput input, string resumeDir = null, string fromStep = null, string drawingFilter = null)
        {
            // 内核静态状态 (登记表、紧固件组缓存、出图目录) 不得带到本次运行
            Cat.Reset(); Std.Reset(); Drw.Reset();
            project.Bind(input);
            List<string> problems;
            try { problems = project.Validate(); }
            catch (Exception ex) { problems = new List<string> { ex.Message }; }     // 参数类型错误等
            var unknown = input.Unknown(project.KnownParams);
            if (unknown.Count > 0) problems.Add("未知参数 (拼写错误?): " + string.Join(", ", unknown));
            var steps = input.Steps.Count > 0 ? input.Steps : AllSteps.ToList();
            foreach (string s in steps) if (!AllSteps.Contains(s)) problems.Add("未知步骤: " + s);
            if (fromStep != null)
            {
                if (!AllSteps.Contains(fromStep)) problems.Add("未知步骤: " + fromStep);
                else steps = steps.Where(s => Array.IndexOf(AllSteps, s) >= Array.IndexOf(AllSteps, fromStep)).ToList();
            }
            if (problems.Count > 0)
            {
                Console.WriteLine("参数校验未通过 (未连接 SolidWorks):");
                foreach (string p in problems) Console.WriteLine("  - " + p);
                return 2;
            }
            // 模型路径超出 MAX_PATH 时出图会无限挂起 (见 PathLimit): 要出图的运行不启动, 其余情况提示后照常运行
            var longest = PathLimit.ForRun(project, input.Task, resumeDir);
            var pathNotes = PathLimit.Advice(longest, Run.PlatformRoot, resumeDir != null);
            if (pathNotes.Count > 0) pathNotes.Insert(0, PathLimit.Describe(longest));
            if (longest.Level == PathLimit.Level.Over)
            {
                if (steps.Contains("drawings"))
                {
                    Console.WriteLine("模型路径过长, 未启动 (未连接 SolidWorks):");
                    foreach (string p in pathNotes) Console.WriteLine("  - " + p);
                    return 2;
                }
                pathNotes.Add("本次步骤不含出图, 照常运行; 之后在这个运行目录上续跑出图同样会挂起");
            }

            if (resumeDir != null) Run.Resume(resumeDir);
            else Run.Begin(input.Task, project.Id, project.Title, input.Dir);
            Console.WriteLine("运行目录: " + Run.Dir);
            foreach (string p in pathNotes) Console.WriteLine("  !! " + p);
            Run.Check("参数校验", true, "通过 (" + project.Id + ")");

            List<AsmTarget> targets = null;
            bool ok = false;
            string error = null;
            try
            {
                Cat.Date = Run.Manifest.StartedAt.Substring(0, 10);
                project.Prepare();
                if (resumeDir != null && steps.Count > 0 && steps[0] == "assembly" && Cat.Lazy)
                {
                    // 按需建模的设计在零件步骤只登记定义 (不建模), 续跑装配前须重新登记
                    Console.WriteLine("按需建模: 从装配续跑, 先重新登记零件定义");
                    steps.Insert(0, "parts");
                }
                Sw.Connect();
                if (resumeDir != null)
                {
                    // 从零件步骤续跑 = 重新建模, 不载入旧登记表 (零件步骤失败过的目录也没有登记表)
                    if (steps.Count > 0 && steps[0] != "parts") Cat.LoadReg();
                    targets = LoadTargets();
                }
                foreach (string step in steps)
                {
                    string cn = StepCn[step];
                    switch (step)
                    {
                        case "parts":
                            Run.Step(cn, () => { Sw.CloseAll(); project.BuildParts(); Cat.SaveReg(); });
                            break;
                        case "assembly":
                            Run.Step(cn, () => { targets = project.BuildAssemblies(); Cat.SaveReg(); SaveTargets(targets); });
                            break;
                        case "checks":
                            Run.Step(cn, () => DoChecks(project, Need(targets)));
                            break;
                        case "drawings":
                            Run.Step(cn, () => DoDrawings(project, Need(targets), drawingFilter));
                            break;
                        case "export":
                            Run.Step(cn, () => DoExport(Need(targets)));
                            break;
                        case "post":
                            Run.Step(cn, () => DoPost(project));
                            break;
                    }
                }
                CheckBaseline(input.Dir);
                ok = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Console.WriteLine();
                Console.WriteLine("[ERR] " + ex.Message);
                for (Exception ie = ex.InnerException; ie != null; ie = ie.InnerException) Console.WriteLine("  <- " + ie.Message);
                Console.WriteLine(ex.StackTrace);
            }
            finally
            {
                try { if (Sw.App != null) Sw.CloseAll(); } catch { }
                Sw.Disconnect();
                Run.Finish(ok, error);
            }
            Console.WriteLine();
            Console.WriteLine(string.Format("### 运行{0}: {1}", Run.Manifest.Status == "passed" ? "通过" : "未通过", Run.Dir));
            foreach (var c in Run.Manifest.Checks) Console.WriteLine(string.Format("   {0} {1}: {2}", c.Passed ? "✓" : "✗", c.Name, c.Summary));
            Run.CloseLog();
            return Run.Manifest.Status == "passed" ? 0 : 1;
        }

        /// <summary>
        /// 有 input/&lt;任务&gt;/expected.json 时与之逐项比对 (见 Baseline)。步骤没有全部完成也算不通过:
        /// 部分运行无法证明"和以前一样对"。没有基准的设计 (新设计) 不登记这项校核。
        /// </summary>
        static void CheckBaseline(string inputDir)
        {
            string f = inputDir == null ? null : Path.Combine(inputDir, Baseline.FileName);
            if (f == null || !File.Exists(f)) { Console.WriteLine("  (无回归基准 " + Baseline.FileName + ", 不比对)"); return; }
            var diffs = new List<string>();
            var notRun = AllSteps.Where(s => !Run.Manifest.Steps.Any(r => r.Name == StepCn[s] && r.Status == "passed")).Select(s => StepCn[s]).ToList();
            if (notRun.Count > 0) diffs.Add("步骤未完成: " + string.Join(", ", notRun) + " (部分运行, 无法完整比对)");
            Baseline.Expected e = null;
            try { e = Baseline.Parse(File.ReadAllText(f)); }
            catch (Exception ex) { diffs.Add("基准文件无法解析: " + ex.Message); }
            if (e != null)
            {
                string idx = Path.Combine(Run.Drawings, "index.json");
                diffs.AddRange(Baseline.Compare(e, Run.Manifest, File.Exists(idx) ? File.ReadAllText(idx) : null));
            }
            var sb = new StringBuilder();
            sb.AppendLine("# 回归基准比对");
            sb.AppendLine();
            sb.AppendLine("> 基准: `" + Run.Rel(f) + "`" + (e != null && e.SourceRun != null ? " (来源运行 " + e.SourceRun + ")" : ""));
            sb.AppendLine();
            if (diffs.Count == 0) sb.AppendLine("全部一致。");
            foreach (string s in diffs) sb.AppendLine("- " + s);
            string report = Path.Combine(Run.Checks, "baseline.md");
            File.WriteAllText(report, sb.ToString(), new UTF8Encoding(false));
            foreach (string s in diffs) Console.WriteLine("  !! " + s);
            Run.Check(Baseline.CheckName, diffs.Count == 0,
                diffs.Count == 0 ? string.Format("一致: 校核 {0} 项, 指标 {1} 项, 图纸 {2} 张 (外形尺寸读数 {3} 个)", e.Checks.Count, e.Metrics.Count, e.Drawings.Count,
                                                 e.Drawings.Sum(x => x.Extents?.Count ?? 0))
                                 : diffs.Count + " 处偏离: " + string.Join("; ", diffs.Take(3)) + (diffs.Count > 3 ? " …" : ""),
                report);
        }

        static List<AsmTarget> Need(List<AsmTarget> t)
        {
            if (t == null || t.Count == 0) throw new Exception("尚无装配体 (先运行 assembly 步骤)");
            return t;
        }

        static string TargetsFile { get { return Path.Combine(Run.Dir, "assemblies.json"); } }

        static void SaveTargets(List<AsmTarget> t)
        {
            var rows = t.Select(a => new Dictionary<string, object> { { "path", a.Path }, { "top_level", a.TopLevel }, { "drawing", a.Drawing }, { "main", a.Main },
                                                                    { "force_a0", a.ForceA0 }, { "top_level_bom", a.TopLevelBom } });
            File.WriteAllText(TargetsFile, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
        }

        static List<AsmTarget> LoadTargets()
        {
            if (!File.Exists(TargetsFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(TargetsFile));
            return doc.RootElement.EnumerateArray().Select(e => new AsmTarget
            {
                Path = e.GetProperty("path").GetString(), TopLevel = e.GetProperty("top_level").GetBoolean(),
                Drawing = e.GetProperty("drawing").GetBoolean(), Main = e.GetProperty("main").GetBoolean(), ForceA0 = e.GetProperty("force_a0").GetBoolean(),
                TopLevelBom = e.TryGetProperty("top_level_bom", out var tb) && tb.GetBoolean(),
            }).ToList();
        }

        static void DoChecks(CadProject project, List<AsmTarget> targets)
        {
            // 1. 模型完整性: 逐个打开, 读加载警告、重建错误、浮动/缺失零部件
            int issues = Integrity.Models(null);
            Run.Check("模型完整性", issues == 0, issues == 0 ? "全部零件/装配体打开无错误、无重建警告" : issues + " 条问题", Path.Combine(Run.Checks, "integrity.md"));
            // 2. 装配体外形 + 干涉
            var itf = new StringBuilder();
            var res = targets.Select(t => Checks.Assembly(t.Path, t.TopLevel, itf, project.AcceptInterference)).ToList();
            Checks.Report(res, itf);
            // 3. BOM + 截图 (主装配体)
            var main = targets.Single(t => t.Main);
            Checks.Bom(main.Path, project.Title, project.BomExtras);
            Shots(project, main.Path);
            // 4. 项目专属
            project.ExtraChecks(targets);
        }

        static void Shots(CadProject project, string asm)
        {
            Sw.CloseAll();
            ModelDoc2 md = Sw.Open(asm);
            AssemblyDoc a = (AssemblyDoc)md;
            try { a.ResolveAllLightWeightComponents(false); } catch { }
            string nm = Path.GetFileNameWithoutExtension(asm);
            Checks.Shot(md, Path.Combine(Run.Images, nm + "_等轴测.png"), (int)swStandardViews_e.swIsometricView);
            Checks.Shot(md, Path.Combine(Run.Images, nm + "_主视图.png"), (int)swStandardViews_e.swFrontView);
            Checks.Shot(md, Path.Combine(Run.Images, nm + "_俯视图.png"), (int)swStandardViews_e.swTopView);
            Checks.Shot(md, Path.Combine(Run.Images, nm + "_背面等轴测.png"), 0, true);
            var hide = project.InteriorHide;
            if (hide != null && Checks.Hide(a, hide, true) > 0)
            {
                Checks.Shot(md, Path.Combine(Run.Images, nm + "_内部结构.png"), (int)swStandardViews_e.swIsometricView);
                Checks.Hide(a, hide, false);
            }
            Sw.CloseAll();
        }

        static void DoDrawings(CadProject project, List<AsmTarget> targets, string filter)
        {
            project.ConfigureDrawings();
            Drw.ForceA0.Clear(); Drw.TopLevelBom.Clear();
            foreach (var t in targets.Where(t => t.ForceA0)) Drw.ForceA0.Add(t.Path);
            foreach (var t in targets.Where(t => t.TopLevelBom)) Drw.TopLevelBom.Add(t.Path);
            Drw.All(filter, targets.Where(t => t.Drawing).Select(t => t.Path));
            Drw.FormatSheets();
            int n = Directory.Exists(Run.Drawings) ? Directory.GetFiles(Run.Drawings, "*.SLDDRW", SearchOption.AllDirectories).Length : 0;
            int pdf = Directory.Exists(Run.Drawings) ? Directory.GetFiles(Run.Drawings, "*.pdf", SearchOption.AllDirectories)
                .Count(f => !f.Contains(Path.DirectorySeparatorChar + "_formats" + Path.DirectorySeparatorChar)) : 0;
            // 没有任何尺寸的图纸 (图纸目录备注 "待补尺寸") 不算生成成功
            string idx = Path.Combine(Run.Drawings, "index.json");
            var noDims = new List<string>();
            if (File.Exists(idx))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(idx));
                noDims = doc.RootElement.EnumerateArray().Where(r => r.GetProperty("remark").GetString() == Drw.NeedsDims)
                            .Select(r => r.GetProperty("file").GetString()).ToList();
            }
            Run.Check("工程图生成", Drw.Failed == 0 && n > 0 && pdf == n && noDims.Count == 0,
                string.Format("{0} 张 SLDDRW / {1} 份 PDF, 失败 {2}, 无尺寸 {3}{4}", n, pdf, Drw.Failed, noDims.Count,
                    noDims.Count > 0 ? " (" + string.Join(", ", noDims.Take(3)) + ")" : ""),
                Path.Combine(Run.Drawings, "图纸目录.md"));
            int bad = Integrity.Drawings(null);
            Run.Check("工程图参考", bad == 0, bad == 0 ? "重新打开无参考警告" : bad + " 张有参考警告");
        }

        static void DoExport(List<AsmTarget> targets)
        {
            var main = targets.Single(t => t.Main);
            string step = Export.Step(main.Path);
            string summary;
            bool ok = StepCheck.Verify(step, main.Path, out summary);
            Run.Check("STEP 身份与内容", ok, summary, step);
            Run.Metric("STEP/实体数", StepCheck.Breps(File.ReadAllText(step, Encoding.Latin1)));
        }

        static void DoPost(CadProject project)
        {
            string py = Path.Combine(Run.PlatformRoot, "python");
            // 图纸版面审核 (超出图框 / 压标题栏 / 文字重叠 / 图线压技术要求)
            string auditJson = Path.Combine(Run.Checks, "drawing_audit.json");
            int rc = Python(Path.Combine(py, "audit_drawings.py"), Q(Run.Drawings) + " --json " + Q(auditJson));
            if (rc == 0 && File.Exists(auditJson))
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(auditJson)))
                {
                    int total = doc.RootElement.GetProperty("total").GetInt32(), probs = doc.RootElement.GetProperty("problem_count").GetInt32();
                    Run.Check("图纸版面审核", probs == 0, string.Format("{0} 张, 问题 {1} 张", total, probs), auditJson);
                }
                // 外形尺寸: 同一脚本按图纸目录记录的位置从 PDF 读出标注值, 写入图纸目录 (回归基准比对) 并与视图几何核对
                int unread; List<string> wrong;
                int ne = Drw.ApplyExtentReadings(auditJson, out unread, out wrong);
                Run.Check("外形尺寸读数与几何一致", unread == 0 && wrong.Count == 0,
                    string.Format("{0} 个外形尺寸 (从 PDF 读出), 读不出 {1}, 与视图几何不符 {2}{3}", ne, unread, wrong.Count,
                        wrong.Count > 0 ? ": " + string.Join("; ", wrong.Take(3)) : ""), auditJson);
            }
            else
            {
                Run.Check("图纸版面审核", false, "审核脚本失败 (退出码 " + rc + ")");
                Run.Check("外形尺寸读数与几何一致", false, "审核脚本失败, 未从 PDF 读出外形尺寸 (退出码 " + rc + ")");
            }
            // 全套图纸合订 PDF (目录 + 书签)
            rc = Python(Path.Combine(py, "merge_drawings.py"), Q(Run.Dir));
            Run.Check("图纸合订", rc == 0, rc == 0 ? "exports/ 下全套图纸 PDF" : "合订脚本失败 (退出码 " + rc + ")");
        }

        static string Q(string s) { return "\"" + s + "\""; }

        static int Python(string script, string args)
        {
            var psi = new ProcessStartInfo(Env.Python, Q(script) + " " + args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            using var p = Process.Start(psi);
            string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            foreach (string l in (o + e).Split('\n')) if (l.Trim() != "") Console.WriteLine("  [py] " + l.TrimEnd());
            return p.ExitCode;
        }
    }
}
