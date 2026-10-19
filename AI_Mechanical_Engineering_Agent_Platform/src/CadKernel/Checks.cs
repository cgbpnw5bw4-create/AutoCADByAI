using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 装配校核与物料清单。来源: CFC-650 Report.cs (干涉检查兼作孔位校核, 首轮在该设备上检出 1030 处真实问题后修正为 0)。
    /// 原理: 每个螺栓都真实穿过被连接件的孔, 螺纹孔按公称直径建模 —— 漏孔、错位、孔小、螺栓顶底都会报干涉。
    /// </summary>
    public static class Checks
    {
        static readonly Encoding U8B = new UTF8Encoding(true), U8 = new UTF8Encoding(false);

        public sealed class AsmResult
        {
            public string Name, File;
            public int Parts, Interferences;
            /// <summary>其中属于"已登记接受"的 (设计项目 AcceptInterference 认可的, 如导入设计原模型自带的干涉)</summary>
            public int Accepted;
            public double L, H, W;
            public bool Failed;
            public string Error;
        }

        /// <summary>
        /// 一处干涉: 两个零部件 (文件路径、文档名、实例名)、B 相对 A 的位置、干涉体积。
        /// B 相对 A 的位置与所在装配层级无关 —— 同一处干涉在总装和模块里检查时相同, 用于识别已登记的干涉。
        /// </summary>
        public sealed class Interf
        {
            public string PathA, PathB, TitleA, TitleB, NameA, NameB;
            public Xf Rel;
            public double VolumeMm3;
        }

        /// <summary>由 SolidWorks 干涉对象取明细; 不是恰好两个零部件时 Rel 为 null (不会被认作已知干涉)</summary>
        public static Interf Describe(Interference it)
        {
            var d = new Interf { VolumeMm3 = it.Volume / (Sw.MM * Sw.MM * Sw.MM) };
            object[] cps = (object[])it.Components ?? new object[0];
            if (cps.Length != 2) { d.NameA = string.Join(" / ", cps.Cast<Component2>().Select(c => c.Name2)); d.NameB = ""; return d; }
            var a = (Component2)cps[0]; var b = (Component2)cps[1];
            Func<Component2, Xf> world = c => Xf.FromArray((double[])((MathTransform)c.GetTotalTransform(false)).ArrayData);
            var ma = a.GetModelDoc2() as ModelDoc2; var mb = b.GetModelDoc2() as ModelDoc2;
            d.PathA = a.GetPathName(); d.PathB = b.GetPathName();
            d.TitleA = ma == null ? null : ma.GetTitle(); d.TitleB = mb == null ? null : mb.GetTitle();
            d.NameA = a.Name2; d.NameB = b.Name2;
            d.Rel = Xf.Relative(world(b), world(a));
            return d;
        }

        /// <summary>
        /// 干涉检查 (重合面/相切接触不计; topLevel = 子装配视为整体)。运行校核与 cad import 登记原模型干涉共用, 判据一致。
        /// 检查本身出错时抛异常 —— 调用方不得当作 0 处。
        /// </summary>
        public static List<Interf> Detect(AssemblyDoc a, bool topLevel)
        {
            InterferenceDetectionMgr idm = a.InterferenceDetectionManager;
            idm.TreatCoincidenceAsInterference = false;
            idm.TreatSubAssembliesAsComponents = topLevel;
            idm.IncludeMultibodyPartInterferences = false;
            idm.MakeInterferingPartsTransparent = false;
            idm.UseTransform = false;
            var list = new List<Interf>();
            foreach (object o in (object[])idm.GetInterferences() ?? new object[0]) list.Add(Describe((Interference)o));
            idm.Done();
            return list;
        }

        /// <summary>
        /// 打开装配体: 外形 (GetBox)、零件数 (含紧固件)、干涉 (重合面不计)。
        /// topLevel = 子装配视为整体 (只查模块之间)。明细追加到 md。
        /// accept 非空时, 它认可的干涉记为"已登记接受" (仍逐条列出), 只有其余的算新增。
        /// </summary>
        public static AsmResult Assembly(string path, bool topLevel, StringBuilder md, Func<Interf, bool> accept = null)
        {
            var r = new AsmResult { File = path, Name = Path.GetFileNameWithoutExtension(path) };
            Sw.CloseAll();
            ModelDoc2 m = Sw.Open(path);
            AssemblyDoc a = (AssemblyDoc)m;
            // 零部件 > 500 按大型装配体模式轻化打开 → 还原后再检查 (轻化零件无几何、属性读不到)
            try { a.ResolveAllLightWeightComponents(false); } catch { }
            object[] cs = (object[])a.GetComponents(false);
            r.Parts = cs == null ? 0 : cs.Count(o => (((Component2)o).GetPathName() ?? "").ToLower().EndsWith(".sldprt"));
            double[] b = (double[])a.GetBox(0);
            r.L = (b[3] - b[0]) / Sw.MM; r.H = (b[4] - b[1]) / Sw.MM; r.W = (b[5] - b[2]) / Sw.MM;
            Sw.Log(string.Format("{0,-36} 零件 {1,5}  {2:F0}×{3:F0}×{4:F0}", r.Name, r.Parts, r.L, r.H, r.W));
            r.Interferences = Interference(a, topLevel, md, r.Name, accept, out r.Accepted, out r.Failed, out r.Error);
            Sw.CloseAll();
            return r;
        }

        static int Interference(AssemblyDoc a, bool topLevel, StringBuilder sb, string title, Func<Interf, bool> accept,
                                out int accepted, out bool failed, out string error)
        {
            failed = false; error = null; accepted = 0;
            try
            {
                var list = Detect(a, topLevel);
                int n = list.Count;
                var rows = new List<string>();
                int logged = 0;
                for (int i = 0; i < n; i++)
                {
                    Interf d = list[i];
                    bool ok = accept != null && d.Rel != null && accept(d);
                    if (ok) accepted++;
                    string c1 = d.NameA, c2 = d.NameB;
                    double v = d.VolumeMm3;
                    // 新增的全部列出; 已登记接受的最多列 60 条 (数量照实统计)
                    if (!ok || rows.Count < 60) rows.Add(string.Format("| {0} | {1:F1} | {2} | {3} | {4} |", i + 1, v, c1, c2, ok ? "原模型已有, 已登记接受" : "**新增**"));
                    if (!ok && logged++ < 60) Sw.Log(string.Format("   !! 干涉 {0:F1}mm³  {1}  <->  {2}", v, c1, c2));
                }
                sb.AppendLine("## " + title + (topLevel ? " (模块间)" : "") + " —— " + n + " 处" + (accept != null ? string.Format(" (已登记接受 {0}, 新增 {1})", accepted, n - accepted) : ""));
                sb.AppendLine();
                if (n > 0)
                {
                    sb.AppendLine("| 序号 | 干涉体积 (mm³) | 零部件 A | 零部件 B | 说明 |");
                    sb.AppendLine("|---:|---:|---|---|---|");
                    foreach (string s in rows) sb.AppendLine(s);
                    sb.AppendLine();
                }
                if (accepted > 0) Sw.Log(string.Format("   {0}: 干涉 {1} 处, 其中原模型已有并已登记接受 {2} 处", title, n, accepted));
                return n;
            }
            catch (Exception ex)
            {
                // 检查本身失败不能当作 0 处干涉
                failed = true; error = ex.Message;
                Sw.Log("!! 干涉检查失败 " + title + ": " + ex.Message);
                sb.AppendLine("## " + title + " —— 检查失败: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// 写 checks/assemblies.md 与 checks/interference.md, 登记干涉校核结论;
        /// 零件数与外形登记为指标 (装配/&lt;名称&gt;/…), 由回归基准比对 —— 它们没有"对错"的绝对判据, 只能和已确认的结果比。
        /// </summary>
        public static void Report(List<AsmResult> results, StringBuilder itf)
        {
            var dims = new StringBuilder();
            dims.AppendLine("# 装配体几何校核");
            dims.AppendLine();
            dims.AppendLine("> 由 `cad run` 自动生成 (IAssemblyDoc::GetBox)。零件数含紧固件。");
            dims.AppendLine();
            dims.AppendLine("| 装配体 | 零件数 | 外形 L×H×W (mm) |");
            dims.AppendLine("|---|---:|---|");
            foreach (var r in results) dims.AppendLine(string.Format("| {0} | {1} | {2:F0} × {3:F0} × {4:F0} |", r.Name, r.Parts, r.L, r.H, r.W));
            string fDims = Path.Combine(Run.Checks, "assemblies.md");
            File.WriteAllText(fDims, dims.ToString(), U8);

            int total = results.Sum(r => r.Interferences), acc = results.Sum(r => r.Accepted), fresh = total - acc;
            var head = new StringBuilder();
            head.AppendLine("# 干涉检查报告");
            head.AppendLine();
            head.AppendLine("> SolidWorks 干涉检查 (IInterferenceDetectionMgr), 重合面/相切接触不计为干涉。");
            head.AppendLine("> 装配体内全部零件两两检查, 含全部紧固件 —— 螺栓穿过的孔若缺失或错位即报干涉, 兼作孔位校核。");
            if (acc > 0)
                head.AppendLine("> \"已登记接受\" = 设计项目登记的已知干涉 (如导入设计原模型自带的, 同一对零件、同一相对位置、同样体积); 只有新增的判不通过。");
            head.AppendLine();
            head.AppendLine("**检出干涉合计: " + total + " 处" + (acc > 0 ? string.Format(", 其中已登记接受 {0} 处, 新增 {1} 处", acc, fresh) : "") + "**");
            head.AppendLine();
            string fItf = Path.Combine(Run.Checks, "interference.md");
            File.WriteAllText(fItf, head.ToString() + itf.ToString(), U8);
            var failed = results.Where(r => r.Failed).ToList();
            Run.Check("干涉检查", fresh == 0 && failed.Count == 0,
                failed.Count > 0 ? "检查失败: " + string.Join(", ", failed.Select(r => r.Name))
                : acc > 0 ? string.Format("{0} 个装配体, 干涉 {1} 处: 原模型已有并已登记接受 {2}, 新增 {3}", results.Count, total, acc, fresh)
                : string.Format("{0} 个装配体, 干涉 {1} 处", results.Count, total), fItf);
            if (acc > 0) Run.Metric("干涉/已登记接受", acc);
            foreach (var r in results)
            {
                string k = "装配/" + r.Name + "/";
                Run.Metric(k + "零件数", r.Parts);
                Run.Metric(k + "长mm", r.L, tol: 1); Run.Metric(k + "高mm", r.H, tol: 1); Run.Metric(k + "宽mm", r.W, tol: 1);
            }
        }

        // ================================================================ 视图截图
        public static double RearYaw = -Math.PI / 4, RearPitch = 0.6155;

        /// <summary>导出当前文档视图为 PNG (rear = 背面等轴测)</summary>
        public static void Shot(ModelDoc2 md, string file, int view, bool rear = false)
        {
            try
            {
                md.ClearSelection2(true);
                if (rear)
                {
                    md.ShowNamedView2("", (int)swStandardViews_e.swBackView);
                    ModelView mv = (ModelView)md.ActiveView;
                    mv.RotateAboutCenter(0, RearYaw);
                    mv.RotateAboutCenter(RearPitch, 0);
                }
                else md.ShowNamedView2("", view);
                md.ViewZoomtofit2();
                md.ViewDisplayShaded();
                int e = 0, w = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                md.Extension.SaveAs(file, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e, ref w);
            }
            catch (Exception ex) { Sw.Log("!! 截图失败 " + Path.GetFileName(file) + ": " + ex.Message); }
        }

        /// <summary>按文件名片段隐藏/显示零部件 (出内部结构视图)</summary>
        public static int Hide(AssemblyDoc a, Func<Component2, bool> pick, bool hide)
        {
            object[] cs = (object[])a.GetComponents(false);
            int n = 0;
            if (cs == null) return 0;
            foreach (object o in cs)
            {
                Component2 c = (Component2)o;
                if (!pick(c)) continue;
                c.Visible = (int)(hide ? swComponentVisibilityState_e.swComponentHidden : swComponentVisibilityState_e.swComponentVisible);
                n++;
            }
            ((ModelDoc2)a).GraphicsRedraw2();
            return n;
        }

        // ================================================================ BOM
        public sealed class BomItem { public string Code, Name, Spec, Mat, Kind, Brand, Proc, Blank, Surf, Remark, File; public int Qty; public double Kg; }

        /// <summary>
        /// 遍历装配体 (含全部子装配与紧固件组) 生成分类 BOM: bom/BOM_物料清单.md + bom/BOM_&lt;类别&gt;.csv (带 BOM 头, Excel 直接打开)。
        /// 自制件质量 = 实体体积 × GB 牌号密度; 外购件取样本值。extras = 未建模但须采购的物料。返回设备净重 kg。
        /// </summary>
        public static double Bom(string asmPath, string title, IEnumerable<BomItem> extras = null)
        {
            Sw.Head("分类 BOM");
            Sw.CloseAll();
            ModelDoc2 am = Sw.Open(asmPath);
            AssemblyDoc a = (AssemblyDoc)am;
            try { a.ResolveAllLightWeightComponents(false); } catch { }
            var map = new Dictionary<string, BomItem>(StringComparer.OrdinalIgnoreCase);
            object[] cs = (object[])a.GetComponents(false) ?? new object[0];
            foreach (object o in cs)
            {
                Component2 c = (Component2)o;
                string p = c.GetPathName();
                if (string.IsNullOrEmpty(p) || !p.ToLower().EndsWith(".sldprt") || c.IsSuppressed()) continue;
                BomItem it;
                if (map.TryGetValue(p, out it)) { it.Qty++; continue; }
                ModelDoc2 m = (ModelDoc2)c.GetModelDoc2();
                it = new BomItem { Qty = 1, File = Path.GetFileName(p) };
                if (m != null)
                {
                    it.Code = Sw.GetProp(m, "代号"); it.Name = Sw.GetProp(m, "名称"); it.Spec = Sw.GetProp(m, "规格");
                    it.Mat = Sw.GetProp(m, "材料"); it.Kind = Sw.GetProp(m, "类别"); it.Brand = Sw.GetProp(m, "品牌");
                    it.Proc = Sw.GetProp(m, "工艺"); it.Blank = Sw.GetProp(m, "下料"); it.Surf = Sw.GetProp(m, "表面处理");
                    it.Remark = Sw.GetProp(m, "备注");
                    double kg; double.TryParse(Sw.GetProp(m, "质量"), out kg); it.Kg = kg;
                }
                map[p] = it;
            }
            Sw.CloseAll();
            if (extras != null) foreach (var x in extras) map["extra:" + x.Code + x.Name] = x;

            var all = map.Values.ToList();
            var md = new StringBuilder();
            md.AppendLine("# " + title + "  物料清单 (BOM)");
            md.AppendLine();
            md.AppendLine("> 由 SolidWorks 装配体遍历自动生成 (含全部子装配与紧固件组)。自制件质量 = 实体体积 × GB 牌号密度; 外购件质量取样本值。");
            md.AppendLine("> 工艺物料不计入设备重量。");
            md.AppendLine();
            double grand = 0;
            var sum = new List<string>();
            foreach (string k in new[] { "自制件", "外购件", "标准件", "工艺物料" })
            {
                var list = all.Where(x => x.Kind == k).OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Name).ToList();
                if (list.Count == 0) continue;
                double tot = list.Sum(x => x.Kg * x.Qty);
                if (k != "工艺物料") { grand += tot; sum.Add(string.Format("| {0} | {1} | {2} | {3:F1} |", k, list.Count, list.Sum(x => x.Qty), tot)); }
                md.AppendLine("## " + k + string.Format(" ({0} 种 / {1} 件 / {2:F1} kg)", list.Count, list.Sum(x => x.Qty), tot));
                md.AppendLine();
                if (k == "自制件")
                {
                    md.AppendLine("| 序号 | 图号 | 名称 | 规格 | 材料 | 数量 | 单重 kg | 下料 | 工艺路线 | 表面处理 |");
                    md.AppendLine("|---:|---|---|---|---|---:|---:|---|---|---|");
                    int i = 0; foreach (var t in list) md.AppendLine(string.Format("| {0} | {1} | {2} | {3} | {4} | {5} | {6:F2} | {7} | {8} | {9} |", ++i, t.Code, t.Name, t.Spec, t.Mat, t.Qty, t.Kg, t.Blank, t.Proc, t.Surf));
                }
                else
                {
                    md.AppendLine("| 序号 | 代号 | 名称 | 规格/型号 | 数量 | 单重 kg | 推荐品牌 | 备注 |");
                    md.AppendLine("|---:|---|---|---|---:|---:|---|---|");
                    int i = 0; foreach (var t in list) md.AppendLine(string.Format("| {0} | {1} | {2} | {3} | {4} | {5:F2} | {6} | {7} |", ++i, t.Code, t.Name, t.Spec, t.Qty, t.Kg, t.Brand, t.Remark));
                }
                md.AppendLine();
                var csv = new StringBuilder();
                csv.AppendLine("序号,图号/代号,名称,规格,材料,数量,单重kg,总重kg,下料,工艺路线,表面处理,品牌,备注,文件");
                int j = 0;
                foreach (var t in list)
                    csv.AppendLine(string.Join(",", new[] { (++j).ToString(), t.Code, t.Name, t.Spec, t.Mat, t.Qty.ToString(), t.Kg.ToString("F3"),
                        (t.Kg * t.Qty).ToString("F2"), t.Blank, t.Proc, t.Surf, t.Brand, t.Remark, t.File }.Select(Q).ToArray()));
                File.WriteAllText(Path.Combine(Run.Bom, "BOM_" + k + ".csv"), csv.ToString(), U8B);
            }
            var head = new StringBuilder();
            head.AppendLine("## 汇总");
            head.AppendLine();
            head.AppendLine("| 类别 | 种数 | 件数 | 质量 kg |");
            head.AppendLine("|---|---:|---:|---:|");
            foreach (string s in sum) head.AppendLine(s);
            head.AppendLine(string.Format("| **合计** | | | **{0:F1}** |", grand));
            head.AppendLine();
            string text = md.ToString();
            int pos = text.IndexOf("\n## ", StringComparison.Ordinal);
            text = pos < 0 ? text + head : text.Substring(0, pos + 1) + head.ToString() + text.Substring(pos + 1);
            string f = Path.Combine(Run.Bom, "BOM_物料清单.md");
            File.WriteAllText(f, text, U8);
            Sw.Log(string.Format("BOM: 合计 {0:F1} kg; {1}", grand, string.Join(" / ", sum.ToArray())));
            Run.Check("物料清单", all.Count > 0 && all.All(x => !string.IsNullOrEmpty(x.Kind)),
                string.Format("{0} 种 {1} 件, 合计 {2:F1} kg", all.Count, all.Sum(x => x.Qty), grand), f);
            Run.Metric("物料/种数", all.Count);
            Run.Metric("物料/件数", all.Sum(x => x.Qty));
            Run.Metric("物料/质量kg", grand, tolPct: 0.5);
            return grand;
        }

        static string Q(string s) { s = s ?? ""; return "\"" + s.Replace("\"", "\"\"") + "\""; }
    }
}
