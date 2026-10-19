using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using CadKernel;

namespace CadCli
{
    /// <summary>
    /// cad run    &lt;input 目录&gt; [--from 步骤] [--drawings 过滤]   新建一次运行 (output/&lt;时间&gt;_&lt;任务&gt;/)
    /// cad resume &lt;运行目录&gt;  [--from 步骤] [--drawings 过滤]   在已有运行目录上续跑
    /// cad baseline &lt;运行目录&gt;                                    把该次结果写成回归基准 input/&lt;任务&gt;/expected.json
    /// cad import &lt;STEP&gt; [--task 名] [--force]                     STEP → input/&lt;任务&gt;/import.json + geometry/ (之后 cad run)
    /// cad list                                                  设计项目、输入任务、最近运行
    /// cad doctor                                                 环境检查 (不启动 SolidWorks)
    /// cad clean                                                  关闭专用实例中 output/ 下的文档
    /// cad diag   &lt;文件&gt;                                          特征树 + 包围盒 + 体积
    /// 步骤: parts assembly checks drawings export post
    /// </summary>
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                string root = Env.FindPlatformRoot(AppContext.BaseDirectory);
                Env.Load(root);
                Run.Init(root);
                string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
                string Opt(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
                switch (cmd)
                {
                    case "run":
                    {
                        if (args.Length < 2) return Usage();
                        string dir = ResolveInput(root, args[1]);
                        var input = DesignInput.Load(dir);
                        return Pipeline.Execute(FindProject(input.Project), input, null, Opt("--from"), Opt("--drawings"));
                    }
                    case "resume":
                    {
                        if (args.Length < 2) return Usage();
                        string runDir = RunDir(root, args[1]);
                        var input = DesignInput.Load(Path.Combine(runDir, "input"));
                        input.Task = ReadTask(runDir) ?? input.Task;
                        return Pipeline.Execute(FindProject(input.Project), input, runDir, Opt("--from") ?? "drawings", Opt("--drawings"));
                    }
                    case "list": return List(root);
                    case "doctor": return Doctor(root);
                    case "baseline":
                        if (args.Length < 2) return Usage();
                        return MakeBaseline(root, RunDir(root, args[1]));
                    case "import":
                        if (args.Length < 2) return Usage();
                        return ImportStep(root, args[1], Opt("--task"), args.Contains("--force"));
                    case "probe-prism":
                    {
                        // 调试: 对一个零件几何文件逐实体做 2.5D 识别并打印截面 (不写任何文件)
                        if (args.Length < 2) return Usage();
                        Sw.Connect();
                        try
                        {
                            var m = Sw.LoadGeometry(Path.GetFullPath(args[1]));
                            int i = 0, ok = 0;
                            foreach (SolidWorks.Interop.sldworks.Body2 b in (object[])((SolidWorks.Interop.sldworks.PartDoc)m).GetBodies2(0, true))
                            {
                                var trace = new List<string>();
                                string why;
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                var p = Prism.Recognize(b, out why, trace);
                                trace.Add(string.Format("识别 {0:F1} s", sw.Elapsed.TotalSeconds));
                                if (p != null && (p.Cuts != null || p.Holes != null))
                                {
                                    // 与导入时相同: 带斜口 / 横孔的实际建一次核对
                                    sw.Restart();
                                    string w = Prism.Verify(p, b);
                                    trace.Add(string.Format("重建核对: {0} ({1:F1} s)", w ?? "与原实体一致", sw.Elapsed.TotalSeconds));
                                    if (w != null) { why = "带斜口 / 横孔, 重建后与原实体不符"; p = null; }
                                }
                                if (p != null) ok++;
                                if (args.Length < 3 || args[2] == "all" || (p == null) == (args[2] == "fail"))
                                {
                                    var bb = ((double[])b.GetBodyBox()).Select(v => v / Sw.MM).ToArray();
                                    Console.WriteLine(string.Format("实体 {0}: {1}  [{2:F1} × {3:F1} × {4:F1} @ {5:F1},{6:F1},{7:F1}]", ++i, p != null ? "可重建" : why,
                                        bb[3] - bb[0], bb[4] - bb[1], bb[5] - bb[2], bb[0], bb[1], bb[2]));
                                    foreach (string t in trace) Console.WriteLine("    " + t);
                                }
                                else i++;
                            }
                            Console.WriteLine("可重建 " + ok + "/" + i);
                            Sw.App.CloseDoc(m.GetTitle());
                        }
                        finally { Sw.Disconnect(); }
                        return 0;
                    }
                    case "probe-faces":
                    {
                        // 调试: 逐实体打印面的构成 (平面按法向分组列出位置, 圆柱按轴向与半径, 其它曲面的类型、范围、面积); 实体序号可选 (从 1 起)。
                        // 再给 @x0,y0,z0,x1,y1,z1 时, 逐个列出包围盒与该区域相交的面 (类型、参数、范围、面积)
                        if (args.Length < 2) return Usage();
                        var pick = new HashSet<int>(args.Skip(2).Where(a => !a.StartsWith("@")).Select(int.Parse));
                        var region = args.Skip(2).Where(a => a.StartsWith("@")).Select(a => a.Substring(1).Split(',').Select(double.Parse).ToArray()).FirstOrDefault();
                        Sw.Connect();
                        try
                        {
                            var m = Sw.LoadGeometry(Path.GetFullPath(args[1]));
                            int i = 0;
                            foreach (SolidWorks.Interop.sldworks.Body2 b in (object[])((SolidWorks.Interop.sldworks.PartDoc)m).GetBodies2(0, true))
                            {
                                i++;
                                if (pick.Count > 0 && !pick.Contains(i)) continue;
                                var bb = ((double[])b.GetBodyBox()).Select(v => v / Sw.MM).ToArray();
                                if (region != null && Enumerable.Range(0, 3).Any(k => bb[k + 3] < region[k] || bb[k] > region[k + 3])) continue;   // 实体顺序每次载入都可能不同, 按区域选
                                var faces = ((object[])b.GetFaces()).Cast<SolidWorks.Interop.sldworks.Face2>().ToList();
                                Console.WriteLine(string.Format("实体 {0}: 面 {1} 个, 体积 {2:F0} mm³  [{3:F1} × {4:F1} × {5:F1} @ {6:F1},{7:F1},{8:F1}]", i, faces.Count,
                                    ((double[])b.GetMassProperties(1.0))[3] / 1e-9, bb[3] - bb[0], bb[4] - bb[1], bb[5] - bb[2], bb[0], bb[1], bb[2]));
                                var planes = new List<Tuple<string, double, double>>();     // 法向, 位置, 面积
                                var cyls = new List<string>();
                                if (region != null)
                                {
                                    foreach (var f in faces)
                                    {
                                        var fb = ((double[])f.GetBox()).Select(v => v / Sw.MM).ToArray();
                                        if (Enumerable.Range(0, 3).Any(k => fb[k + 3] < region[k] || fb[k] > region[k + 3])) continue;
                                        var s = (SolidWorks.Interop.sldworks.Surface)f.GetSurface();
                                        string desc = s.IsPlane() ? "平面 " + string.Join(",", ((double[])s.PlaneParams).Select((x, k) => (k < 3 ? x : x / Sw.MM).ToString("F3")))
                                            : s.IsCylinder() ? "圆柱 " + string.Join(",", ((double[])s.CylinderParams).Select((x, k) => (k >= 3 && k < 6 ? x : x / Sw.MM).ToString("F3")))
                                            : s.IsTorus() ? "圆环面" : s.IsCone() ? "圆锥面" : s.IsParametric() ? "样条面" : "其它曲面";
                                        Console.WriteLine(string.Format("    {0}  面积 {1:F2}  [{2:F2}..{3:F2}, {4:F2}..{5:F2}, {6:F2}..{7:F2}]  边 {8}", desc, f.GetArea() / 1e-6,
                                            fb[0], fb[3], fb[1], fb[4], fb[2], fb[5], f.GetEdgeCount()));
                                    }
                                    continue;
                                }
                                foreach (var f in faces)
                                {
                                    var s = (SolidWorks.Interop.sldworks.Surface)f.GetSurface();
                                    double area = f.GetArea() / 1e-6;
                                    if (s.IsPlane())
                                    {
                                        var q = (double[])s.PlaneParams;
                                        var n = new[] { q[0], q[1], q[2] };
                                        int sg = n.Select(Math.Abs).ToList().IndexOf(n.Select(Math.Abs).Max());
                                        if (n[sg] < 0) n = n.Select(x => -x).ToArray();
                                        double off = (n[0] * q[3] + n[1] * q[4] + n[2] * q[5]) / Sw.MM;
                                        planes.Add(Tuple.Create(string.Join(",", n.Select(x => x.ToString("F3"))), off, area));
                                    }
                                    else if (s.IsCylinder())
                                    {
                                        var q = (double[])s.CylinderParams;
                                        cyls.Add(string.Format("轴 {0:F3},{1:F3},{2:F3} R{3:F3}", q[3], q[4], q[5], q[6] / Sw.MM));
                                    }
                                    else
                                    {
                                        var fb = ((double[])f.GetBox()).Select(v => v / Sw.MM).ToArray();
                                        string t = s.IsTorus() ? "圆环面" : s.IsCone() ? "圆锥面" : s.IsSphere() ? "球面" : s.IsParametric() ? "样条面" : "其它曲面";
                                        Console.WriteLine(string.Format("    {0} 面积 {1:F1} mm²  [{2:F1}..{3:F1}, {4:F1}..{5:F1}, {6:F1}..{7:F1}]", t, area, fb[0], fb[3], fb[1], fb[4], fb[2], fb[5]));
                                    }
                                }
                                foreach (var g in planes.GroupBy(p => p.Item1).OrderByDescending(g => g.Sum(p => p.Item3)))
                                    Console.WriteLine("    平面 n=" + g.Key + ": " + string.Join("  ", g.OrderBy(p => p.Item2).Select(p => p.Item2.ToString("F2") + "(" + p.Item3.ToString("F0") + ")")));
                                foreach (var g in cyls.GroupBy(c => c).OrderByDescending(g => g.Count()))
                                    Console.WriteLine("    圆柱 " + g.Key + " ×" + g.Count());
                            }
                            Sw.App.CloseDoc(m.GetTitle());
                        }
                        finally { Sw.Disconnect(); }
                        return 0;
                    }
                    case "probe-sheet":
                    {
                        // 调试: 对一个零件几何文件逐实体做钣金识别, 识别出的在临时零件中实际建一次与原实体做差集 (不写任何文件)
                        if (args.Length < 2) return Usage();
                        Sw.Connect();
                        try
                        {
                            var m = Sw.LoadGeometry(Path.GetFullPath(args[1]));
                            int i = 0, ok = 0;
                            foreach (SolidWorks.Interop.sldworks.Body2 b in (object[])((SolidWorks.Interop.sldworks.PartDoc)m).GetBodies2(0, true))
                            {
                                var trace = new List<string>();
                                string why;
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                if (Prism.Recognize(b, out why) != null) { i++; continue; }          // 与导入时同序: 截面拉伸认得的不再试钣金
                                var d = SheetMetal.Recognize(b, out why, trace);
                                if (d != null)
                                {
                                    string w = Prism.Verify(mm => SheetMetal.Build(mm, d), b);
                                    trace.Add(string.Format("重建核对: {0} ({1:F1} s)", w ?? "与原实体一致", sw.Elapsed.TotalSeconds));
                                    if (w != null) { why = "钣金重建后与原实体不符"; d = null; }
                                }
                                if (d != null) ok++;
                                var bb = ((double[])b.GetBodyBox()).Select(v => v / Sw.MM).ToArray();
                                if (args.Length < 3 || args[2] == "all" || (d == null) == (args[2] == "fail"))
                                {
                                    Console.WriteLine(string.Format("实体 {0}: {1}  [{2:F1} × {3:F1} × {4:F1} @ {5:F1},{6:F1},{7:F1}]", ++i, d != null ? "可重建" : why,
                                        bb[3] - bb[0], bb[4] - bb[1], bb[5] - bb[2], bb[0], bb[1], bb[2]));
                                    foreach (string t in trace) Console.WriteLine("    " + t);
                                }
                                else i++;
                            }
                            Console.WriteLine("钣金可重建 " + ok + "/" + i);
                            Sw.App.CloseDoc(m.GetTitle());
                        }
                        finally { Sw.Disconnect(); }
                        return 0;
                    }
                    case "try-sheet":
                    {
                        // 调试: 取 import.json 中一个零件的钣金定义, 在临时零件中建出 (按步骤计时) 并与原实体做差集; 不写文件
                        if (args.Length < 3) return Usage();
                        string idir = Path.Combine(root, "input", args[1]);
                        var ip = ImportModel.Load(idir).Part(args[2]);
                        if (ip?.Model.Sheets == null) throw new Exception(args[2] + " 没有钣金定义");
                        Sw.Connect();
                        try
                        {
                            var orig = Sw.LoadGeometry(Path.Combine(idir, ip.Model.File));
                            foreach (var d in ip.Model.Sheets)
                            {
                                var ob = Prism.Find(orig, d) ?? throw new Exception("原几何中找不到这个钣金实体");
                                var sw = System.Diagnostics.Stopwatch.StartNew();
                                string w = Prism.Verify(mm => SheetMetal.Build(mm, d), ob);
                                Console.WriteLine(string.Format("钣金 ⟂{0} 上表面 {1}: {2} (共 {3:F0} s)", d.Axis, d.Top, w ?? "与原实体一致", sw.Elapsed.TotalSeconds));
                            }
                            Sw.App.CloseDoc(orig.GetTitle());
                        }
                        finally { Sw.Disconnect(); }
                        return 0;
                    }
                    case "try-rebuild":
                    {
                        // 调试: 按运行时同样的步骤在一个零件中重建 (载入原几何 → 删去识别出的实体 → 截面拉伸、钣金依次建出) 并逐实体比对; 不核对、不写文件
                        if (args.Length < 2) return Usage();
                        Sw.Connect();
                        try
                        {
                            string file = Path.GetFullPath(args[1]);
                            var orig = Sw.LoadGeometry(file);
                            var prisms = new List<PrismDef>(); var sheets = new List<SheetDef>();
                            foreach (SolidWorks.Interop.sldworks.Body2 b in (object[])((SolidWorks.Interop.sldworks.PartDoc)orig).GetBodies2(0, true))
                            {
                                // 与导入时相同: 带切除的截面拉伸与钣金先在临时零件中核对
                                string w;
                                var pr = Prism.Recognize(b, out w);
                                if (pr != null && ((pr.Cuts == null && pr.Holes == null) || Prism.Verify(pr, b) == null)) { prisms.Add(pr); continue; }
                                if (pr != null) continue;
                                var sh = SheetMetal.Recognize(b, out w);
                                if (sh != null && Prism.Verify(mm => SheetMetal.Build(mm, sh), b) == null) sheets.Add(sh);
                            }
                            var defs = prisms.Cast<IBodyDef>().Concat(sheets).ToList();
                            Console.WriteLine(string.Format("识别并核对: 截面拉伸 {0}, 钣金 {1}", prisms.Count, sheets.Count));
                            // 同一文件再载入会得到已打开的同一文档 → 复制成另一个文件名再载入
                            string copy = Path.Combine(Path.GetTempPath(), "try_rebuild_" + Path.GetFileName(file));
                            File.Copy(file, copy, true);
                            var built = Sw.LoadGeometry(copy);
                            var sw = System.Diagnostics.Stopwatch.StartNew();
                            Prism.RemoveRebuilt(built, defs);
                            Console.WriteLine("已删去被重建的原实体, 剩 " + Sw.BodyCount(built));
                            foreach (var pr in prisms) Prism.Build(built, pr);
                            Console.WriteLine("截面拉伸建完, 实体 " + Sw.BodyCount(built));
                            int n = 0;
                            foreach (var sh in sheets)
                            {
                                try { SheetMetal.Build(built, sh); }
                                catch (Exception e) { Console.WriteLine(string.Format("钣金 {0} (⟂{1} 上表面 {2}) 失败: {3}\n{4}", n + 1, sh.Axis, sh.Top, e.Message, e.StackTrace)); throw; }
                                Console.WriteLine(string.Format("钣金 {0} 建完, 实体 {1}", ++n, Sw.BodyCount(built)));
                            }
                            built.ForceRebuild3(false);
                            var orphan = Sw.OrphanSketches(built);
                            Console.WriteLine(string.Format("重建 {0:F0} s, 实体 {1}, 未消费草图 {2}", sw.Elapsed.TotalSeconds, Sw.BodyCount(built), orphan.Count == 0 ? "无" : string.Join(",", orphan)));
                            var cmp = Prism.Compare(orig, built, defs);
                            Console.WriteLine(string.Format("实体 {0}/{1} 一致: 重建 {2} 个, 最大差集 {3:F6} mm³; 保留原几何 {4} 个", cmp.Matched, cmp.Original, cmp.Rebuilt, cmp.MaxXorMm3, cmp.Kept));
                            foreach (string s in cmp.Problems) Console.WriteLine("  !! " + s);
                            Sw.App.CloseDoc(orig.GetTitle()); Sw.App.CloseDoc(built.GetTitle());
                            return cmp.Problems.Count == 0 ? 0 : 1;
                        }
                        finally { Sw.Disconnect(); }
                    }
                    case "compare-geometry":
                    {
                        // 调试: 导入任务中一个按实体重建的零件, 原几何与重建零件逐实体比对 —— 与 cad run 的"零件几何与原 STEP 一致"同一算法 (不写文件)
                        if (args.Length < 4) return Usage();
                        string idir = Path.Combine(root, "input", args[1]);
                        var ip = ImportModel.Load(idir).Part(args[2]);
                        if (ip == null || ip.Model.Type != "bodies") throw new Exception(args[2] + " 不是 input/" + args[1] + " 中按实体重建的零件");
                        Sw.Connect();
                        try
                        {
                            var orig = Sw.LoadGeometry(Path.Combine(idir, ip.Model.File));
                            var built = Sw.Open(Path.GetFullPath(args[3]));
                            var cmp = Prism.Compare(orig, built, ip.Model.Rebuilt());
                            Console.WriteLine(string.Format("实体 {0}/{1} 一致: 重建 {2} 个, 最大差集 {3:F6} mm³ (相对 {4:E1}); 保留原几何 {5} 个",
                                cmp.Matched, cmp.Original, cmp.Rebuilt, cmp.MaxXorMm3, cmp.MaxXorRel, cmp.Kept));
                            foreach (string s in cmp.Problems) Console.WriteLine("  !! " + s);
                            Sw.App.CloseDoc(orig.GetTitle());
                            Sw.Close(built);
                            return cmp.Problems.Count == 0 ? 0 : 1;
                        }
                        finally { Sw.Disconnect(); }
                    }
                    case "clean":
                        Sw.Connect(); Sw.CloseAll(); Sw.Disconnect();
                        Console.WriteLine("已关闭专用实例中 output/ 下的文档");
                        return 0;
                    case "diag":
                    {
                        if (args.Length < 2) return Usage();
                        Sw.Connect();
                        var m = Sw.Open(Path.GetFullPath(args[1]));
                        for (var f = (SolidWorks.Interop.sldworks.Feature)m.FirstFeature(); f != null; f = (SolidWorks.Interop.sldworks.Feature)f.GetNextFeature())
                            Console.WriteLine(string.Format("  {0,-22} {1}{2}", f.GetTypeName2(), f.Name,
                                ((bool[])f.IsSuppressed2((int)SolidWorks.Interop.swconst.swInConfigurationOpts_e.swThisConfiguration, null))[0] ? "  (压缩)" : ""));
                        if (m.GetType() == (int)SolidWorks.Interop.swconst.swDocumentTypes_e.swDocPART)
                            Console.WriteLine("  包围盒 " + Sw.BoxStr(Sw.PartBox(m)) + "  体积 " + Sw.Volume(m).ToString("F0") + " mm³  质量 " + Sw.GetProp(m, "质量"));
                        Sw.Close(m); Sw.Disconnect();
                        return 0;
                    }
                    case "dump":
                    {
                        if (args.Length < 2) return Usage();
                        Sw.Connect();
                        Drw.Dump(Path.GetFullPath(args[1]), args.Length > 2 ? int.Parse(args[2]) : 1, args.Length > 3 ? int.Parse(args[3]) : 12);
                        Sw.Disconnect();
                        return 0;
                    }
                    case "probe-asm":
                    {
                        // 调试: 按装配图流程重复出主/俯视图组 + 序号 + 明细表, 逐个注释输出位置读数, 读数异常的那张另存 (不写运行目录)
                        if (args.Length < 2) return Usage();
                        string outDir = Path.GetFullPath(Opt("--out") ?? Path.Combine(root, "output", "_probe_asm_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")));
                        Sw.Connect();
                        try
                        {
                            Drw.ProbeAsm(Path.GetFullPath(args[1]), Opt("--size") ?? "a2", double.Parse(Opt("--scale") ?? "10", System.Globalization.CultureInfo.InvariantCulture),
                                int.Parse(Opt("--n") ?? "10"), outDir, args.Contains("--top-bom"), Opt("--preload") == null ? null : Path.GetFullPath(Opt("--preload")));
                            return 0;
                        }
                        finally { Sw.CloseAll(); Sw.Disconnect(); }
                    }
                    default: return Usage();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERR] " + ex.Message);
                return 1;
            }
        }

        static int Usage()
        {
            Console.WriteLine("用法:");
            Console.WriteLine("  cad run <input/任务目录> [--from parts|assembly|checks|drawings|export|post] [--drawings 路径片段|asm]");
            Console.WriteLine("  cad resume <output/运行目录> [--from 步骤]   (默认从 drawings 续跑)");
            Console.WriteLine("  cad baseline <output/运行目录>              (看图确认无误后, 把该次结果写成 input/<任务>/expected.json)");
            Console.WriteLine("  cad import <STEP 文件> [--task 任务名] [--force]   (STEP → input/<任务>/import.json + geometry/, 之后 cad run <任务>)");
            Console.WriteLine("  cad list | doctor | clean | diag <文件> | dump <工程图> [视图序号] [记录数] | probe-asm <装配体> [--scale 10] [--size a2] [--n 10] [--top-bom] [--preload 装配体] [--out 目录] | probe-prism <x_t> [all|fail] | probe-sheet <x_t> [all|fail] | try-sheet <任务> <零件Id> | try-rebuild <x_t> | probe-faces <x_t> [@x0,y0,z0,x1,y1,z1] | compare-geometry <任务> <零件Id> <零件文件>");
            return 2;
        }

        /// <summary>运行目录: 绝对路径, 或 output/ 下的目录名; 也接受平台根相对路径 (output/xxx)</summary>
        static string RunDir(string root, string a)
        {
            if (Path.IsPathRooted(a)) return Path.GetFullPath(a);
            string p = Path.Combine(root, a);
            return Path.GetFullPath(Directory.Exists(p) && File.Exists(Path.Combine(p, "run.json")) ? p : Path.Combine(root, "output", a));
        }

        /// <summary>
        /// STEP → input/&lt;任务&gt;/: import.json (装配层级、实例位置、零件表, 可编辑) + geometry/*.x_t (各零件原几何) + design.json (project = imported)。
        /// 已有 import.json 时须加 --force (会覆盖对它的手工修改)。
        /// </summary>
        static int ImportStep(string root, string stepArg, string task, bool force)
        {
            string step = Path.GetFullPath(stepArg);
            if (!File.Exists(step)) throw new FileNotFoundException("找不到 STEP 文件: " + step);
            task = task ?? Run.Safe(Path.GetFileNameWithoutExtension(step));
            string dir = Path.Combine(root, "input", task);
            if (File.Exists(Path.Combine(dir, ImportModel.FileName)) && !force)
            {
                Console.WriteLine("input/" + task + "/" + ImportModel.FileName + " 已存在; 重新导入会覆盖其中的手工修改, 确认请加 --force");
                return 2;
            }
            Directory.CreateDirectory(dir);
            // 原几何先写临时目录, 导入全部成功后才替换 geometry/ 与 import.json —— 中途失败时旧数据原样保留
            string geo = Path.Combine(dir, "geometry"), stage = Path.Combine(dir, ".geometry_importing");
            Sw.Connect();
            try
            {
                // 专用实例对载入过的 x_t 所在目录持有句柄, 只有实例退出才释放 (见 Sw.Shutdown): 上次导入、调试或被中途结束的运行
                // 可能占着暂存目录 / geometry/ —— 删不掉就先退出实例
                if (Directory.Exists(stage))
                {
                    try { Directory.Delete(stage, true); }
                    catch (IOException) { Sw.Shutdown(); Sw.Connect(); Directory.Delete(stage, true); }
                }
                ImportModel model;
                try { model = StepImport.FromStep(step, stage); }
                catch { try { Sw.Shutdown(); Directory.Delete(stage, true); } catch { } throw; }
                Sw.Shutdown();            // 换 geometry/ 之前退出实例, 释放它对新旧几何目录的句柄
                SwapDir(stage, geo);
                var problems = model.Validate(dir).Concat(model.Warnings).ToList();
                File.WriteAllText(Path.Combine(dir, ImportModel.FileName), model.ToJson(), new UTF8Encoding(false));
                string dj = Path.Combine(dir, "design.json");
                if (!File.Exists(dj))
                    File.WriteAllText(dj, "{\n  // 由 cad import 生成: 零件、装配与位置见 import.json, 原几何在 geometry/ (不入库, 可由源 STEP 重新导入)\n" +
                        "  \"project\": \"imported\",\n  \"title\": " + JsonSerializer.Serialize(model.Asm(model.Root).Name,
                        new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + ",\n  \"params\": {}\n}\n", new UTF8Encoding(false));
                long bytes = Directory.GetFiles(geo).Sum(f => new FileInfo(f).Length);
                Console.WriteLine();
                Console.WriteLine(string.Format("已写入 input/{0}/: import.json, geometry/ ({1} 个文件, {2:F1} MB)", task, Directory.GetFiles(geo).Length, bytes / 1048576.0));
                foreach (var g in model.Parts.GroupBy(p => p.Kind + (p.Model.Type == "tube" ? " (直管, 参数化)" : "")))
                    Console.WriteLine(string.Format("  {0,-18} {1,3} 种 {2,4} 件", g.Key, g.Count(), g.Sum(p => model.World[p.Id].Count)));
                foreach (string p in problems) Console.WriteLine("  !! " + p);
                Console.WriteLine("下一步: cad run " + task);
                return problems.Count == 0 ? 0 : 1;
            }
            finally { Sw.Disconnect(); }
        }

        /// <summary>
        /// 用 fresh 目录替换 target: 先把旧目录改名备份, 再换入新目录, 最后删备份; 换入失败则把备份放回。
        /// 刚写出的文件可能正被杀毒软件扫描, 每步重试 10 次、间隔 1 秒 (SolidWorks 停在目录里造成的占用重试无效, 调用前须 Sw.ReleaseDir)。
        /// </summary>
        static void SwapDir(string fresh, string target)
        {
            string backup = target + ".old";
            Action<Action> retry = act =>
            {
                for (int i = 1; ; i++)
                {
                    try { act(); return; }
                    catch (IOException) when (i < 10) { System.Threading.Thread.Sleep(1000); }
                    catch (UnauthorizedAccessException) when (i < 10) { System.Threading.Thread.Sleep(1000); }
                }
            };
            if (Directory.Exists(backup)) retry(() => Directory.Delete(backup, true));
            if (Directory.Exists(target))
            {
                try { retry(() => Directory.Move(target, backup)); }
                catch (IOException)
                {
                    // 目录本身被占用 (实测 2026-10-16: 中途被结束的运行没来得及断开, 专用实例的当前目录停在这里) —— 锁的只是目录,
                    // 里面的文件可以换: 退为逐个文件替换 (新几何已在 fresh 中完整写好)
                    Console.WriteLine("  (" + target + " 被占用, 改为逐个文件替换)");
                    foreach (string f in Directory.GetFiles(target)) File.Delete(f);
                    foreach (string f in Directory.GetFiles(fresh)) File.Move(f, Path.Combine(target, Path.GetFileName(f)));
                    Directory.Delete(fresh, true);
                    return;
                }
            }
            try { retry(() => Directory.Move(fresh, target)); }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(target)) Directory.Move(backup, target);
                throw;
            }
            if (Directory.Exists(backup)) try { retry(() => Directory.Delete(backup, true)); } catch { Console.WriteLine("  (旧几何备份未能删除: " + backup + ")"); }
        }

        /// <summary>
        /// 由一次运行生成回归基准。只接受全部步骤完成、除"回归基准"外全部校核通过的运行;
        /// 已有基准时先列出新旧差异, 便于在提交说明里写明这次结果为什么变了。
        /// </summary>
        static int MakeBaseline(string root, string runDir)
        {
            string mf = Path.Combine(runDir, "run.json");
            if (!File.Exists(mf)) throw new FileNotFoundException("不是运行目录 (缺 run.json): " + runDir);
            var m = JsonSerializer.Deserialize<RunManifest>(File.ReadAllText(mf), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
            var bad = m.Checks.Where(c => !c.Passed && c.Name != Baseline.CheckName).Select(c => c.Name).ToList();
            var notRun = Pipeline.AllSteps.Length - m.Steps.Count(s => s.Status == "passed");
            if (m.Error != null || bad.Count > 0 || notRun > 0)
            {
                Console.WriteLine("该运行不能作为基准: " + (m.Error ?? "") + (bad.Count > 0 ? " 未通过的校核: " + string.Join(", ", bad) : "")
                                  + (notRun > 0 ? " 有 " + notRun + " 个步骤未完成" : ""));
                return 1;
            }
            string inputDir = Path.Combine(root, "input", m.Task);
            if (!File.Exists(Path.Combine(inputDir, "design.json"))) throw new DirectoryNotFoundException("找不到输入任务 input/" + m.Task);
            string idx = Path.Combine(runDir, "drawings", "index.json");
            string indexJson = File.Exists(idx) ? File.ReadAllText(idx) : null;
            string target = Path.Combine(inputDir, Baseline.FileName);
            if (File.Exists(target))
            {
                var diffs = Baseline.Compare(Baseline.Parse(File.ReadAllText(target)), m, indexJson);
                Console.WriteLine(diffs.Count == 0 ? "与现有基准一致" : "相对现有基准的变化 " + diffs.Count + " 处:");
                foreach (string d in diffs) Console.WriteLine("  - " + d);
            }
            File.WriteAllText(target, Baseline.Generate(m, indexJson, Path.GetFileName(runDir)), new UTF8Encoding(false));
            Console.WriteLine("已写入 " + Path.GetRelativePath(root, target));
            return 0;
        }

        static string ResolveInput(string root, string a)
        {
            if (Directory.Exists(a)) return Path.GetFullPath(a);
            string p = Path.Combine(root, "input", a);
            if (Directory.Exists(p)) return p;
            throw new DirectoryNotFoundException("找不到输入目录: " + a);
        }

        static string ReadTask(string runDir)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")));
            return doc.RootElement.TryGetProperty("task", out var t) ? t.GetString() : null;
        }

        static IEnumerable<CadProject> Projects()
        {
            var asm = typeof(CadProjects.PlanetaryGear.PlanetaryGearbox).Assembly;
            return asm.GetTypes().Where(t => !t.IsAbstract && typeof(CadProject).IsAssignableFrom(t))
                      .Select(t => (CadProject)Activator.CreateInstance(t)).OrderBy(p => p.Id);
        }

        static CadProject FindProject(string id)
        {
            var p = Projects().FirstOrDefault(q => q.Id == id);
            if (p == null) throw new Exception("未知设计项目: " + id + " (可用: " + string.Join(", ", Projects().Select(q => q.Id)) + ")");
            return p;
        }

        static int List(string root)
        {
            Console.WriteLine("设计项目 (src/CadProjects):");
            foreach (var p in Projects()) Console.WriteLine(string.Format("  {0,-18} {1}", p.Id, p.Description));
            Console.WriteLine();
            Console.WriteLine("输入任务 (input/):");
            foreach (string d in Directory.GetDirectories(Path.Combine(root, "input")).OrderBy(x => x))
                if (File.Exists(Path.Combine(d, "design.json")))
                {
                    var i = DesignInput.Load(d);
                    Console.WriteLine(string.Format("  {0,-22} project={1}  {2}", Path.GetFileName(d), i.Project, i.Title));
                }
            Console.WriteLine();
            Console.WriteLine("最近运行 (output/):");
            foreach (string d in Directory.GetDirectories(Path.Combine(root, "output")).OrderByDescending(x => x).Take(10))
            {
                string f = Path.Combine(d, "run.json");
                if (!File.Exists(f)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                Console.WriteLine(string.Format("  {0,-40} {1,-8} {2}", Path.GetFileName(d), r.GetProperty("status").GetString(),
                    r.TryGetProperty("title", out var t) ? t.GetString() : ""));
            }
            return 0;
        }

        /// <summary>环境检查: 只读, 不启动 SolidWorks</summary>
        static int Doctor(string root)
        {
            int bad = 0;
            void Row(bool ok, string what, string detail, bool warn = false)
            {
                if (!ok) bad++;
                Console.WriteLine(string.Format("  {0} {1,-26} {2}", !ok ? "✗" : warn ? "!" : "✓", what, detail));
            }
            Console.WriteLine("平台根目录: " + root);
            Row(File.Exists(Env.SolidWorksExe), "SolidWorks 可执行文件", Env.SolidWorksExe);
            Row(File.Exists(Env.MaterialDb), "SolidWorks 材料库", Env.MaterialDb);
            // 与内核相同的来源: SolidWorks 用户选项中的默认零件模板, 工程图模板取同一目录
            string partTpl = Env.DefaultPartTemplate();
            Row(partTpl != null && File.Exists(partTpl), "默认零件模板 (用户选项)", partTpl ?? "注册表中读不到");
            if (partTpl != null)
            {
                string tpl = Path.GetDirectoryName(partTpl);
                foreach (string t in new[] { "gb_a3.drwdot", "gb_a2.drwdot", "gb_a0.drwdot" })
                    Row(File.Exists(Path.Combine(tpl, t)), "工程图模板 " + t, Path.Combine(tpl, t));
            }
            string bom = Path.Combine(Env.SolidWorksDir, @"lang\chinese-simplified\gb-bom-material.sldbomtbt");
            Row(File.Exists(bom), "GB 明细表模板", bom);
            var asm = typeof(SolidWorks.Interop.sldworks.SldWorks).Assembly.GetName();
            Row(true, "SolidWorks 类型库 (PIA)", asm.Name + " " + asm.Version);
            try
            {
                var psi = new ProcessStartInfo(Env.Python, "-c \"import pymupdf, sys; print(sys.version.split()[0], pymupdf.__version__)\"")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = Process.Start(psi);
                string o = p.StandardOutput.ReadToEnd().Trim(), e = p.StandardError.ReadToEnd().Trim();
                p.WaitForExit();
                // WindowsApps 下的 python.exe 应用执行别名可能排在 PATH 前面且什么都不输出: 在 config/cad.json 写解释器完整路径
                Row(p.ExitCode == 0, "Python + PyMuPDF", p.ExitCode == 0 ? o
                    : e != "" ? e.Split('\n').Last() : Env.Python + " 退出码 " + p.ExitCode + " (无输出; 在 config/cad.json 的 python 写解释器完整路径)");
            }
            catch (Exception ex) { Row(false, "Python + PyMuPDF", ex.Message); }
            Row(true, "专用 SolidWorks 实例", Sw.InstanceStatus());
            // 模型路径长度 (见 PathLimit): 按每个输入任务在本平台目录下新建一次运行估算; ≥ MAX_PATH 时出图会挂起
            var paths = new List<KeyValuePair<string, PathLimit.Estimate>>();
            foreach (string d in Directory.GetDirectories(Path.Combine(root, "input")).OrderBy(x => x))
            {
                if (!File.Exists(Path.Combine(d, "design.json"))) continue;
                try
                {
                    var input = DesignInput.Load(d);
                    var project = FindProject(input.Project);
                    project.Bind(input);
                    paths.Add(new KeyValuePair<string, PathLimit.Estimate>(input.Task, PathLimit.ForRun(project, input.Task)));
                }
                catch (Exception ex) { Row(true, "模型路径 " + Path.GetFileName(d), "无法估算: " + ex.Message, true); }
            }
            if (paths.Count > 0)
            {
                var worst = paths.OrderByDescending(x => x.Value.Bytes).First();
                var level = worst.Value.Level;
                Row(level != PathLimit.Level.Over, "模型路径长度 (MAX_PATH)", worst.Key + " " + PathLimit.Describe(worst.Value), level == PathLimit.Level.Unverified);
                foreach (var x in paths.Where(x => x.Key != worst.Key && x.Value.Level != PathLimit.Level.Ok))
                    Console.WriteLine(string.Format("      {0}: {1} 字节{2}", x.Key, x.Value.Bytes, x.Value.Level == PathLimit.Level.Over ? " (≥ MAX_PATH)" : ""));
                foreach (string s in PathLimit.Advice(worst.Value, root, false)) Console.WriteLine("      " + s);
            }
            Console.WriteLine(bad == 0 ? "环境就绪" : bad + " 项未就绪");
            return bad == 0 ? 0 : 1;
        }
    }
}
