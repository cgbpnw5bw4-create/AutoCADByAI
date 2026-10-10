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
                            Console.WriteLine(string.Format("  {0,-22} {1}", f.GetTypeName2(), f.Name));
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
            Console.WriteLine("  cad list | doctor | clean | diag <文件> | dump <工程图> [视图序号] [记录数]");
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
            void Row(bool ok, string what, string detail) { if (!ok) bad++; Console.WriteLine(string.Format("  {0} {1,-26} {2}", ok ? "✓" : "✗", what, detail)); }
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
            Console.WriteLine(bad == 0 ? "环境就绪" : bad + " 项未就绪");
            return bad == 0 ? 0 : 1;
        }
    }
}
