using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 工程图自动生成 (GB 模板, 第一角投影):
    ///   零件图: 视图 + 外形尺寸 + 回转体直径/轴向尺寸(含配合) + 孔坐标表(孔标记) + 孔径标注(含螺纹孔) + 技术要求 + 标题栏属性
    ///   装配图: 主/俯/轴测视图 + 明细表 + 零件序号
    ///   输出: SLDDRW + PDF; 激光下料件另出 DXF (1:1 展开面)
    /// 选边方式: View.GetPolylines7 取视图几何(视图坐标, 模型尺度) -> View.SelectEntity 精确选中, 不依赖屏幕拾取;
    /// 装配图中先按记录位置换成所在实例上的边 (InstanceEnt); 外形尺寸是否量对由后处理从 PDF 读出标注值核对 (ExtentDim、MarksCol)。
    /// </summary>
    public static class Drw
    {
        static string DrwRoot { get { return Run.Drawings; } }
        static string DxfRoot { get { return Run.Dxf; } }
        // GB/T 14690 优先比例 + 允许比例 (1:1.5, 1:2.5, 1:3, 1:4, 1:6 及其 ×10ⁿ)
        static readonly double[] SCALES = { 0.2, 0.25, 0.4, 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 6, 10, 15, 20, 25, 30, 40, 50 };   // <1 为放大比例 (0.5 = 2:1)

        /// <summary>比例文字: 1:n 或 n:1</summary>
        static string ScaleText(double n) { return n >= 1 ? "1:" + n : (1 / n).ToString("0.#") + ":1"; }
        const double MM = 0.001;

        class Seg { public bool Circle; public double X1, Y1, X2, Y2, Cx, Cy, R; public object Ent; }

        /// <summary>零件图技术要求末条 (设计项目的通用技术条件), null = 不加</summary>
        public static string PartGeneralNote;
        /// <summary>装配图技术要求 (多行, 不含标题 "技术要求")</summary>
        public static string AsmNotes = DefaultAsmNotes;
        const string DefaultAsmNotes = "1. 装配前所有零件清洗干净, 去毛刺。\n2. 螺栓按标准拧紧力矩紧固。\n3. 未注明处按通用技术条件执行。";
        /// <summary>强制用 A0 出图的装配体 (如整机总装)</summary>
        public static readonly HashSet<string> ForceA0 = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>明细表只列顶层的装配体 (强制 A0 的总装也是顶层明细)</summary>
        public static readonly HashSet<string> TopLevelBom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 图纸目录行: 图号, 名称, 图幅, 比例, 文件, 备注, 尺寸数, 孔标注数, 序号数, 外形尺寸读数 (ExtentsCol), 外形尺寸位置与应有值 (MarksCol)
        /// (index.tsv 同序)。外形尺寸读数在出图时留空 (没有外形尺寸为 "[]"), 由后处理从 PDF 读出后填入 (ApplyExtentReadings)
        /// </summary>
        static readonly List<string[]> Index = new List<string[]>();
        const int INDEX_COLS = 11;
        public const string NeedsDims = "待补尺寸";
        /// <summary>取视图几何的超时 (s): 正常最大的视图 (微波实验炉总装) 也只要几秒, 超过即视为挂起</summary>
        const int GEO_TIMEOUT_S = 300;
        /// <summary>专用实例因挂起被结束 (或崩溃) 后, 同一张图重启实例重画的次数上限</summary>
        const int LOST_RETRIES = 2;
        static int nOk, nFail, nDxf;
        /// <summary>最近一次 All() 的失败张数</summary>
        public static int Failed;

        /// <summary>清空出图状态与设计项目的出图设置 (每次运行开始时由 Pipeline 调用)</summary>
        public static void Reset()
        {
            Index.Clear(); nOk = nFail = nDxf = 0; Failed = 0;
            ForceA0.Clear(); TopLevelBom.Clear(); PartGeneralNote = null; AsmNotes = DefaultAsmNotes;
        }

        /// <summary>调试 (cad dump): 输出工程图各视图概况与第 viewIndex 个模型视图 (1 起) 的折线原始记录</summary>
        public static void Dump(string drwPath, int viewIndex = 1, int maxRecords = 6)
        {
            ModelDoc2 d = Sw.Open(drwPath);
            View v = (View)((DrawingDoc)d).GetFirstView();
            Sheet sh = (Sheet)((DrawingDoc)d).GetCurrentSheet();
            Sw.Log("图纸属性 " + string.Join(" ", ((double[])sh.GetProperties2()).Select(q => q.ToString("0.###"))) + " 格式=" + sh.GetTemplateName());
            object[] ns = (object[])v.GetNotes();
            if (ns != null) foreach (Note nt in ns)
                {
                    string t = nt.PropertyLinkedText ?? "";
                    if (t.Contains("$") || nt.GetText().Contains(":")) Sw.Log("  注释 " + t + " => " + nt.GetText());
                }
            LayerMgr lm = (LayerMgr)d.GetLayerManager();
            Sw.Log("  当前图层: " + lm.GetCurrentLayer());
            foreach (string ln in (string[])lm.GetLayerList() ?? new string[0])
            {
                Layer ly = (Layer)lm.GetLayer(ln);
                Sw.Log(string.Format("    图层 {0} 可见={1} 打印={2}", ln, ly.Visible, ly.Printable));
            }
            for (View q = (View)v.GetNextView(); q != null; q = (View)q.GetNextView())
            {
                object[] tas = (object[])q.GetTableAnnotations();
                if (tas != null)
                    foreach (TableAnnotation ta in tas)
                    {
                        var sb = new StringBuilder();
                        for (int r = 0; r < Math.Min(ta.RowCount, 4); r++) { for (int c = 0; c < ta.ColumnCount; c++) sb.Append(ta.get_Text(r, c) + "|"); sb.Append(" // "); }
                        Sw.Log(string.Format("  表格 类型={0} 行={1} 列={2}: {3}", ta.Type, ta.RowCount, ta.ColumnCount, sb));
                        if (ta.Type == (int)swTableAnnotationType_e.swTableAnnotation_BillOfMaterials)
                        {
                            BomFeature bf = ((BomTableAnnotation)ta).BomFeature;
                            object vis = null;
                            object cf = bf.GetConfigurations(true, ref vis);
                            Sw.Log(string.Format("  明细表: 类型={0} 配置={1}", bf.TableType, cf == null ? "-" : string.Join(",", (string[])cf)));
                        }
                    }
                double[] ol = (double[])q.GetOutline(); double[] ps = (double[])q.Position;
                object[] eq = Sw.Guard("视图 " + q.Name + " 取几何 (GetPolylines7)", GEO_TIMEOUT_S, () => { object o; return (object[])q.GetPolylines7(1, out o); });
                Sw.Log(string.Format("  视图 {0} 模型={1} 位置=({2:F3},{3:F3}) 外框=({4:F3},{5:F3})-({6:F3},{7:F3}) 比例={8} 可见={9} 边数={10} 注释={11}",
                    q.Name, Path.GetFileName(q.GetReferencedModelName()), ps[0], ps[1], ol[0], ol[1], ol[2], ol[3], q.ScaleDecimal,
                    q.GetVisible(), eq == null ? 0 : eq.Length, q.GetNoteCount()));
                // 装配图视图: 有几何显示的零部件数 (零部件可见却没画出时比别的运行少, 见 api_calibration.md 第 2 节第 27 条)、视图中隐藏的零部件
                var hid = ((object[])q.GetHiddenComponents() ?? new object[0]).OfType<Component2>().Select(c => c.Name2).ToList();
                Sw.Log(string.Format("      轻化={0} 显示的零部件 {1} 个, 视图中隐藏 {2} 个{3}", q.IsLightweight(), q.GetVisibleComponentCount(),
                    hid.Count, hid.Count == 0 ? "" : ": " + string.Join(", ", hid)));
            }
            for (int vi = 0; vi < viewIndex; vi++) v = (View)v.GetNextView();
            object plo = null;
            object[] ents = Sw.Guard("视图 " + v.Name + " 取几何 (GetPolylines7)", GEO_TIMEOUT_S, () => { object o; var e = (object[])v.GetPolylines7(1, out o); plo = o; return e; });
            double[] pl = (double[])plo;
            Sw.Log("视图 " + v.Name + " 实体 " + ents.Length + " 数据 " + pl.Length);
            int shown = 0;
            var types = new Dictionary<string, int>();
            string perr;
            foreach (var rec in ParsePolylines(pl, ents.Length, out perr))
            {
                string tk = "type=" + rec.Type + " gs=" + (int)pl[rec.GeomAt - 1] + (rec.GeomLen == (int)pl[rec.GeomAt - 1] ? "" : "(无几何数据)");
                types[tk] = types.TryGetValue(tk, out int tc) ? tc + 1 : 1;
                if (shown < maxRecords && (rec.Type != 0 || rec.Np != 2))
                {
                    shown++;
                    double mnx = double.MaxValue, mxx = double.MinValue, mny = double.MaxValue, mxy = double.MinValue;
                    for (int j = 0; j < rec.Np; j++)
                    {
                        double px = pl[rec.PtsAt + 3 * j], py = pl[rec.PtsAt + 3 * j + 1];
                        mnx = Math.Min(mnx, px); mxx = Math.Max(mxx, px); mny = Math.Min(mny, py); mxy = Math.Max(mxy, py);
                    }
                    object e = ents[rec.Index];
                    Sw.Log(string.Format("      实体={0} 点范围 x[{1:F5},{2:F5}] y[{3:F5},{4:F5}]", e == null ? "null" : (e is Edge ? "Edge" : e.GetType().Name), mnx, mxx, mny, mxy));
                    var sb = new StringBuilder();
                    for (int j = 0; j < Math.Min(rec.GeomLen, 16); j++) sb.Append(pl[rec.GeomAt + j].ToString("F4") + " ");
                    Sw.Log(string.Format("  [{0}] {1} np={2} geom: {3}", rec.Index, tk, rec.Np, sb));
                    var pt = new StringBuilder();
                    for (int j = 0; j < Math.Min(rec.Np, 4); j++) pt.Append(string.Format("({0:F4},{1:F4}) ", pl[rec.PtsAt + 3 * j], pl[rec.PtsAt + 3 * j + 1]));
                    Sw.Log("      pts: " + pt);
                }
            }
            Sw.Log("记录类型: " + string.Join(", ", types.Select(kv => kv.Key + "×" + kv.Value)) + (perr == null ? "" : "; !! " + perr));
            List<Seg> soft;
            var g = Geo(v, out soft);
            Sw.Log(string.Format("Geo: 直线 {0} 整圆 {1} 软直线 {2}; View.Position = ({3:F4},{4:F4}) 比例 {5}", g.Count(q => !q.Circle), g.Count(q => q.Circle), soft.Count,
                ((double[])v.Position)[0], ((double[])v.Position)[1], v.ScaleDecimal));
            foreach (var s in g.Where(q => !q.Circle).Concat(soft))
                Sw.Log(string.Format("   {0} ({1:F5},{2:F5})-({3:F5},{4:F5})", soft.Contains(s) ? "软" : "线", s.X1, s.Y1, s.X2, s.Y2));
            double[] o = ToSheet(v, 0, 0, 0);
            Sw.Log(string.Format("模型原点 -> 图纸 ({0:F4},{1:F4})", o[0], o[1]));
            DimProbe(d, v, pl, ents);
            Sw.App.CloseDoc(d.GetTitle());
        }

        /// <summary>
        /// 调试 (cad dump): 照外形尺寸 (Overall) 的选法列出最下 / 最上的水平线候选 (y 全精度、所在实例), 逐个与最下一条配对标竖直尺寸,
        /// 按实例选边 (InstanceEnt) 与按原实体选边各标一次, 输出尺寸读数 (标完即删, 文档不保存)。
        /// </summary>
        static void DimProbe(ModelDoc2 d, View v, double[] pl, object[] ents)
        {
            string perr;
            var hl = new List<(Seg s, int i)>();
            foreach (var rec in ParsePolylines(pl, ents.Length, out perr))
            {
                if (rec.Np != 2 || (rec.Type == 1 && rec.GeomLen >= 9)) continue;
                var s = new Seg { X1 = pl[rec.PtsAt], Y1 = pl[rec.PtsAt + 1], X2 = pl[rec.PtsAt + 3], Y2 = pl[rec.PtsAt + 4], Ent = ents[rec.Index] };
                if (Math.Abs(s.Y1 - s.Y2) < 1e-7 && Math.Abs(s.X1 - s.X2) > 1e-4) hl.Add((s, rec.Index));
            }
            if (hl.Count == 0) return;
            var bot = hl.OrderBy(h => h.s.Y1).ThenByDescending(h => Math.Abs(h.s.X1 - h.s.X2)).Take(4).ToList();
            var top = hl.OrderByDescending(h => h.s.Y1).ThenByDescending(h => Math.Abs(h.s.X1 - h.s.X2)).Take(8).ToList();
            var parts = AsmParts(v.ReferencedDocument as AssemblyDoc);       // 调试探针 (cad dump), 不在运行出图中
            var inst = new Dictionary<int, Seg>();
            Sw.Log(string.Format("尺寸探针: 零件实例 {0} 个; 最下 / 最上水平线候选", parts.Count));
            foreach (var h in bot.Concat(top))
            {
                string geo = "", miss;
                try
                {
                    var cp = ((Edge)h.s.Ent).GetCurveParams3();
                    double[] a = (double[])cp.StartPoint, b = (double[])cp.EndPoint;
                    geo = string.Format(" 边 (零件坐标 mm) ({0:F2},{1:F2},{2:F2})-({3:F2},{4:F2},{5:F2})", a[0] * 1000, a[1] * 1000, a[2] * 1000, b[0] * 1000, b[1] * 1000, b[2] * 1000);
                }
                catch { geo = " 边几何读不出"; }
                Component2 ic;
                inst[h.i] = new Seg { X1 = h.s.X1, Y1 = h.s.Y1, X2 = h.s.X2, Y2 = h.s.Y2, Ent = InstanceEnt(v, h.s, parts, out miss, out ic) };
                string cn = ic != null ? ic.Name2 : miss ?? "原实体已带零部件";
                Sw.Log(string.Format("   [{0}] y={1:F9} m 长 {2:F1} mm x[{3:F1},{4:F1}] 实例 {5}{6}", h.i, h.s.Y1, Math.Abs(h.s.X1 - h.s.X2) * 1000,
                    Math.Min(h.s.X1, h.s.X2) * 1000, Math.Max(h.s.X1, h.s.X2) * 1000, cn, geo));
            }
            double[] ol = (double[])v.GetOutline();
            Func<Seg, Seg, string> dim = (p, q) =>
            {
                if (!Sel(d, v, p, false) || !Sel(d, v, q, true)) return "选不中";
                var dm = (DisplayDimension)d.AddVerticalDimension2(ol[0] - 0.012, (ol[1] + ol[3]) / 2, 0);
                if (dm == null) return "标不出";
                string val;
                try { val = (((Dimension)dm.GetDimension2(0)).SystemValue * 1000).ToString("F3"); } catch (Exception x) { val = "读数失败 " + x.Message; }
                d.ClearSelection2(true);
                ((Annotation)dm.GetAnnotation()).Select3(false, null);
                d.Extension.DeleteSelection2(0);
                return val;
            };
            foreach (var t in top)
                Sw.Log(string.Format("   [{0}]-[{1}] 几何 {2:F3} mm, 按实例选边 {3}, 按原实体选边 {4}", bot[0].i, t.i, (t.s.Y1 - bot[0].s.Y1) * 1000,
                    dim(inst[bot[0].i], inst[t.i]), dim(bot[0].s, t.s)));
            d.ClearSelection2(true);
        }

        /// <summary>
        /// 各图幅空白图纸 (只有 GB 图框与标题栏) 导出到 drawings/_formats/format_A3.pdf 等,
        /// 供 python/audit_drawings.py 区分"图纸格式"与"内容" (同图幅只有一张图时, 统计法无法识别格式元素)。
        /// </summary>
        public static void FormatSheets()
        {
            string dir = Path.Combine(DrwRoot, "_formats");
            Directory.CreateDirectory(dir);
            foreach (string size in new[] { "a3", "a2", "a0" })
            {
                ModelDoc2 d;
                NewDrawing(size, out d);
                int e1 = 0, w1 = 0;
                d.Extension.SaveAs(Path.Combine(dir, "format_" + size.ToUpper() + ".pdf"), 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e1, ref w1);
                Sw.App.CloseDoc(d.GetTitle());
            }
        }

        /// <summary>重新打开已保存的工程图并导出 PDF (大装配体视图后台生成, 新建后立即导出会得到空视图)</summary>
        public static void Pdf(string drwPath, string ext = ".pdf")
        {
            ModelDoc2 d = Sw.Open(drwPath);
            d.ForceRebuild3(false);
            int e1 = 0, w1 = 0;
            bool ok = d.Extension.SaveAs(Path.ChangeExtension(drwPath, ext), 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e1, ref w1);
            Sw.Log(ext.ToUpper() + (ok ? " OK " : " 失败 ") + Path.GetFileName(drwPath) + " err=" + e1 + " warn=" + w1);
            Sw.App.CloseDoc(d.GetTitle());
        }

        /// <summary>
        /// 出图: 本次运行已建零件中 _DRW 非空者出零件图 (filter 按路径片段筛选, "asm" = 只出装配图);
        /// assemblies 为要出装配图的装配体路径 (按给定顺序)。
        /// </summary>
        public static void All(string filter, IEnumerable<string> assemblies)
        {
            Sw.Head("工程图");
            Directory.CreateDirectory(DrwRoot); Directory.CreateDirectory(DxfRoot);
            var paths = Cat.Reg.Values.Where(p => p.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                                      .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
            foreach (string path in paths)
            {
                bool asmOnly = filter != null && (filter == "asm" || filter.StartsWith("asm:"));
                if (asmOnly) break;
                if (filter != null && !filter.Split(',').Any(fl => path.Contains(fl))) continue;
                if (path.Contains(@"\00_标准件\") || path.Contains(@"\00_外购件\") || path.Contains(@"\管段\")) continue;
                bool dxfDone = false;
                for (int attempt = 1; ; attempt++)
                {
                    if (Sw.Lost) Sw.Reconnect();
                    Sw.CloseAll();
                    Sw.Recycle(5.0);
                    ModelDoc2 m = null;
                    try
                    {
                        m = Sw.Open(path);
                        string type = Sw.GetProp(m, "_DRW");
                        if (type == "" || type == "none") { Sw.Close(m); break; }
                        // DXF 须在出图前导出 (PartDrawing 结束时关闭零件); 重画时已导出的不再导出
                        string dxf = Sw.GetProp(m, "_DXF");
                        if (dxf != "" && !dxfDone) { Dxf(m, path, dxf); dxfDone = true; }
                        PartDrawing(m, path, type);
                        nOk++;
                        WriteIndex();                    // 每张图后更新目录: 中途中断也不丢失已出图记录
                    }
                    catch (Exception e)
                    {
                        // 专用实例被超时看守结束 (或崩溃): 重启后整张重画
                        if (Sw.Lost && attempt <= LOST_RETRIES) { Sw.Log("!! " + Path.GetFileName(path) + ": " + e.Message + " → 重启专用实例后重画 (第 " + attempt + " 次)"); continue; }
                        nFail++; Sw.Log("!! 出图失败 " + Path.GetFileName(path) + ": " + e.Message);
                    }
                    break;
                }
            }
            if (filter == null || filter == "asm" || filter.StartsWith("asm:"))
                foreach (string ap in assemblies ?? new string[0])
                {
                    if (filter != null && filter.StartsWith("asm:") && !ap.Contains(filter.Substring(4))) continue;   // asm:M03
                    if (Sw.Lost) Sw.Reconnect();
                    Sw.CloseAll();
                    Sw.Recycle(ForceA0.Contains(ap) ? 2.5 : 5.0);   // 大图前尽量从干净的实例开始
                    // 大装配图保存偶发 err=1 (CFC 与微波实验炉实测, 同一张图下次即正常) → 关闭专用实例中的全部文档 (含未保存的这张图) 后整张重画一次。
                    // 专用实例被超时看守结束 (或崩溃) 时重启后重画, 另计次数 (至多 LOST_RETRIES 次), 不占上述重画
                    for (int attempt = 1, lost = 0; attempt <= 2; attempt++)
                    {
                        if (Sw.Lost) Sw.Reconnect();          // 重画次数用尽后的保存失败重画同样要先有实例
                        try { AsmDrawing(ap); nOk++; WriteIndex(); break; }
                        catch (Exception e)
                        {
                            Sw.Log("!! 装配图失败 " + Path.GetFileName(ap) + " (第 " + attempt + " 次): " + e.Message);
                            if (Sw.Lost && lost++ < LOST_RETRIES) { Sw.Log("   重启专用实例后重画"); attempt--; continue; }
                            try { Sw.App.CloseAllDocuments(true); } catch { }
                            if (attempt == 2) nFail++;
                        }
                    }
                }
            Sw.CloseAll();
            WriteIndex();
            Sw.Log(string.Format("工程图完成: 成功 {0} / 失败 {1}; DXF {2}", nOk, nFail, nDxf));
            Failed = nFail;
        }

        // =====================================================================================
        static string Tpl(string size) { return Path.Combine(Sw.TplDir, "gb_" + size + ".drwdot"); }

        /// <summary>图纸比例 1:n (保持模板图幅与第一视角; Sheet.SetScale 实测不生效)</summary>
        static void SheetScale(DrawingDoc dd, double n)
        {
            Sheet sh = (Sheet)dd.GetCurrentSheet();
            double[] p = (double[])sh.GetProperties2();
            // 模板内记录的图纸格式路径是模板作者机器上的 (v:\updated cs\...); A0 时 SetupSheet5 因此失败 → 改用本机 sheetformat 目录中的同名文件
            string fmt = sh.GetTemplateName();
            double s1n = n >= 1 ? 1 : 1 / n, s2n = n >= 1 ? n : 1;      // 放大比例按 n:1 写入
            bool ok = dd.SetupSheet5(sh.GetName(), (int)p[0], (int)p[1], s1n, s2n, true, fmt, p[5], p[6], "默认", false);
            if (!ok && !File.Exists(fmt))
            {
                string exe = Sw.App.GetExecutablePath();
                string alt = Path.Combine(exe, @"lang\chinese-simplified\sheetformat", Path.GetFileName(fmt));
                if (!File.Exists(alt)) alt = Path.Combine(Path.GetDirectoryName(exe), @"lang\chinese-simplified\sheetformat", Path.GetFileName(fmt));
                if (File.Exists(alt)) ok = dd.SetupSheet5(sh.GetName(), (int)p[0], (int)p[1], s1n, s2n, true, alt, p[5], p[6], "默认", false);
            }
            double[] q = (double[])sh.GetProperties2();
            if (Math.Abs(q[3] / q[2] - n) > 1e-6) Sw.Log(string.Format("   ! 图纸比例未生效 {0} (SetupSheet5={1})", ScaleText(q[3] / q[2]), ok));
        }

        static DrawingDoc NewDrawing(string size, out ModelDoc2 d)
        {
            int ps = size == "a3" ? (int)swDwgPaperSizes_e.swDwgPaperA3size : size == "a2" ? (int)swDwgPaperSizes_e.swDwgPaperA2size
                   : size == "a1" ? (int)swDwgPaperSizes_e.swDwgPaperA1size : (int)swDwgPaperSizes_e.swDwgPaperA0size;
            double[] wh = size == "a3" ? new[] { 0.42, 0.297 } : size == "a2" ? new[] { 0.594, 0.42 } : size == "a1" ? new[] { 0.841, 0.594 } : new[] { 1.189, 0.841 };
            d = (ModelDoc2)Sw.App.NewDocument(Tpl(size), ps, wh[0], wh[1]);
            if (d == null) throw new Exception("新建工程图失败 " + size);
            // GB A0/A1 模板显示基准轴/原点/参考点, 统一关闭 (其质量单位为 g: 明细表与标题栏质量改用零件属性 "质量", 不受影响)
            var ext = d.Extension;
            foreach (var t in new[] { swUserPreferenceToggle_e.swDisplayAxes, swUserPreferenceToggle_e.swDisplayOrigins,
                                      swUserPreferenceToggle_e.swDisplayReferencePoints, swUserPreferenceToggle_e.swDisplayReferencePoints2 })
                ext.SetUserPreferenceToggle((int)t, 0, false);
            return (DrawingDoc)d;
        }

        static double[] ToSheet(View v, double x, double y, double z)
        {
            MathUtility mu = (MathUtility)Sw.App.GetMathUtility();
            MathPoint mp = (MathPoint)mu.CreatePoint(new double[] { x * MM, y * MM, z * MM });
            mp = (MathPoint)mp.MultiplyTransform(v.ModelToViewTransform);
            return (double[])mp.ArrayData;
        }

        /// <summary>视图几何 (视图局部坐标, 米, 模型尺度): 直线 + 整圆</summary>
        static List<Seg> Geo(View v) { List<Seg> soft; return Geo(v, out soft); }

        /// <summary>
        /// 同上; soft = 侧看的圆/圆弧边 (投影为与坐标轴平行的直线)。实测 (2026-10-09): 这类边以圆弧记录返回,
        /// 起点 ≠ 终点、折线点共线 (如 68 点), 原逻辑既不当圆也不当直线。只作外形尺寸的后备, 不参与孔与回转体尺寸。
        /// </summary>
        static List<Seg> Geo(View v, out List<Seg> soft)
        {
            var r = new List<Seg>();
            soft = new List<Seg>();
            // GetPolylines7 偶发不返回 (SolidWorks 空闲挂起, api_calibration.md 第 4 节第 10 条): 超时即结束专用实例, 由出图循环重启后重画这张图
            object plo = null;
            object[] ents = Sw.Guard("视图 " + v.Name + " 取几何 (GetPolylines7)", GEO_TIMEOUT_S, () => { object o; var e = (object[])v.GetPolylines7(1, out o); plo = o; return e; });
            double[] pl = (double[])plo;
            if (ents == null || pl == null) return r;
            string perr;
            foreach (var rec in ParsePolylines(pl, ents.Length, out perr))
            {
                int type = rec.Type, g0 = rec.GeomAt, np = rec.Np, p0 = rec.PtsAt, i = rec.Index;
                var xs = new double[np]; var ys = new double[np];
                for (int j = 0; j < np; j++) { xs[j] = pl[p0 + 3 * j]; ys[j] = pl[p0 + 3 * j + 1]; }
                if (type == 1 && rec.GeomLen >= 9)
                {
                    // 圆弧: [圆心 xyz, 起点 xyz, 终点 xyz, 法向]; 起点 = 终点 即整圆
                    double cx = pl[g0], cy = pl[g0 + 1], sx = pl[g0 + 3], sy = pl[g0 + 4], ex = pl[g0 + 6], ey = pl[g0 + 7];
                    double rr = Math.Sqrt((sx - cx) * (sx - cx) + (sy - cy) * (sy - cy));
                    if (rr > 1e-6 && Math.Abs(sx - ex) < 1e-7 && Math.Abs(sy - ey) < 1e-7)
                        r.Add(new Seg { Circle = true, Cx = cx, Cy = cy, R = rr, Ent = ents[i] });
                    else { var s = AxisLine(xs, ys, ents[i]); if (s != null) soft.Add(s); }
                }
                else if (np == 2) r.Add(new Seg { X1 = xs[0], Y1 = ys[0], X2 = xs[1], Y2 = ys[1], Ent = ents[i] });
                else if (np > 2) { var s = AxisLine(xs, ys, ents[i]); if (s != null) soft.Add(s); }
            }
            if (perr != null) Sw.Log("   ! 视图 " + v.Name + " 折线数据: " + perr + " (之后的边未参与标注)");
            return r;
        }

        /// <summary>GetPolylines7 中的一条记录: 类型、几何数据 (起点, 个数)、折线点 (起点, 个数), Index = 对应实体序号</summary>
        internal struct PolyRec { public int Index, Type, GeomAt, GeomLen, PtsAt, Np; }

        /// <summary>
        /// 解析 GetPolylines7 返回的数组。记录 = [类型, gs, 几何(gs 个), 线属性 6 个, 点数, 点 (3×点数)]。
        /// 实测 (2026-10-10, 微波实验炉装配图): 由样条转成折线的边 (类型 0) 第二个数为 17, 后面却没有几何数据、直接是线属性 →
        /// 按 gs 跳过会错位, 之后的记录全部读乱 (数组长度为负 → 异常 → 该视图外形尺寸整张漏标)。
        /// 每条先按 gs 解析, 若下一条记录的起点不合理, 改按"无几何数据"解析; 两种都不合理即停止并返回出错位置 (不抛异常)。
        /// </summary>
        internal static List<PolyRec> ParsePolylines(double[] pl, int count, out string error)
        {
            var r = new List<PolyRec>(); error = null;
            int k = 0;
            for (int i = 0; i < count && k < pl.Length; i++)
            {
                PolyRec a, b;
                bool okA = TryRec(pl, k, i, Gs(pl, k), out a), okB = TryRec(pl, k, i, 0, out b);
                // 两种解释都能读出本条时, 以下一条记录的起点是否合理来定; 都不合理则仍取能读出的那种, 错误留给下一条报告
                PolyRec rec;
                if (okA && StartOk(pl, a.PtsAt + 3 * a.Np)) rec = a;
                else if (okB && StartOk(pl, b.PtsAt + 3 * b.Np)) rec = b;
                else if (okA) rec = a;
                else if (okB) rec = b;
                else { error = string.Format("第 {0} 条记录 (偏移 {1}) 无法解析", i, k); break; }
                r.Add(rec);
                k = rec.PtsAt + 3 * rec.Np;
            }
            return r;
        }

        static bool IsInt(double v) { return Math.Abs(v - Math.Round(v)) < 1e-9; }
        static int Gs(double[] pl, int k) { return k + 1 < pl.Length && IsInt(pl[k + 1]) && pl[k + 1] >= 0 && pl[k + 1] < 1e5 ? (int)pl[k + 1] : -1; }

        static bool TryRec(double[] pl, int k, int i, int geomLen, out PolyRec rec)
        {
            rec = default(PolyRec);
            if (geomLen < 0 || k + 1 >= pl.Length || !IsInt(pl[k]) || pl[k] < 0 || !IsInt(pl[k + 1])) return false;
            long npAt = (long)k + 2 + geomLen + 6;
            if (npAt >= pl.Length || !IsInt(pl[npAt]) || pl[npAt] < 0) return false;
            long np = (long)pl[npAt];
            if (npAt + 1 + 3 * np > pl.Length) return false;
            rec = new PolyRec { Index = i, Type = (int)pl[k], GeomAt = k + 2, GeomLen = geomLen, PtsAt = (int)npAt + 1, Np = (int)np };
            return true;
        }

        /// <summary>下一条记录能否从 j 开始: 恰到数组末尾, 或类型与 gs 为合理的非负整数且至少一种解释下点数位置合理</summary>
        static bool StartOk(double[] pl, int j)
        {
            if (j == pl.Length) return true;
            if (j > pl.Length - 9 || !IsInt(pl[j]) || pl[j] < 0 || pl[j] > 64) return false;
            PolyRec t;
            return TryRec(pl, j, 0, Gs(pl, j), out t) || TryRec(pl, j, 0, 0, out t);
        }

        /// <summary>折线点共线且平行于坐标轴时, 返回其外包直线段; 否则 null</summary>
        static Seg AxisLine(double[] xs, double[] ys, object ent)
        {
            if (xs.Length < 2) return null;
            double x0 = xs.Min(), x1 = xs.Max(), y0 = ys.Min(), y1 = ys.Max();
            if (x1 - x0 < 1e-7 && y1 - y0 > 1e-4) return new Seg { X1 = x0, Y1 = y0, X2 = x0, Y2 = y1, Ent = ent };
            if (y1 - y0 < 1e-7 && x1 - x0 > 1e-4) return new Seg { X1 = x0, Y1 = y0, X2 = x1, Y2 = y0, Ent = ent };
            return null;
        }

        /// <summary>
        /// 孔 = 相邻圆柱/圆锥面为凹面 (面的外法向指向轴线) 的圆边。外轮廓圆、凸台/销轴、圆角边都不是孔
        /// (板件规则原先把视图中所有整圆当孔, 盘类零件会把外圆与销轴列进孔坐标表)。
        /// 面朝向取面三角化的法向 (即背离材料的方向); 实测 FaceInSurfaceSense 不能单独判定 (圆柱曲面自身法向方向不固定)。
        /// 无法取得边、相邻面或三角化数据时按孔处理 (保持原行为)。
        /// </summary>
        internal static bool IsHoleEdge(object ent)
        {
            Edge e = ent as Edge;
            if (e == null) return true;
            Curve c = e.GetCurve() as Curve;
            if (c == null || !c.IsCircle()) return true;
            double[] cp = (double[])c.CircleParams;           // 圆心 xyz, 轴向 xyz, 半径 (m)
            object[] fs = (object[])e.GetTwoAdjacentFaces2();
            if (fs == null) return true;
            foreach (object fo in fs)
            {
                Face2 f = fo as Face2;
                if (f == null) continue;
                Surface s = (Surface)f.GetSurface();
                if (!(s.IsCylinder() || s.IsCone())) continue;
                float[] tri = f.GetTessTriangles(true) as float[];
                float[] nrm = f.GetTessNorms() as float[];
                if (tri == null || nrm == null || tri.Length < 9 || nrm.Length < 3) return true;
                return Concave(new double[] { tri[0], tri[1], tri[2] }, new double[] { nrm[0], nrm[1], nrm[2] }, cp);
            }
            return false;
        }

        /// <summary>面上一点 p 的外法向 n 是否指向轴线 (轴线过 cp[0..2], 方向 cp[3..5])</summary>
        internal static bool Concave(double[] p, double[] n, double[] cp)
        {
            double[] v = { p[0] - cp[0], p[1] - cp[1], p[2] - cp[2] };
            double ax = v[0] * cp[3] + v[1] * cp[4] + v[2] * cp[5];
            double[] radial = { v[0] - ax * cp[3], v[1] - ax * cp[4], v[2] - ax * cp[5] };
            return n[0] * radial[0] + n[1] * radial[1] + n[2] * radial[2] < 0;
        }

        /// <summary>视图局部坐标 -> 图纸坐标。GetPolylines7 的坐标以视图中心 (模型包围盒中心) 为原点, 模型尺度 (实测)</summary>
        class VMap
        {
            public double Ox, Oy, S;
            public VMap(View v)
            {
                double[] a = (double[])v.Position;
                Ox = a[0]; Oy = a[1]; S = v.ScaleDecimal;
            }
            public double X(double lx) { return Ox + S * lx; }
            public double Y(double ly) { return Oy + S * ly; }
        }

        static bool Sel(ModelDoc2 d, View v, Seg s, bool append)
        {
            if (!append) d.ClearSelection2(true);
            return s.Ent != null && v.SelectEntity(s.Ent, append);
        }

        /// <summary>
        /// 装配体的零件实例 (各层展开到零件, 不含压缩与隐藏的) 及其变换 (Transform2.ArrayData, 相对顶层装配体); am 为 null 时返回空表。
        /// 不用 View.GetVisibleComponents: 实测 (2026-10-17) 新建的装配图视图与重新打开的工程图中它都返回空。
        /// 由出图方传入已打开的装配体, 每张装配图取一次 (不必为每个外形尺寸经 View.ReferencedDocument 再取)。
        /// </summary>
        static List<(Component2 c, double[] xf)> AsmParts(AssemblyDoc am)
        {
            var r = new List<(Component2, double[])>();
            if (am == null) return r;
            foreach (Component2 c in ((object[])am.GetComponents(false) ?? new object[0]).OfType<Component2>())
            {
                try
                {
                    if (c.IsSuppressed() || c.Visible != (int)swComponentVisibilityState_e.swComponentVisible) continue;
                    object[] ch = (object[])c.GetChildren();
                    if (ch != null && ch.Length > 0) continue;
                    r.Add((c, (double[])c.Transform2.ArrayData));
                }
                catch { }
            }
            return r;
        }

        /// <summary>点 p (m) 经 MathTransform.ArrayData 变换: 行向量, [0..8] 旋转 (第 i 行 = 局部第 i 轴的像), [9..11] 平移, [12] 比例</summary>
        internal static double[] XfPoint(double[] t, double[] p)
        {
            double s = t[12];
            return new[] { s * (p[0] * t[0] + p[1] * t[3] + p[2] * t[6]) + t[9],
                           s * (p[0] * t[1] + p[1] * t[4] + p[2] * t[7]) + t[10],
                           s * (p[0] * t[2] + p[1] * t[5] + p[2] * t[8]) + t[11] };
        }

        /// <summary>点 (px, py) 到线段 (ax, ay)-(bx, by) 的距离</summary>
        internal static double SegDist(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
            double t = l2 < 1e-18 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (py - ay) * dy) / l2));
            double ex = ax + t * dx - px, ey = ay + t * dy - py;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        /// <summary>
        /// 装配图视图中, 折线记录 s 所在实例上的那条边。实测 (2026-10-17, cad dump / probe-asm, 微波实验炉配电盘模块装配图):
        /// 装配体视图的 GetPolylines7 给出的边不带零部件 (Entity.GetComponent() 为 null, 几何在零件坐标系), 同一零件的各实例共用;
        /// View.SelectEntity 收到这样的边时自行挑一个实例, 每次打开、每次出图挑的都可能不同, 不一定是记录所在的那个 —— 外形尺寸因此选到另一根同型线槽、
        /// 另一根镜像放置的竖线槽上, 总高 480 量成 220 / 440 / 180 等 (api_calibration.md 第 2 节第 26 条)。
        /// → 在视图所示装配体的零件实例中 (AsmParts), 把边的两端点经实例变换与视图变换投到图纸上, 记录的两点都落在投影线段上 (0.01 mm 内)
        /// 的实例即是, 返回该实例中的对应边 (Component2.GetCorrespondingEntity)。对应边的 GetComponent() 同样为 null, 选中后
        /// SelectionMgr.GetSelectedObjectsComponent4 也读不出实例, 是否选对只能由尺寸读数判断 (ExtentDim 读回核对)。
        /// 只处理直线边; 原实体已带零部件时原样返回 (miss = null, comp = null); 零件图视图、非直线边、找不到实例时返回原实体, miss 为原因。
        /// </summary>
        static object InstanceEnt(View v, Seg s, List<(Component2 c, double[] xf)> parts, out string miss, out Component2 comp)
        {
            miss = null; comp = null;
            Edge e = s.Ent as Edge;
            if (e == null) { miss = "非边"; return s.Ent; }
            if (parts == null || parts.Count == 0) { miss = "无零件实例"; return s.Ent; }
            double[] p1, p2;
            try
            {
                if (((Entity)e).GetComponent() != null) return s.Ent;
                Curve cv = e.GetCurve() as Curve;
                if (cv == null || !cv.IsLine()) { miss = "非直线边"; return s.Ent; }
                var cp = e.GetCurveParams3();
                p1 = (double[])cp.StartPoint; p2 = (double[])cp.EndPoint;
            }
            catch (Exception x) { miss = x.Message; return s.Ent; }
            var vm = new VMap(v);
            double[] vt = (double[])v.ModelToViewTransform.ArrayData;
            double r1x = vm.X(s.X1), r1y = vm.Y(s.Y1), r2x = vm.X(s.X2), r2y = vm.Y(s.Y2);
            const double TOL = 1e-5;                          // 图纸上 0.01 mm
            double best = double.MaxValue;
            int hits = 0;
            foreach (var (c, xf) in parts)
            {
                double[] a = XfPoint(vt, XfPoint(xf, p1)), b = XfPoint(vt, XfPoint(xf, p2));
                double dev = Math.Max(SegDist(r1x, r1y, a[0], a[1], b[0], b[1]), SegDist(r2x, r2y, a[0], a[1], b[0], b[1]));
                best = Math.Min(best, dev);
                if (dev > TOL) continue;
                hits++;
                object ce = null;
                try { ce = c.GetCorrespondingEntity(e); } catch { }
                if (ce != null) { comp = c; return ce; }
            }
            miss = hits > 0 ? hits + " 个几何吻合的实例取不到对应边" : string.Format("无几何吻合的实例 (最近 {0:F3} mm)", best * 1000);
            return s.Ent;
        }

        static DisplayDimension DimH(ModelDoc2 d, View v, Seg a, Seg b, double sx, double sy)
        {
            if (!Sel(d, v, a, false) || !Sel(d, v, b, true)) return null;
            return (DisplayDimension)d.AddHorizontalDimension2(sx, sy, 0);
        }

        static DisplayDimension DimV(ModelDoc2 d, View v, Seg a, Seg b, double sx, double sy)
        {
            if (!Sel(d, v, a, false) || !Sel(d, v, b, true)) return null;
            return (DisplayDimension)d.AddVerticalDimension2(sx, sy, 0);
        }

        /// <summary>"名称|值;名称|值" -> 有序行</summary>
        internal static List<string[]> ParseTable(string s)
        {
            var r = new List<string[]>();
            if (string.IsNullOrWhiteSpace(s)) return r;
            foreach (string row in s.Split(';'))
            {
                int i = row.IndexOf('|');
                if (i > 0) r.Add(new[] { row.Substring(0, i).Trim(), row.Substring(i + 1).Trim() });
            }
            return r;
        }

        /// <summary>参数表 (左上角在 x,y; 两列 40+56 mm, 行高 4.2 mm, 字高 2.2 mm), 与孔坐标表同一格式</summary>
        static void ParamTable(DrawingDoc dd, List<string[]> rows, double x, double y)
        {
            TableAnnotation ta = dd.InsertTableAnnotation2(false, x, y, 1, "", rows.Count, 2);
            if (ta == null) throw new Exception("InsertTableAnnotation2 返回 null");
            for (int i = 0; i < rows.Count; i++) { ta.set_Text(i, 0, rows[i][0]); ta.set_Text(i, 1, rows[i][1]); }
            TextFormat tf = ta.GetTextFormat(); tf.CharHeight = 0.0022; ta.SetTextFormat(false, tf);
            ta.SetColumnWidth(0, 0.040, 0); ta.SetColumnWidth(1, 0.056, 0);
            for (int i = 0; i < rows.Count; i++) ta.SetRowHeight(i, 0.0042, 0);
        }

        static Dictionary<double, string> ParseMap(string s)
        {
            var r = new Dictionary<double, string>();
            if (string.IsNullOrEmpty(s)) return r;
            foreach (string t in s.Split(';'))
            {
                int i = t.IndexOf(':');
                if (i > 0) { double dv; if (double.TryParse(t.Substring(0, i), out dv)) r[dv] = t.Substring(i + 1); }
            }
            return r;
        }

        static string Near(Dictionary<double, string> m, double d)
        {
            foreach (var kv in m) if (Math.Abs(kv.Key - d) < 0.05) return kv.Value;
            return null;
        }

        static void Note(ModelDoc2 d, string text, double x, double y, double h = 0.0035)
        {
            Note n = (Note)d.InsertNote(text);
            if (n == null) return;
            Annotation an = (Annotation)n.GetAnnotation();
            an.SetPosition2(x, y, 0);
            try
            {
                TextFormat tf = (TextFormat)an.GetTextFormat(0);
                tf.CharHeight = h;
                an.SetTextFormat(0, false, tf);
            }
            catch { }
        }

        // =====================================================================================
        /// <summary>单个零件出图 (不写入本设备的图纸目录)</summary>
        public static void Single(ModelDoc2 m, string path, string type) { PartDrawing(m, path, type); }

        static void PartDrawing(ModelDoc2 m, string path, string type)
        {
            string code = Sw.GetProp(m, "代号"), name = Sw.GetProp(m, "名称");
            var fits = ParseMap(Sw.GetProp(m, "_FITS")); var taps = ParseMap(Sw.GetProp(m, "_TAPS"));
            string notes = Sw.GetProp(m, "_NOTES"), dxfN = Sw.GetProp(m, "_DXF");
            double[] b = Sw.PartBox(m);
            double dx = b[3] - b[0], dy = b[4] - b[1], dz = b[5] - b[2];

            // ---- 主视图方向 ----
            string mainView; double mw, mh, depth;
            if (type == "turn") { mainView = "*前视"; mw = dz; mh = Math.Max(dx, dy); depth = Math.Max(dx, dy); }
            else
            {
                string n = dxfN == "" ? "" : dxfN + "+";
                if (n == "") n = HoleFace(m, b);                  // 方管/角钢/焊接件: 主视图正对开孔最多的表面
                if (n == "")
                {
                    double aF = dx * dy, aT = dx * dz, aR = dz * dy;
                    n = aF >= aT && aF >= aR ? "Z+" : aT >= aR ? "Y+" : "X+";
                }
                var names = new Dictionary<string, string> { { "Z+", "*前视" }, { "Z-", "*后视" }, { "Y+", "*上视" }, { "Y-", "*下视" }, { "X+", "*右视" }, { "X-", "*左视" } };
                mainView = names[n];
                if (n[0] == 'Z') { mw = dx; mh = dy; depth = dz; }
                else if (n[0] == 'Y') { mw = dx; mh = dz; depth = dy; }
                else { mw = dz; mh = dy; depth = dx; }
            }
            bool threeViews = type == "weld" || type == "sheet";
            bool holeTable = type != "turn";
            // ---- 技术要求 (左下角) 先排版: 视图区下边界 = 技术要求顶 + 20 mm (底边外形尺寸及其文字) ----
            var lines = new List<string> { "技术要求" };
            int li = 1;
            foreach (string t in (notes ?? "").Split('|')) if (t.Trim() != "") lines.AddRange(Wrap(li++ + ". " + t.Trim(), 34));
            if (dxfN != "") lines.AddRange(Wrap(li++ + ". 外形及全部开孔按 1:1 DXF 数控下料: exports/dxf/" + Path.Combine(RelDir(path), Path.GetFileNameWithoutExtension(path) + ".dxf").Replace('\\', '/'), 34));
            if (!string.IsNullOrEmpty(PartGeneralNote)) lines.AddRange(Wrap(li + ". " + PartGeneralNote, 34));
            double notesTop = Math.Min(0.012 + 0.0046 * lines.Count + 0.002, 0.118);
            // ---- 版面 (A3 横, 单位 m): 图框内框 x 0.025~0.415, y 0.005~0.292; 标题栏 x≥0.235, y≤0.061 ----
            // 绘图区 = 内框 − 左下技术要求 − 右下标题栏及"其余 Ra"; 选使视图组连同尺寸余量放得下的最大比例 (含放大比例),
            // 视图组在绘图区内居中。细长件 (长 ≥ 6 倍截面) 按 GB 断开画法去掉中间等截面段, 尺寸仍注实长。
            // 孔坐标表优先放右上角, 其次绘图区右下角; 都与视图冲突时才在右侧让出一列。
            int predHoles = type == "turn" ? 0 : HoleCount(m, mainView);
            // 参数表 (如齿轮参数, 零件属性 _TABLE) 与孔坐标表上下叠放在同一列: 参数表在上
            var param = ParseTable(Sw.GetProp(m, "_TABLE"));
            bool useHoleTab = holeTable && predHoles > 4;
            bool useTab = useHoleTab || param.Count > 0;
            double paramH = param.Count * 0.0042, holeH = useHoleTab ? (Math.Min(predHoles, 44) + 1) * 0.0042 : 0;
            double tabW = 0.096, tabH = paramH + holeH + (param.Count > 0 && useHoleTab ? 0.004 : 0);
            int la = type == "turn" ? 2 : (mainView == "*右视" || mainView == "*左视") ? 2 : 0;          // 主视图水平方向对应的模型轴
            var cuts = mw >= 6 * Math.Max(mh, 1) ? BreakSpans(m, la, b, Math.Max(mh, type == "turn" ? 0 : depth)) : new List<double[]>();
            double cutLen = cuts.Sum(c => c[1] - c[0]);
            const double BRKGAP = 0.010;                                   // 断裂线间隙 (SolidWorks 默认 10 mm)
            const double ML = 0.020, MR = 0.022, MB = 0.026, MT = 0.012;  // 尺寸、孔位链、孔标记、剖切标注所需余量
            double GX = type == "turn" ? 0.040 : 0.032, GY = 0.032;        // 视图间距 (放侧视图尺寸 / 主视图孔位链)
            double zx0 = 0.030, zy0 = Math.Max(notesTop, 0.076) + 0.004, zy1 = 0.287;
            Func<double, double> wMain = sc => (mw - cutLen) * MM / sc + cuts.Count * BRKGAP;
            Func<double, double[]> groupWH = sc =>
            {
                double k = MM / sc;
                if (type == "turn") return new[] { wMain(sc) + GX + mh * k, mh * k };
                return new[] { wMain(sc) + GX + depth * k, (threeViews ? mh + depth : mh) * k + (threeViews ? GY : 0) };
            };
            double scale = 0, gLeft = 0, gTop = 0; double[] tabPos = null;
            for (int pass = 0; pass < 2 && tabPos == null; pass++)
            {
                double zx1 = pass == 0 ? 0.410 : 0.300;                    // 第 2 轮: 右侧让出孔坐标表一列
                scale = SCALES[SCALES.Length - 1];
                foreach (double sc in SCALES)
                {
                    if (cuts.Count > 0 && sc < 1) continue;          // 断开画法的长件不用放大比例
                    double[] wh0 = groupWH(sc);
                    if (wh0[0] + ML + MR <= (zx1 - zx0) && wh0[1] + MB + MT <= (zy1 - zy0)) { scale = sc; break; }
                }
                double[] G = groupWH(scale);
                gLeft = zx0 + ML + ((zx1 - zx0) - ML - MR - G[0]) / 2;
                gTop = zy1 - MT - ((zy1 - zy0) - MT - MB - G[1]) / 2;
                if (!useTab) break;
                // 预计占位 (视图 + 尺寸余量)
                double k = MM / scale, wm = wMain(scale), mhS = mh * k, dS = (type == "turn" ? mh : depth) * k;
                var occ = new List<double[]> {
                    new[] { gLeft - ML, gTop - mhS - MB, gLeft + wm + 0.020, gTop + MT },
                    new[] { gLeft + wm + GX - 0.006, gTop - mhS - MB, gLeft + wm + GX + dS + MR, gTop + MT } };
                if (threeViews) occ.Add(new[] { gLeft - ML, gTop - mhS - GY - dS - MB, gLeft + wm + 0.020, gTop - mhS - GY + 0.006 });
                var cand = new List<double[]> {
                    new[] { 0.403 - tabW, 0.287 - tabH, 0.403, 0.287 },             // 右上角
                    new[] { 0.403 - tabW, zy0 + 0.002, 0.403, zy0 + 0.002 + tabH } }; // 绘图区右下角
                foreach (var c in cand)
                    if (c[1] >= zy0 - 1e-6 && !occ.Any(o => c[0] < o[2] && c[2] > o[0] && c[1] < o[3] && c[3] > o[1])) { tabPos = c; break; }
                if (tabPos == null && pass == 1) tabPos = cand[0];
            }
            TabX = tabPos != null ? tabPos[0] : 0.307; TabY = tabPos != null ? tabPos[3] : 0.287;
            double paramY = TabY;
            if (param.Count > 0) TabY -= paramH + 0.004;                    // 孔坐标表放在参数表下方
            CurBox = b;

            ModelDoc2 d;
            DrawingDoc dd = NewDrawing("a3", out d);
            Sheet sh = (Sheet)dd.GetCurrentSheet();
            // 生产图纸: 视图生成时自动加孔中心符号线与回转体轴线 (工程图文档属性, 不影响用户系统选项)
            foreach (var t in new[] { swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarks, swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles,
                                      swUserPreferenceToggle_e.swDetailingAutoInsertCenterLines, swUserPreferenceToggle_e.swDetailingCenterMarkShowLines })
                d.Extension.SetUserPreferenceToggle((int)t, 0, true);
            SheetScale(dd, scale);
            double s1 = 1 / scale;
            double wM = wMain(scale);

            View vMain, vSide = null, vBelow = null;
            var want = new List<Tuple<View, double?, double?>>();       // 请求的视图位置 (投影视图只核对可自由移动的方向)
            if (type == "turn")
            {
                // 端视图(圆) 放右侧, 由其生成轴向全剖视图 A-A 作为主视图
                double ex = gLeft + wM + GX + mh * MM * s1 / 2, ey = gTop - mh * MM * s1 / 2;
                View ve = dd.CreateDrawViewFromModelView3(path, "*前视", ex, ey, 0);
                ve.ScaleDecimal = s1;
                dd.ActivateView(ve.Name);
                double L = Math.Max(dx, dy) * 0.8 * MM;
                d.SketchManager.AddToDB = true;
                SketchSegment ln = d.SketchManager.CreateLine(0, L, 0, 0, -L, 0);
                d.SketchManager.AddToDB = false;
                d.ClearSelection2(true);
                ln.Select4(false, null);
                vMain = dd.CreateSectionViewAt5(gLeft + wM / 2, ey, 0, "A", 0, null, 0);
                if (vMain == null) throw new Exception("剖视图创建失败");
                vSide = ve;
                want.Add(Tuple.Create(ve, (double?)ex, (double?)ey));
                want.Add(Tuple.Create(vMain, (double?)(gLeft + wM / 2), (double?)null));
            }
            else
            {
                double mx = gLeft + wM / 2, my = gTop - mh * MM * s1 / 2;
                vMain = dd.CreateDrawViewFromModelView3(path, mainView, mx, my, 0);
                if (vMain == null) throw new Exception("主视图创建失败");
                vMain.ScaleDecimal = s1;
                d.ClearSelection2(true);
                d.Extension.SelectByID2(vMain.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                vSide = dd.CreateUnfoldedViewAt3(gLeft + wM + GX + depth * MM * s1 / 2, my, 0, false);
                want.Add(Tuple.Create(vMain, (double?)mx, (double?)my));
                want.Add(Tuple.Create(vSide, (double?)(gLeft + wM + GX + depth * MM * s1 / 2), (double?)null));
                if (threeViews)
                {
                    d.ClearSelection2(true);
                    d.Extension.SelectByID2(vMain.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                    vBelow = dd.CreateUnfoldedViewAt3(mx, my - mh * MM * s1 / 2 - GY - depth * MM * s1 / 2, 0, false);
                    want.Add(Tuple.Create(vBelow, (double?)null, (double?)(my - mh * MM * s1 / 2 - GY - depth * MM * s1 / 2)));
                }
            }
            SheetScale(dd, scale);          // 插入首个视图时 SolidWorks 可能自动改图纸比例, 放完视图后再设一次
            foreach (View v in new[] { vMain, vSide, vBelow }) if (v != null) try { v.UseSheetScale = 1; } catch { }   // 视图标注只注 "A-A", 比例见标题栏
            d.ForceRebuild3(false);
            if (want.Aggregate(false, (moved, w) => Place(w.Item1, w.Item2, w.Item3) | moved)) d.ForceRebuild3(false);

            // ---- 尺寸 ----
            int nd = 0;
            var marks = new List<ExtentMark>();              // 外形尺寸 (存盘前取位置记入图纸目录, 后处理从 PDF 读出标注值核对)
            // 断开段在主视图局部坐标 (模型尺度, 相对视图中心) 中的范围, 供回转体直径尺寸避让
            var cutLocal = new List<double[]>();
            foreach (var c in cuts)
            {
                double[] ps0 = (double[])vMain.Position, mid = { (b[0] + b[3]) / 2, (b[1] + b[4]) / 2, (b[2] + b[5]) / 2 };
                double[] p1 = (double[])mid.Clone(), p2 = (double[])mid.Clone(); p1[la] = c[0]; p2[la] = c[1];
                double u1 = (ToSheet(vMain, p1[0], p1[1], p1[2])[0] - ps0[0]) / vMain.ScaleDecimal, u2 = (ToSheet(vMain, p2[0], p2[1], p2[2])[0] - ps0[0]) / vMain.ScaleDecimal;
                cutLocal.Add(new[] { Math.Min(u1, u2), Math.Max(u1, u2) });
            }
            try { nd += type == "turn" ? TurnDims(d, vMain, fits, cutLocal, ParseDias(Sw.GetProp(m, "_DIAS"))) : Overall(d, vMain, marks, true, true); } catch (Exception e) { Sw.Log("   尺寸(主) " + e.Message); }
            // A3 标题栏 (右下 180×56, 图框边距 5) 上方: 侧视图若低到其上方附近, 水平尺寸改标在视图上方
            try
            {
                if (vSide != null && type != "turn")
                {
                    double[] so = (double[])vSide.GetOutline();
                    bool low = so[2] > 0.420 - 0.005 - 0.180 && so[1] - 0.020 < 0.005 + 0.056 + 0.004;
                    nd += Overall(d, vSide, marks, true, false, low);
                }
            }
            catch { }
            try { if (vBelow != null) nd += Overall(d, vBelow, marks, false, true); } catch { }
            int nh = 0;
            // 回转体的孔 (端部螺纹孔等) 已在剖视图直径/轴向尺寸中表达;
            // 其余零件: 整圆最多的视图出孔坐标表, 其他含孔视图 (如角座的另一翼) 各自标孔径与孔位
            View tabView = null;
            if (type != "turn") try
            {
                var hvs = new[] { vMain, vSide, vBelow }.Where(v => v != null)
                          .Select(v => new { V = v, N = Geo(v).Count(g => g.Circle && IsHoleEdge(g.Ent)) }).Where(q => q.N > 0)
                          .OrderByDescending(q => q.N).ToList();
                for (int i = 0; i < hvs.Count; i++) nh += Holes(d, dd, hvs[i].V, taps, fits, i == 0, 0);
                if (hvs.Count > 0) tabView = hvs[0].V;
            }
            catch (Exception e) { Sw.Log("   孔标注 " + e.Message); }
            // 断开画法: 尺寸须在断开之前标注 (已标尺寸随几何移动; 断开后再标的尺寸位置会被映射到别处, 实测)
            if (cuts.Count > 0)
                foreach (View v in new[] { vMain, vBelow }) if (v != null) ApplyBreaks(d, dd, v, cuts, la, b);
            // 孔标记 + 孔坐标表: 断开之后按实际图面位置作为图纸注释插入
            if (tabView != null) try { nh += Holes(d, dd, tabView, taps, fits, true, 1); } catch (Exception e) { Sw.Log("   孔坐标表 " + e.Message); }

            // ---- 技术要求 + 粗糙度 (图纸注释: 先激活图纸, 避免挂到断开视图上被重新映射) ----
            dd.ActivateView(((View)dd.GetFirstView()).Name); d.ClearSelection2(true);
            Note(d, string.Join("\n", lines.ToArray()), 0.028, notesTop, 0.0030);
            string ra = Sw.GetProp(m, "_RA");                          // "-" = 不标粗糙度 (如烧成耐火制品)
            if (ra != "-") Note(d, "其余 Ra " + (ra == "" ? "3.2" : ra), 0.372, 0.070, 0.0035);
            if (param.Count > 0) try { ParamTable(dd, param, TabX, paramY); } catch (Exception e) { Sw.Log("   参数表 " + e.Message); }

            d.ForceRebuild3(false);
            // 存盘前再核对一次视图位置: 投影视图建好时在请求位置, 之后的重建中可能被移到父视图另一侧 (见 Place)
            if (want.Aggregate(false, (moved, w) => Place(w.Item1, w.Item2, w.Item3) | moved))
            {
                d.ForceRebuild3(false);
                foreach (var w in want) if (Place(w.Item1, w.Item2, w.Item3)) Sw.Log("   !! 视图 " + w.Item1.Name + " 重建后又离开请求位置");
            }
            string markCol = MarksCol(marks);
            string dir = Path.Combine(DrwRoot, RelDir(path));
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".SLDDRW");
            Sw.Save(d, f);
            int e1 = 0, w1 = 0;
            d.Extension.SaveAs(Path.ChangeExtension(f, ".pdf"), 0, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref e1, ref w1);
            Sw.App.CloseDoc(d.GetTitle());
            Index.Add(new[] { code, name, "A3", ScaleText(scale), Path.Combine(RelDir(path), Path.GetFileName(f)), nd + nh > 0 ? "" : NeedsDims,
                              nd.ToString(), nh.ToString(), "", marks.Count == 0 ? "[]" : "", markCol });
            Sw.Log(string.Format("{0,-16} {1,-22} A3 {2,-6} 尺寸 {3,3}  孔 {4,3}", code, name, ScaleText(scale), nd, nh));
            Sw.Close(m);
        }

        static string RelDir(string path)
        {
            string full = Path.GetFullPath(path), root = Path.GetFullPath(Run.Models);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return "";
            return Path.GetDirectoryName(full.Substring(root.Length).TrimStart('\\'));
        }

        static IEnumerable<string> Wrap(string s, int n)
        {
            var r = new List<string>();
            while (s.Length > n) { r.Add(s.Substring(0, n)); s = "   " + s.Substring(n); }
            r.Add(s);
            return r;
        }

        /// <summary>
        /// 外形尺寸: 最左/最右竖直边 -> 水平尺寸; 最下/最上水平边 -> 竖直尺寸。标注成功的尺寸连同其应有值追加到 marks (存盘前取位置, 见 MarksCol)。
        /// parts = 装配图视图所示装配体的零件实例 (AsmParts, 用于按实例选边); 零件图为 null
        /// </summary>
        static int Overall(ModelDoc2 d, View v, List<ExtentMark> marks, bool horiz, bool vert, bool hAbove = false, List<(Component2 c, double[] xf)> parts = null)
        {
            List<Seg> soft;
            var g = Geo(v, out soft); var vm = new VMap(v);
            double[] ol = (double[])v.GetOutline();
            int n = 0;
            var vl = g.Where(s => !s.Circle && Math.Abs(s.X1 - s.X2) < 1e-7 && Math.Abs(s.Y1 - s.Y2) > 1e-4).ToList();
            var hl = g.Where(s => !s.Circle && Math.Abs(s.Y1 - s.Y2) < 1e-7 && Math.Abs(s.X1 - s.X2) > 1e-4).ToList();
            // 回转体侧视: 端面是侧看的圆边 (软直线); 直线不足两条时用它们标轴向外形 (圆边尺寸取圆心, 圆心在该直线上)
            if (vl.Count < 2) vl.AddRange(soft.Where(s => Math.Abs(s.X1 - s.X2) < 1e-7));
            if (hl.Count < 2) hl.AddRange(soft.Where(s => Math.Abs(s.Y1 - s.Y2) < 1e-7));
            DisplayDimension dm0;
            double mm;
            if (horiz && vl.Count >= 2 && (dm0 = ExtentDim(d, v, vl, s => s.X1, s => Math.Abs(s.Y1 - s.Y2), false,
                                                    (ol[0] + ol[2]) / 2, hAbove ? ol[3] + 0.010 : ol[1] - 0.012, parts, out mm)) != null) { n++; marks.Add(new ExtentMark { Dm = dm0, Geo = mm }); }
            if (vert && hl.Count >= 2 && (dm0 = ExtentDim(d, v, hl, s => s.Y1, s => Math.Abs(s.X1 - s.X2), true,
                                                   ol[0] - 0.012, (ol[1] + ol[3]) / 2, parts, out mm)) != null) { n++; marks.Add(new ExtentMark { Dm = dm0, Geo = mm }); }
            // 圆形外轮廓 (盘、法兰、齿圈的端视图): 没有可标的外形直线时, 标最大整圆直径
            if (horiz && vert && n == 0)
            {
                var c = g.Where(s => s.Circle && !IsHoleEdge(s.Ent)).OrderByDescending(s => s.R).FirstOrDefault();
                if (c != null && Sel(d, v, c, false))
                {
                    double ang = 0.785;
                    var dm = (DisplayDimension)d.AddDiameterDimension2(vm.X(c.Cx) + (c.R * vm.S + 0.010) * Math.Cos(ang), vm.Y(c.Cy) + (c.R * vm.S + 0.010) * Math.Sin(ang), 0);
                    if (dm != null) { n++; marks.Add(new ExtentMark { Dm = dm, Geo = 2 * c.R / MM }); }
                }
            }
            return n;
        }

        /// <summary>
        /// 一个外形尺寸: ls 中坐标 pos 最小与最大的两组边之间 (vertical = 竖直尺寸)。
        /// 每组取与极值相差 0.001 mm 以内的边、长边优先 —— 外轮廓处常有多条重合的边 (面板边、线槽边、端头), 原先只按坐标排序, 选中哪条取决于 1e-12 级的舍入。
        /// 每对边先按记录所在实例的边 (InstanceEnt) 标注, 标不出再按原实体标 (每组前 3 条), 第一个标注成功的即采用。
        /// geoMm = 这对边之间的几何距离 (尺寸应有值)。出图中不读回尺寸值: 是否量对 (选到别的实例时 480 量成 220, api_calibration.md
        /// 第 2 节第 26 条) 由后处理从 PDF 读出标注值与 geoMm 核对 (MarksCol → audit_drawings.py → ApplyExtentReadings) ——
        /// 读的是图纸上实际印出的数, 且不增加出图中的 SolidWorks 调用; 代价是量错时不再当场换边重标, 而是校核不通过。
        /// </summary>
        static DisplayDimension ExtentDim(ModelDoc2 d, View v, List<Seg> ls, Func<Seg, double> pos, Func<Seg, double> len, bool vertical,
                                          double sx, double sy, List<(Component2 c, double[] xf)> parts, out double geoMm)
        {
            geoMm = double.NaN;
            // 极值处不能是样条边: 导入件中短的样条边也以两点折线记录返回, 看似直线; 选中它标注时 SolidWorks 不按记录位置量
            // (实测 2026-10-20, 微波实验炉磁控管电源模块主视图: 顶部螺钉头的样条小段在最高处, 记录 215.95, 标出 208.37 = 箱体上下沿)
            // → 从极值往里逐条核对 (只查到第一条可用的边为止), 可用 = 直线或圆 (侧看的圆边量到圆心, 圆心正在该直线上, 回转体侧视的外形靠它);
            // 轮廓线 (SilhouetteEdge) 照旧可用
            var isLine = new Dictionary<Seg, bool>();
            Func<Seg, bool> straight = s =>
            {
                if (!isLine.TryGetValue(s, out bool ok))
                {
                    ok = true;
                    if (s.Ent is Edge e) try { var cv = (Curve)e.GetCurve(); ok = cv.IsLine() || cv.IsCircle(); } catch { }
                    isLine[s] = ok;
                }
                return ok;
            };
            Seg first = ls.OrderBy(pos).FirstOrDefault(straight), last = ls.OrderByDescending(pos).FirstOrDefault(straight);
            if (first == null || last == null) return null;
            double lo = pos(first), hi = pos(last);
            if (hi - lo <= 1e-4) return null;
            var A = ls.Where(s => Math.Abs(pos(s) - lo) < 1e-6 && straight(s)).OrderByDescending(len).Take(3).ToList();
            var B = ls.Where(s => Math.Abs(hi - pos(s)) < 1e-6 && straight(s)).OrderByDescending(len).Take(3).ToList();
            string what = vertical ? "竖直" : "水平";
            var inst = new Dictionary<Seg, Seg>();
            var miss = new Dictionary<Seg, string>();
            Func<Seg, Seg> at = s =>
            {
                if (!inst.TryGetValue(s, out Seg r))
                {
                    string m;
                    Component2 c;
                    inst[s] = r = new Seg { X1 = s.X1, Y1 = s.Y1, X2 = s.X2, Y2 = s.Y2, Ent = InstanceEnt(v, s, parts, out m, out c) };
                    miss[s] = m;
                }
                return r;
            };
            foreach (var a in A)
                foreach (var b in B)
                    foreach (bool useInst in new[] { true, false })
                    {
                        Seg sa = useInst ? at(a) : a, sb = useInst ? at(b) : b;
                        if (useInst && ReferenceEquals(sa.Ent, a.Ent) && ReferenceEquals(sb.Ent, b.Ent)) continue;     // 两边都没换成实例上的边
                        if (!Sel(d, v, sa, false) || !Sel(d, v, sb, true)) continue;
                        var dm = (DisplayDimension)(vertical ? d.AddVerticalDimension2(sx, sy, 0) : d.AddHorizontalDimension2(sx, sy, 0));
                        if (dm == null) continue;
                        geoMm = (pos(b) - pos(a)) / MM;
                        // 装配图中没能换成实例上的边: 由 SolidWorks 自行挑实例, 可能量错 —— 记下原因, PDF 核对不符时据此排查
                        if (!useInst && parts != null && parts.Count > 0 && (miss[a] != null || miss[b] != null))
                            Sw.Log(string.Format("   ! 视图 {0} {1}外形尺寸按原实体选边 (实例: {2} / {3})", v.Name, what, miss[a] ?? "已定", miss[b] ?? "已定"));
                        return dm;
                    }
            d.ClearSelection2(true);
            return null;
        }

        /// <summary>一个外形尺寸: 尺寸对象与应有值 (按视图几何, mm)。存盘前取其文字位置写入图纸目录, 后处理在 PDF 上按位置读出标注值核对</summary>
        sealed class ExtentMark { public DisplayDimension Dm; public double Geo; }

        /// <summary>
        /// 存盘前 (视图已定位、断开画法已完成): 各外形尺寸文字在图纸上的位置 (Annotation.GetPosition, mm, 原点在图纸左下) 与应有值 →
        /// 图纸目录列 "[[x,y,应有值],…]"; 位置读不出时 x、y 记 null (后处理按读不出计)
        /// </summary>
        static string MarksCol(List<ExtentMark> marks)
        {
            var rows = new List<double[]>();
            foreach (var m in marks)
            {
                double[] p = null;
                try { p = (double[])((Annotation)m.Dm.GetAnnotation()).GetPosition(); } catch (Exception e) { Sw.Log("   外形尺寸位置读不出: " + e.Message); }
                rows.Add(p == null ? new[] { double.NaN, double.NaN, m.Geo } : new[] { p[0] / MM, p[1] / MM, m.Geo });
            }
            return MarksJson(rows);
        }

        /// <summary>[[x,y,应有值],…] (mm) → JSON 文本; NaN 写作 null</summary>
        internal static string MarksJson(IEnumerable<double[]> rows)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            Func<double, string> f = x => double.IsNaN(x) ? "null" : Math.Round(x, 3).ToString("0.###", ci);
            return "[" + string.Join(",", rows.Select(r => "[" + string.Join(",", r.Select(f)) + "]")) + "]";
        }

        /// <summary>图纸目录列 → [[x,y,应有值],…] (null 保留); 空或无法解析为 null</summary>
        internal static List<double?[]> ParseMarks(string col)
        {
            if (string.IsNullOrWhiteSpace(col)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<List<double?[]>>(col); } catch { return null; }
        }

        /// <summary>外形尺寸读数 (mm) → 图纸目录列: 保留 2 位、升序的 JSON 数组文本, 如 "[174.1,480,900]"; 没有外形尺寸为 "[]"</summary>
        internal static string ExtentsCol(IEnumerable<double> mm)
        {
            return "[" + string.Join(",", mm.Select(x => Math.Round(x, 2)).OrderBy(x => x)
                                              .Select(x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))) + "]";
        }

        /// <summary>图纸目录列 → 外形尺寸读数; 空 (旧目录行, 未记录) 或无法解析为 null</summary>
        internal static List<double> ParseExtents(string col)
        {
            if (string.IsNullOrWhiteSpace(col)) return null;
            try { return System.Text.Json.JsonSerializer.Deserialize<List<double>>(col); } catch { return null; }
        }

        /// <summary>回转体 (轴线沿视图水平方向, 过 y=0): 各级直径 (φ + 配合) + 轴向台阶链式尺寸 + 总长</summary>
        /// <summary>"16;31" -> 直径集合 (mm)</summary>
        internal static List<double> ParseDias(string s)
        {
            var r = new List<double>();
            foreach (string t in (s ?? "").Split(';')) { double v; if (double.TryParse(t.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) r.Add(v); }
            return r;
        }

        /// <param name="only">非空时只标注这些直径 (齿轮等非回转特征在剖视图中会投影出伪"直径", 由零件属性 _DIAS 指定)</param>
        static int TurnDims(ModelDoc2 d, View v, Dictionary<double, string> fits, List<double[]> cutLocal = null, List<double> only = null)
        {
            var g = Geo(v); var vm = new VMap(v);
            double[] ol = (double[])v.GetOutline();
            int n = 0;
            var hl = g.Where(s => !s.Circle && Math.Abs(s.Y1 - s.Y2) < 1e-7 && Math.Abs(s.X1 - s.X2) > 5e-4).ToList();
            // 直径: 取每个 |y| 的上下对称边, 按直径去重。
            // 剖视图中同一条轮廓线可能返回两条重合的边 (剖切面边界与轮廓各一条, 实测 m=1.5 太阳轮轴 Ø12), 其中一条标不上尺寸 →
            // 同一直径的每一对候选边都试一次, 标注成功才算完成 (原逻辑只试第一对, 失败后该直径整个漏标)
            var done = new HashSet<long>();
            var usedX = new List<double>();
            int row = 0;
            foreach (var top in hl.Where(s => s.Y1 > 1e-4).OrderByDescending(s => Math.Abs(s.X1 - s.X2)))
            {
                long key = (long)Math.Round(top.Y1 * 2e4);
                if (done.Contains(key)) continue;
                double xm = (top.X1 + top.X2) / 2;
                var bots = hl.Where(s => Math.Abs(s.Y1 + top.Y1) < 1e-6 && Math.Min(s.X1, s.X2) <= xm + 1e-6 && Math.Max(s.X1, s.X2) >= xm - 1e-6).ToList();
                if (bots.Count == 0) continue;
                double dia = top.Y1 * 2 / MM;
                if (only != null && only.Count > 0 && !only.Any(o => Math.Abs(o - dia) < 0.05)) { done.Add(key); continue; }
                double xl = xm + (row % 2 == 0 ? 0.004 : -0.004) * (row / 2 + 1) / vm.S;
                // 将断开的视图: 尺寸不得落在被去掉的段内 (否则断开后被映射到图外), 移到同一轮廓线上最近的保留处
                if (cutLocal != null)
                {
                    double lo = Math.Min(top.X1, top.X2), hi = Math.Max(top.X1, top.X2), mg = 0.006 / vm.S;
                    foreach (var c in cutLocal)
                        if (xl > c[0] - mg && xl < c[1] + mg)
                            xl = (xl - c[0] < c[1] - xl && c[0] - mg > lo) || c[1] + mg >= hi ? c[0] - mg : c[1] + mg;
                }
                double sx = vm.X(xl);
                // 相邻台阶 (轴颈与挡肩) 的直径尺寸互相错开 ≥ 6 mm, 向视图外端方向让位
                double dir = xm >= 0 ? 1 : -1;
                for (int k = 0; k < 20 && usedX.Any(u => Math.Abs(u - sx) < 0.006); k++) sx += dir * 0.007;
                DisplayDimension dm = null;
                foreach (var bot in bots) { dm = DimV(d, v, top, bot, sx, vm.Y(0) + 0.002); if (dm != null) break; }
                if (dm == null) continue;                    // 换同一直径的另一条重合边再试
                done.Add(key); usedX.Add(sx);
                n++; row++;
                dm.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, "<MOD-DIAM>");
                string fit = Near(fits, dia);
                if (fit != null)
                {
                    try
                    {
                        DimensionTolerance tol = ((Dimension)dm.GetDimension2(0)).Tolerance;
                        tol.Type = (int)swTolType_e.swTolFIT;
                        if (char.IsUpper(fit[0])) tol.SetFitValues(fit, ""); else tol.SetFitValues("", fit);
                    }
                    catch { }
                }
            }
            // 轴向: 竖直边 x 位置 (高度 ≥2mm), 链式
            // 轴向: 竖直边 (伸到轴线以上 ≥2 mm; 实心轴端面是一条穿过轴线的边, 也要计入), 链式
            var vl = g.Where(s => !s.Circle && Math.Abs(s.X1 - s.X2) < 1e-7 && Math.Max(s.Y1, s.Y2) - Math.Max(Math.Min(s.Y1, s.Y2), 0) >= 2e-3).ToList();
            var xs = vl.Select(s => s.X1).OrderBy(x => x).ToList();
            var uniq = new List<Seg>();
            foreach (double x in xs.Distinct())
            {
                if (uniq.Count > 0 && x - uniq[uniq.Count - 1].X1 < 3e-3) continue;
                uniq.Add(vl.Where(s => Math.Abs(s.X1 - x) < 1e-7).OrderByDescending(s => Math.Abs(s.Y1 - s.Y2)).First());
            }
            double yb = ol[1] - 0.010;
            for (int i = 0; i + 1 < uniq.Count; i++)
                if (DimH(d, v, uniq[i], uniq[i + 1], vm.X((uniq[i].X1 + uniq[i + 1].X1) / 2), yb - (i % 2) * 0.006) != null) n++;
            if (uniq.Count > 2 && DimH(d, v, uniq[0], uniq[uniq.Count - 1], vm.X((uniq[0].X1 + uniq[uniq.Count - 1].X1) / 2), yb - 0.016) != null) n++;
            return n;
        }

        /// <summary>
        /// 孔: 按直径分组, 每组一个直径标注 "n×φd" (螺纹孔 "n×M12-6H");
        /// 孔多于 4 个时生成孔坐标表 (基准 = 视图左下角), 并在孔旁注孔标记 A1/B2...
        /// </summary>
        static double TabX = 0.307, TabY = 0.287;       // 孔坐标表左上角 (图纸坐标)
        static double[] CurBox;                          // 当前零件包围盒 (mm)

        /// <summary>孔圆心相对视图外形左下角的真实坐标 (mm): 由模型几何经 ModelToViewTransform 换算, 不受断开画法影响</summary>
        static double[] HoleXY(View v, Seg h, double lx0, double ly0)
        {
            Edge e = h.Ent as Edge;
            if (e == null || CurBox == null) return new[] { (h.Cx - lx0) / MM, (h.Cy - ly0) / MM };
            double[] cp = (double[])((Curve)e.GetCurve()).CircleParams;
            double[] c = ToSheet(v, cp[0] / MM, cp[1] / MM, cp[2] / MM);
            double sx0 = double.MaxValue, sy0 = double.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                double[] q = ToSheet(v, CurBox[(i & 1) == 0 ? 0 : 3], CurBox[(i & 2) == 0 ? 1 : 4], CurBox[(i & 4) == 0 ? 2 : 5]);
                sx0 = Math.Min(sx0, q[0]); sy0 = Math.Min(sy0, q[1]);
            }
            double sc = v.ScaleDecimal;
            return new[] { (c[0] - sx0) / sc / MM, (c[1] - sy0) / sc / MM };
        }

        static int Holes(ModelDoc2 d, DrawingDoc dd, View v, Dictionary<double, string> taps, Dictionary<double, string> fits, bool table, int phase)
        {
            var g = Geo(v).Where(s => s.Circle && IsHoleEdge(s.Ent)).ToList();
            if (g.Count == 0) return 0;
            var vm = new VMap(v);
            double[] ol = (double[])v.GetOutline();
            // 视图外形左下角 (视图局部坐标)
            var all = Geo(v).Where(s => !s.Circle).ToList();
            double lx0 = all.Count > 0 ? all.Min(s => Math.Min(s.X1, s.X2)) : g.Min(s => s.Cx - s.R);
            double ly0 = all.Count > 0 ? all.Min(s => Math.Min(s.Y1, s.Y2)) : g.Min(s => s.Cy - s.R);
            // 去除同心重复 (沉孔/台阶孔取最大圆)
            var holes = new List<Seg>();
            foreach (var c in g.OrderByDescending(s => s.R))
                if (!holes.Any(h => Math.Abs(h.Cx - c.Cx) < 1e-5 && Math.Abs(h.Cy - c.Cy) < 1e-5)) holes.Add(c);
            var groups = holes.GroupBy(h => Math.Round(h.R * 2 / MM, 1)).OrderByDescending(k => k.Key).ToList();
            int n = 0; char tag = 'A';
            var rows = new List<string[]>();
            // 出孔坐标表时孔径/螺纹规格只列在表中 (规格列), 图上只注孔标记 —— 引线标注会压到相邻孔标记上
            bool useTable = table && holes.Count > 4;
            // phase 0 = 断开前: 孔径引线与孔位尺寸 (无孔坐标表时); phase 1 = 断开后: 孔标记 + 孔坐标表
            if ((phase == 0) == useTable) return 0;
            if (phase == 1) { dd.ActivateView(((View)dd.GetFirstView()).Name); d.ClearSelection2(true); }   // 孔标记作图纸注释
            foreach (var grp in groups)
            {
                double dia = grp.Key;
                var first = grp.OrderBy(h => h.Cy).ThenBy(h => h.Cx).First();
                string tap = Near(taps, dia), fit = Near(fits, dia);
                string lbl = tap != null ? tap.Split(' ')[0] : "φ" + dia.ToString("0.#");
                string depth = tap != null && tap.Contains(" ") ? " " + tap.Substring(tap.IndexOf(' ') + 1) : "";
                if (!useTable) try
                {
                    d.ClearSelection2(true);
                    if (v.SelectEntity(first.Ent, false))
                    {
                        double ang = 0.785;
                        DisplayDimension dm = (DisplayDimension)d.AddDiameterDimension2(vm.X(first.Cx) + (first.R * vm.S + 0.008) * Math.Cos(ang),
                                                                                       vm.Y(first.Cy) + (first.R * vm.S + 0.008) * Math.Sin(ang), 0);
                        if (dm != null)
                        {
                            n++;
                            string pre = grp.Count() > 1 ? grp.Count() + "×" : "";
                            if (tap != null) { dm.ShowDimensionValue = false; dm.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, pre + lbl + depth); }
                            else
                            {
                                dm.SetText((int)swDimensionTextParts_e.swDimensionTextPrefix, pre + "<MOD-DIAM>");
                                if (fit != null)
                                {
                                    try
                                    {
                                        DimensionTolerance tol = ((Dimension)dm.GetDimension2(0)).Tolerance;
                                        tol.Type = (int)swTolType_e.swTolFIT; tol.SetFitValues(fit, "");
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                catch { }
                if (!useTable) continue;
                int k = 1;
                foreach (var h in grp.OrderBy(q => Math.Round(q.Cy, 4)).ThenBy(q => q.Cx))
                {
                    string t = tag.ToString() + k++;
                    double[] hxy = HoleXY(v, h, lx0, ly0);
                    rows.Add(new[] { t, hxy[0].ToString("0.0"), hxy[1].ToString("0.0"),
                                     lbl + (fit != null && tap == null ? fit : "") + depth });
                    Note(d, t, vm.X(h.Cx) + h.R * vm.S * 0.7 + 0.0005, vm.Y(h.Cy) - h.R * vm.S * 0.7 - 0.0003, 0.0018);   // 孔右下方
                    n++;
                }
                tag++;
            }
            if (rows.Count > 44) Sw.Log("   ! 孔坐标表超过 44 行, 已截断 (" + rows.Count + ")");
            if (holes.Count <= 4) n += HolePos(d, v, holes, all, ol);
            if (useTable && rows.Count > 0)
            {
                int nr = Math.Min(rows.Count, 44);
                TableAnnotation ta = dd.InsertTableAnnotation2(false, TabX, TabY, 1, "", nr + 1, 4);
                if (ta != null)
                {
                    ta.set_Text(0, 0, "孔"); ta.set_Text(0, 1, "X"); ta.set_Text(0, 2, "Y"); ta.set_Text(0, 3, "规格");
                    for (int i = 0; i < nr; i++) for (int j = 0; j < 4; j++) ta.set_Text(i + 1, j, rows[i][j]);
                    try
                    {
                        TextFormat tf = ta.GetTextFormat(); tf.CharHeight = 0.0022; ta.SetTextFormat(false, tf);
                        ta.SetColumnWidth(0, 0.010, 0); ta.SetColumnWidth(1, 0.020, 0); ta.SetColumnWidth(2, 0.020, 0); ta.SetColumnWidth(3, 0.046, 0);
                        for (int i = 0; i <= nr; i++) ta.SetRowHeight(i, 0.0042, 0);
                    }
                    catch { }
                }
                Note(d, "孔坐标基准 O: 视图外形左下角", vm.X(lx0), vm.Y(ly0) - 0.004, 0.0022);
            }
            return n + (table ? rows.Count : 0);
        }

        // =====================================================================================
        /// <summary>DXF: 选取法向与 _DXF 一致的最大平面, 1:1 导出轮廓 (激光切割/折弯展开)</summary>
        static void Dxf(ModelDoc2 m, string path, string normal)
        {
            Sw.Activate(m);
            Face2 best = null; double ba = 0;
            object[] bodies = (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true);
            foreach (object bo in bodies)
            {
                object[] faces = (object[])((Body2)bo).GetFaces();
                foreach (object fo in faces)
                {
                    Face2 f = (Face2)fo;
                    Surface su = (Surface)f.GetSurface();
                    if (!su.IsPlane()) continue;
                    double[] pp = (double[])su.PlaneParams;   // nx ny nz px py pz
                    double c = normal == "X" ? Math.Abs(pp[0]) : normal == "Y" ? Math.Abs(pp[1]) : Math.Abs(pp[2]);
                    if (c < 0.999) continue;
                    double a = f.GetArea();
                    if (a > ba) { ba = a; best = f; }
                }
            }
            if (best == null) { Sw.Log("   DXF: 无合适平面"); return; }
            m.ClearSelection2(true);
            ((Entity)best).Select4(false, null);
            string dir = Path.Combine(DxfRoot, RelDir(path));
            Directory.CreateDirectory(dir);
            string f2 = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ".dxf");
            bool ok = ((PartDoc)m).ExportToDWG2(f2, path, (int)swExportToDWG_e.swExportToDWG_ExportSelectedFacesOrLoops, true, null, false, false, 0, null);
            if (!ok) Sw.Log("   DXF 导出失败"); else nDxf++;
        }

        // =====================================================================================
        static readonly double[] ASM_SCALES = { 1, 1.5, 2, 2.5, 3, 4, 5, 6, 10, 15, 20, 25, 30, 40, 50, 60, 100 };
        // 装配图版面: 明细表宽, 技术要求区高, 图框右 / 左边距 (GB/T 14689)
        const double BOMW = 0.195, NOTEH = 0.075, FR = 0.010, FL = 0.025;
        /// <summary>视图组外框允许超出可用区的量: 一个注释外扩量 (见 AsmDrawing 版面校核)</summary>
        const double FIT_TOL = 0.006;

        /// <summary>装配图图幅与主+俯视图组可用区: [宽, 高, L, R, B, T] (图纸坐标, m)</summary>
        static double[] AsmSheet(string size)
        {
            double W = size == "a0" ? 1.189 : 0.594, H = size == "a0" ? 0.841 : 0.42;
            return new[] { W, H, FL + 0.004, W - FR - BOMW - 0.012, NOTEH + 0.005, H - FR - 0.004 };
        }

        /// <summary>
        /// 装配图的主视图 (可用区左上) + 俯视图 (主视图下方, 第一角) + 外形尺寸 + 自动序号, 比例 1:scale。
        /// AsmDrawing 逐级比例尝试与 cad probe-asm 共用。draft = 草稿品质消隐 (总装)。am = 已打开的该装配体 (path)。
        /// </summary>
        static void AsmViews(DrawingDoc dd, ModelDoc2 d, ModelDoc2 am, string path, double[] sh, double scale, double dx, double dy, double dz, bool draft, bool topBom,
                             out View vf, out View vt, out int nd, out int nb, out List<ExtentMark> marks)
        {
            double s1 = 1 / scale, L = sh[2], T = sh[5];
            SheetScale(dd, scale);
            double mx = L + 0.03 + dx * MM * s1 / 2, my = T - 0.03 - dy * MM * s1 / 2;
            vf = dd.CreateDrawViewFromModelView3(path, "*前视", mx, my, 0);
            if (vf == null) throw new Exception("主视图创建失败");
            vf.ScaleDecimal = s1;
            d.ClearSelection2(true);
            d.Extension.SelectByID2(vf.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
            double ty = my - dy * MM * s1 / 2 - 0.035 - dz * MM * s1 / 2;
            vt = dd.CreateUnfoldedViewAt3(mx, ty, 0, false);
            if (draft)
                // 总装 2993 个零部件: 主/俯视图用草稿品质消隐 (精确消隐计算量过大)
                foreach (View v in new[] { vf, vt }) try { v.SetDisplayMode3(false, (int)swDisplayMode_e.swHIDDEN, true, false); } catch { }
            SheetScale(dd, scale);          // 插入首个视图时 SolidWorks 可能自动改图纸比例
            d.ForceRebuild3(false);
            if (Place(vf, mx, my) | Place(vt, null, ty)) d.ForceRebuild3(false);
            nd = 0; nb = 0; marks = new List<ExtentMark>();
            var parts = AsmParts(am as AssemblyDoc);
            try { nd += Overall(d, vf, marks, true, true, parts: parts); nd += Overall(d, vt, marks, false, true, parts: parts); } catch { }
            try { nb = Balloons(dd, d, vf, topBom); }
            catch (Exception e) { Sw.Log("   序号 " + e.Message); }
            d.ForceRebuild3(false);
            try { FixBalloons(d, vf); }
            catch (Exception e) { Sw.Log("   序号位置 " + e.Message); }
        }

        /// <summary>序号圆 [圆心 x, y, 半径] (图纸坐标, m); 读不出返回 null</summary>
        static double[] Circle(Note n)
        {
            double[] bi = n.GetBalloonInfo() as double[];
            return bi == null || bi.Length < 7 || bi[6] <= 0 ? null : new[] { bi[0], bi[1], bi[6] };
        }

        /// <summary>
        /// 序号的存储位置 (文字框左上角) 在序号圆旁: 距圆心不超过 2 × 半径 + 5 mm。
        /// 正常序号实测最多 4.6 mm (半径 3 mm, 随引线方向与字数而变); 不一致的序号偏出 100 mm 以上
        /// </summary>
        static bool Consistent(double[] p, double[] c)
        {
            return Math.Sqrt((p[0] - c[0]) * (p[0] - c[0]) + (p[1] - c[1]) * (p[1] - c[1])) <= 2 * c[2] + 0.005;
        }

        /// <summary>
        /// 自动序号的存储位置与序号圆不一致时, 移回序号圆 (自动布局排定的位置)。实测 (2026-10-16/17, cad probe-asm): 自动序号偶尔
        /// Annotation.GetPosition (存储的位置; 保存后重新打开即按它画, PDF 由此导出) 与 Note.GetBalloonInfo 的序号圆相距很远, 重建不改变两者。
        /// 微波实验炉总装图删除重复序号后 8 次中 4 次: 一个序号存到了视图上方 146 mm 外 (引线斜穿主视图), 一个与别的序号叠在同一处 ——
        /// 存储位置是被删掉的同项序号原来的空位, 序号圆在它自己的空位上。版面校核量的是存储位置, 这类序号使视图组外框虚大
        /// (箱体模块装配图 2026-10-15 量成 192×406 mm、改用 1:15 属同类现象, 未复现, 见 api_calibration.md 第 2 节第 22、25 条)。
        /// → 把位置设到序号圆心, 重建后按新圆心与目标之差再修一次; 仍不一致记日志 (版面校核对序号按存储位置与序号圆的并集计, 见 Ext)。返回移回的个数。
        /// </summary>
        static int FixBalloons(ModelDoc2 d, View v)
        {
            int moved = 0;
            foreach (Note n in BomBalloons(v))
            {
                Annotation a = (Annotation)n.GetAnnotation();
                double[] c0 = Circle(n), p0 = a.GetPosition() as double[];
                if (c0 == null || p0 == null || Consistent(p0, c0)) continue;
                double x = c0[0], y = c0[1];
                double[] c = c0, p = p0;
                for (int k = 0; k < 2; k++)
                {
                    a.SetPosition2(x, y, 0);
                    d.ForceRebuild3(false);
                    c = Circle(n); p = a.GetPosition() as double[];
                    if (c == null || p == null) break;
                    double ex = c0[0] - c[0], ey = c0[1] - c[1];
                    if (Math.Abs(ex) < 0.001 && Math.Abs(ey) < 0.001) break;
                    x = p[0] + ex; y = p[1] + ey;
                }
                bool ok = c != null && p != null && Consistent(p, c) && Math.Abs(c[0] - c0[0]) < 0.002 && Math.Abs(c[1] - c0[1]) < 0.002;
                if (ok) moved++;
                Sw.Log(string.Format("   {0} 序号 {1} \"{2}\" 存储位置 ({3:F1},{4:F1}) 不在序号圆 ({5:F1},{6:F1}) 旁{7}", ok ? "" : "!", a.GetName(), (n.GetText() ?? "").Trim(),
                    p0[0] * 1000, p0[1] * 1000, c0[0] * 1000, c0[1] * 1000,
                    ok ? ", 已移回" : string.Format(", 移动后仍不一致: 位置 ({0:F1},{1:F1}) 序号圆 ({2:F1},{3:F1})",
                        p == null ? double.NaN : p[0] * 1000, p == null ? double.NaN : p[1] * 1000, c == null ? double.NaN : c[0] * 1000, c == null ? double.NaN : c[1] * 1000)));
            }
            return moved;
        }

        /// <summary>
        /// 主视图自动序号, 每个明细表项一个。自动序号在插入明细表之前生成, 挂在叶零件上, IgnoreMultiple 只忽略同一零件的其余实例;
        /// 顶层明细表 (子装配占一行) 插入后, 同一子装配里不同零件上的序号都显示该子装配的项号 —— 微波实验炉总装图 4 项 8 个序号
        /// (项 1 四个、项 2 三个, 2026-10-10 起每次如此)。→ 按序号所指零部件归到明细表项 (顶层明细取其顶层零部件, 否则取零件本身;
        /// 文件 + 配置), 每项只留自动序号返回顺序中的第一个, 其余删除。返回留下的序号数。
        /// </summary>
        static int Balloons(DrawingDoc dd, ModelDoc2 d, View vf, bool topBom)
        {
            d.ClearSelection2(true);
            d.Extension.SelectByID2(vf.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
            AutoBalloonOptions ab = (AutoBalloonOptions)dd.CreateAutoBalloonOptions();
            ab.Layout = (int)swBalloonLayoutType_e.swDetailingBalloonLayout_Square;
            ab.IgnoreMultiple = true;
            object[] notes = dd.AutoBalloon5(ab) as object[];
            if (notes == null) return 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dup = new List<Annotation>();
            int unknown = 0;
            foreach (object o in notes)
            {
                Annotation a = (Annotation)((Note)o).GetAnnotation();
                string key = BomKey(a, topBom);
                if (key == null) unknown++;
                else if (!seen.Add(key)) dup.Add(a);
            }
            if (unknown > 0) Sw.Log(string.Format("   ! {0} 个序号读不出所指零部件, 保留 (明细表插入后按项号复核)", unknown));
            if (dup.Count == 0) return notes.Length;
            int kept = notes.Length - DeleteAnnotations(d, vf, dup);
            Sw.Log(string.Format("   序号: 自动生成 {0} 个, 同一明细表项的 {1} 个已删除, 留 {2} 个", notes.Length, dup.Count, kept));
            return kept;
        }

        /// <summary>明细表 (标题栏上方右对齐) 并复核序号每项一个; 返回复核后的序号数 nb</summary>
        static int AsmBom(ModelDoc2 am, ModelDoc2 d, View vf, double W, double H, bool topBom, int nb)
        {
            string exe = Sw.App.GetExecutablePath();
            string tpl = Path.Combine(exe, @"lang\chinese-simplified\gb-bom-material.sldbomtbt");
            if (!File.Exists(tpl)) tpl = Path.Combine(Path.GetDirectoryName(exe), @"lang\chinese-simplified\gb-bom-material.sldbomtbt");
            if (!File.Exists(tpl)) { Sw.Log("   ! 未找到 GB 明细表模板, 用默认模板"); tpl = ""; }
            try
            {
                int bt = topBom ? (int)swBomType_e.swBomType_TopLevelOnly : (int)swBomType_e.swBomType_PartsOnly;
                // 配置名须明确给出: 总装 (顶层明细) 传 "" 时 InsertBomTable4 静默返回 null
                string cfgName = ((Configuration)am.GetActiveConfiguration()).Name;
                BomTableAnnotation bom = vf.InsertBomTable4(false, W - FR, 0.066, (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomRight,
                    bt, cfgName, tpl, false, (int)swNumberingType_e.swNumberingType_Flat, false);
                if (bom != null) { FitBom(bom, H - FR - 0.004 - 0.066); nb = UniqueItems(d, vf, nb); }
                else Sw.Log("   ! 明细表插入失败 (配置 " + cfgName + ")");
            }
            catch (Exception e) { Sw.Log("   明细表 " + e.Message); }
            return nb;
        }

        /// <summary>序号所指零部件对应的明细表项 (文件|配置); 顶层明细取其顶层零部件。读不出返回 null</summary>
        static string BomKey(Annotation a, bool topBom)
        {
            object[] ents = a.GetAttachedEntities3() as object[];
            if (ents == null) return null;
            foreach (object e in ents)
            {
                Component2 c = null;
                try { c = (Component2)((Entity)e).GetComponent(); } catch { }
                if (c == null) continue;
                if (topBom) for (Component2 p = (Component2)c.GetParent(); p != null; p = (Component2)p.GetParent()) c = p;
                return c.GetPathName() + "|" + c.ReferencedConfiguration;
            }
            return null;
        }

        /// <summary>视图中的明细表序号</summary>
        static List<Note> BomBalloons(View v)
        {
            return ((object[])v.GetNotes() ?? new object[0]).Cast<Note>().Where(n => n.IsBomBalloon()).ToList();
        }

        /// <summary>删除视图 v 中的这些序号, 返回实际删除数 (按删除前后视图中的序号数); 与应删数不符时记日志</summary>
        static int DeleteAnnotations(ModelDoc2 d, View v, List<Annotation> del)
        {
            int before = BomBalloons(v).Count;
            d.ClearSelection2(true);
            foreach (Annotation a in del) a.Select3(true, null);
            d.Extension.DeleteSelection2(0);
            d.ClearSelection2(true);
            int n = before - BomBalloons(v).Count;
            if (n != del.Count) Sw.Log(string.Format("   ! 应删除 {0} 个序号, 实际删除 {1} 个", del.Count, n));
            return n;
        }

        /// <summary>
        /// 明细表插入后按序号显示的项号复核每项只有一个序号 (Balloons 读不出所指零部件的序号在这里补删)。返回删除后的序号数。
        /// </summary>
        static int UniqueItems(ModelDoc2 d, View vf, int nb)
        {
            d.ForceRebuild3(false);
            var seen = new HashSet<string>();
            var dup = BomBalloons(vf).Where(n => { string t = (n.GetText() ?? "").Trim(); return t != "" && !seen.Add(t); })
                                     .Select(n => (Annotation)n.GetAnnotation()).ToList();
            if (dup.Count == 0) return nb;
            Sw.Log(string.Format("   ! 明细表插入后仍有 {0} 个序号与其它序号同项号, 删除", dup.Count));
            return nb - DeleteAnnotations(d, vf, dup);
        }

        /// <summary>
        /// 装配图: 主视图 (左上) + 俯视图 (主视图下方, 第一角) + 轴测图 (取右侧或下方空白中比例较大者);
        /// 明细表在标题栏上方 (右侧), 行高按图幅可用高度收缩; 技术要求在左下角。
        /// </summary>
        static void AsmDrawing(string path)
        {
            ModelDoc2 am = Sw.Open(path);
            // 零部件 > 500 (M05、总装) 按大型装配体模式轻化打开: 零件未载入 → 质量统计为 0、总装明细表无行。先全部还原
            try { ((AssemblyDoc)am).ResolveAllLightWeightComponents(false); } catch { }
            string code = Path.GetFileNameWithoutExtension(path), no, nm;
            AsmBuild.CodeName(path, out no, out nm);
            // 标题栏属性写入装配体 (标题栏引用 $PRPSHEET, 取自视图模型)
            // 设备质量 = 各零件 "质量" 属性之和, 不含工艺物料 (铜箔卷、走带)
            double kg = 0;
            foreach (object co in (object[])((AssemblyDoc)am).GetComponents(false) ?? new object[0])
            {
                Component2 c = (Component2)co;
                if (c.IsSuppressed()) continue;
                ModelDoc2 cm = (ModelDoc2)c.GetModelDoc2();
                if (cm == null || cm.GetType() != (int)swDocumentTypes_e.swDocPART || Sw.GetProp(cm, "类别") == "工艺物料") continue;
                double v; if (double.TryParse(Sw.GetProp(cm, "质量"), out v)) kg += v;
            }
            Sw.Prop(am, "代号", no); Sw.Prop(am, "零件号", no); Sw.Prop(am, "名称", nm); Sw.Prop(am, "材料", " ");
            Sw.Prop(am, "质量", kg.ToString("0.0")); Sw.Prop(am, "版本", Cat.Version); Sw.Prop(am, "设计日期", Cat.Date);
            int se = 0, sw = 0;
            am.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref se, ref sw);

            double[] b = (double[])((AssemblyDoc)am).GetBox(0);
            double dx = (b[3] - b[0]) / MM, dy = (b[4] - b[1]) / MM, dz = (b[5] - b[2]) / MM;
            bool top = ForceA0.Contains(path), topBom = top || TopLevelBom.Contains(path);
            // 图幅: A2 能以 1:20 或更大比例放下则用 A2, 否则 A0 (总装图固定 A0)。
            // 不用 GB A1 模板: 该模板新建的工程图导出 PDF 时视图全部丢失 (A0/A2/A3 正常, 已逐项排查文档属性与页面设置)
            string size = top ? "a0" : "a2";
            double W = 0, H = 0, scale = 0;
            ModelDoc2 d = null; DrawingDoc dd = null; View vf = null, vt = null;
            double[] g = null;                               // 主+俯视图组 (含尺寸、序号) 的实际外框
            int nd = 0, nb = 0;                              // 外形尺寸数, 序号数 (最终采用的比例下)
            List<ExtentMark> marks = null;                   // 外形尺寸 (同上)
            for (int si = 0; ; si++)
            {
                double[] sh = AsmSheet(size);
                W = sh[0]; H = sh[1];
                double L = sh[2], R = sh[3], B = sh[4], T = sh[5];
                // 起始比例: 视图本体放得下的最大比例; 之后逐级缩小直到连同尺寸、序号放进图框
                var cand = ASM_SCALES.Where(sc => dx * MM / sc <= (R - L) * 0.85 && (dy + dz) * MM / sc + 0.03 <= (T - B) * 0.85).ToList();
                if (si >= cand.Count || (size == "a2" && cand[si] > 20))
                {
                    if (size == "a2") { size = "a0"; si = -1; continue; }
                    throw new Exception("A0 图幅仍放不下");
                }
                scale = cand[si];
                dd = NewDrawing(size, out d);
                AsmViews(dd, d, am, path, sh, scale, dx, dy, dz, top, topBom, out vf, out vt, out nd, out nb, out marks);
                // ---- 版面校核: 视图 + 尺寸 + 序号整体须在 [L,R]×[B,T] 内; 放不下则换小一级比例重画 ----
                // 注释按 ±6 mm 外扩估算, 而自动序号的位置每次相差几毫米: 行星齿轮装配图 1:1.5 实测 331 mm 对可用 326 mm,
                // 6 次运行中 2 次改用 1:2 (一次超出 5 mm, 一次为图纸外的异常读数, 见 Ext), 比例随运行变化 (回归基准抓到)。允许超出不多于一个外扩量 FIT_TOL;
                // 是否真正出框由 PDF 版面审核 (python/audit_drawings.py) 判定
                g = Union(Ext(vf), Ext(vt));
                if (g[2] - g[0] <= R - L + FIT_TOL && g[3] - g[1] <= T - B + FIT_TOL)
                {
                    double sx = Shift(g[0], g[2], L, R), sy = Shift(g[1], g[3], B, T);
                    if (sx != 0 || sy != 0)
                    {
                        foreach (View v in new[] { vf, vt })
                        {
                            double[] p = (double[])v.Position;
                            v.Position = new[] { p[0] + sx, p[1] + sy };
                        }
                        d.ForceRebuild3(false);
                        g = Union(Ext(vf), Ext(vt));
                    }
                    if (g[0] >= L - FIT_TOL && g[2] <= R + FIT_TOL && g[1] >= B - FIT_TOL && g[3] <= T + FIT_TOL)
                    {
                        Sw.Log(string.Format("   {0} 1:{1} 连同尺寸/序号 {2:F0}×{3:F0} mm, 可用区 {4:F0}×{5:F0}",
                            size.ToUpper(), scale, (g[2] - g[0]) * 1000, (g[3] - g[1]) * 1000, (R - L) * 1000, (T - B) * 1000));
                        break;
                    }
                }
                Sw.Log(string.Format("   {0} 1:{1} 连同尺寸/序号 {2:F0}×{3:F0} mm 超出可用区 {4:F0}×{5:F0}, 改小比例",
                    size.ToUpper(), scale, (g[2] - g[0]) * 1000, (g[3] - g[1]) * 1000, (R - L) * 1000, (T - B) * 1000));
                Sw.App.CloseDoc(d.GetTitle());
            }

            // ---- 明细表 ----
            var stage = System.Diagnostics.Stopwatch.StartNew();
            nb = AsmBom(am, d, vf, W, H, topBom, nb);
            Sw.Log(string.Format("   明细表 {0:F0} s", stage.Elapsed.TotalSeconds)); stage.Restart();

            // ---- 轴测图: 候选 A = 视图组右侧, B = 视图组下方 (技术要求右侧); 取比例较大者, 放置后按实际外框校核 ----
            double iw = (dx + dz) * 0.707 * MM, ih = (dy * 0.816 + (dx + dz) * 0.408) * MM;
            double[][] reg = {
                new[] { g[2] + 0.015, NOTEH + 0.005, W - FR - BOMW - 0.012, H - FR - 0.004 },
                new[] { 0.24, FR + 0.004, W - FR - BOMW - 0.012, g[1] - 0.010 } };
            double isoS = 0; double[] ir = null;
            foreach (var r in reg)
            {
                double rw = r[2] - r[0], rh = r[3] - r[1];
                if (rw <= 0.05 || rh <= 0.05) continue;
                double fit = 0.9 * Math.Min(rw / iw, rh / ih), pick = 0;
                foreach (double sc in ASM_SCALES) if (1 / sc <= fit) { pick = 1 / sc; break; }
                if (pick > isoS) { isoS = pick; ir = r; }
            }
            if (ir != null)
            {
                View vi = dd.CreateDrawViewFromModelView3(path, "*等轴测", (ir[0] + ir[2]) / 2, (ir[1] + ir[3]) / 2, 0);
                try { vi.SetDisplayMode3(false, (int)swDisplayMode_e.swSHADED_EDGES, false, true); } catch { }
                foreach (double sc in ASM_SCALES.Where(q => 1 / q <= isoS))
                {
                    vi.ScaleDecimal = 1 / sc; isoS = 1 / sc;
                    d.ForceRebuild3(false);
                    double[] o = (double[])vi.GetOutline();
                    if (o[2] - o[0] <= ir[2] - ir[0] && o[3] - o[1] <= ir[3] - ir[1])
                    {
                        double[] p = (double[])vi.Position;
                        vi.Position = new[] { p[0] + ((ir[0] + ir[2]) - (o[0] + o[2])) / 2, p[1] + ((ir[1] + ir[3]) - (o[1] + o[3])) / 2 };
                        break;
                    }
                }
            }
            Sw.Log(string.Format("   轴测图 {0:F0} s", stage.Elapsed.TotalSeconds)); stage.Restart();
            // 技术要求为图纸注释 (先激活图纸, 否则会挂到当前视图上随视图移动)
            dd.ActivateView(((View)dd.GetFirstView()).Name);
            d.ClearSelection2(true);
            Note(d, "技术要求\n" + AsmNotes, FL + 0.005, NOTEH - 0.005, 0.0035);
            d.ForceRebuild3(false);
            string markCol = MarksCol(marks);
            string dir = Path.Combine(DrwRoot, "装配图");
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, code + ".SLDDRW");
            Sw.Save(d, f);
            Sw.App.CloseDoc(d.GetTitle());
            Pdf(f);                          // 重新打开后导出: 大装配体视图后台生成, 新建后立即导出为空白
            Sw.Log(string.Format("   技术要求、保存与导出 PDF {0:F0} s", stage.Elapsed.TotalSeconds));
            Sw.Close(am);
            Index.Add(new[] { no, nm, size.ToUpper(), "1:" + scale, "装配图\\" + Path.GetFileName(f), nd > 0 ? "" : NeedsDims, nd.ToString(), "", nb.ToString(),
                              marks.Count == 0 ? "[]" : "", markCol });
            Sw.Log(string.Format("装配图 {0,-40} {1} 1:{2}  轴测 1:{3}  尺寸 {4}  序号 {5}", code, size.ToUpper(), scale, isoS > 0 ? (1 / isoS).ToString("0") : "-", nd, nb));
        }

        /// <summary>
        /// 视图外框并上其全部注释 (尺寸、序号) 的位置 (图纸坐标, 每个注释按 ±6 mm 计; 序号另并上其序号圆)。
        /// 实测 (2026-10-10, 行星齿轮装配图 4 次中 1 次): 外框连同注释读回 161×4706 mm —— 有一个读数远在图纸之外, 比例因此被无谓缩小一级。
        /// 落在任何图幅 (最大 A0 1189×841) 之外 0.5 m 以上的读数不可能是真实位置: 不计入版面并记日志 (最终版面仍由 PDF 版面审核把关)。
        /// </summary>
        static double[] Ext(View v)
        {
            double[] o = (double[])v.GetOutline();
            if (!OnAnySheet(o[0], o[1]) || !OnAnySheet(o[2], o[3]))
                Sw.Log(string.Format("   ! 视图 {0} 外框读数异常 ({1:F3},{2:F3})-({3:F3},{4:F3})", v.Name, o[0], o[1], o[2], o[3]));
            double x0 = o[0], y0 = o[1], x1 = o[2], y1 = o[3];
            object[] an = (object[])v.GetAnnotations();
            if (an != null)
                foreach (object a in an)
                {
                    double[] p = (double[])((Annotation)a).GetPosition();
                    if (p == null) continue;
                    if (!OnAnySheet(p[0], p[1]))
                    {
                        Sw.Log(string.Format("   ! 视图 {0} 注释位置读数异常 ({1:F3},{2:F3}) 类型 {3}, 不计入版面", v.Name, p[0], p[1], ((Annotation)a).GetType()));
                        continue;
                    }
                    const double r = 0.006;
                    x0 = Math.Min(x0, p[0] - r); y0 = Math.Min(y0, p[1] - r); x1 = Math.Max(x1, p[0] + r); y1 = Math.Max(y1, p[1] + r);
                    // 序号再并上序号圆: 存储位置与序号圆不一致而未能移回时 (见 FixBalloons) 两处都算, 只会更严
                    Note n = ((Annotation)a).GetType() == (int)swAnnotationType_e.swNote ? (Note)((Annotation)a).GetSpecificAnnotation() : null;
                    double[] c = n != null && n.IsBomBalloon() ? Circle(n) : null;
                    if (c != null && OnAnySheet(c[0], c[1]))
                    {
                        x0 = Math.Min(x0, c[0] - c[2]); y0 = Math.Min(y0, c[1] - c[2]); x1 = Math.Max(x1, c[0] + c[2]); y1 = Math.Max(y1, c[1] + c[2]);
                    }
                }
            return new[] { x0, y0, x1, y1 };
        }

        /// <summary>一个注释的各种位置读数 (图纸坐标, m): GetPosition; 序号的圆心与半径 (Bal)、文字框 (Box)、附着点 (Att)</summary>
        class AnnRead { public string View, Name, Kind, Text = ""; public double X, Y; public double[] Bal, Box, Att; }

        static List<AnnRead> Readings(View v)
        {
            var r = new List<AnnRead>();
            foreach (object o in (object[])v.GetAnnotations() ?? new object[0])
            {
                Annotation a = (Annotation)o;
                int t = a.GetType();
                var q = new AnnRead { View = v.Name, Name = a.GetName(), Kind = ((swAnnotationType_e)t).ToString().Replace("sw", "") };
                double[] p = a.GetPosition() as double[];
                if (p != null) { q.X = p[0]; q.Y = p[1]; } else { q.X = q.Y = double.NaN; }
                if (t == (int)swAnnotationType_e.swNote)
                {
                    Note n = (Note)a.GetSpecificAnnotation();
                    q.Text = n.GetText() ?? "";
                    if (n.IsBomBalloon())
                    {
                        q.Kind = "Balloon";
                        double[] bi = n.GetBalloonInfo() as double[];
                        if (bi != null && bi.Length >= 7) q.Bal = new[] { bi[0], bi[1], bi[6] };
                    }
                    double[] e = n.GetExtent() as double[];
                    if (e != null && e.Length >= 6) q.Box = new[] { e[0], e[1], e[3], e[4] };
                    double[] at = n.GetAttachPos() as double[];
                    if (at != null && at.Length >= 2) q.Att = new[] { at[0], at[1] };
                }
                else if (t == (int)swAnnotationType_e.swDisplayDimension)
                    try { q.Text = (((Dimension)((DisplayDimension)a.GetSpecificAnnotation()).GetDimension2(0)).SystemValue * 1000).ToString("0.###") + " mm"; } catch { }
                r.Add(q);
            }
            return r;
        }

        /// <summary>同一注释不同来源的读数互相矛盾之处 (位置读数应在序号圆内 / 文字框旁); 无则 ""</summary>
        static string Conflict(AnnRead q)
        {
            var s = new List<string>();
            if (double.IsNaN(q.X)) s.Add("无位置读数");
            else
            {
                if (q.Bal != null && !Consistent(new[] { q.X, q.Y }, q.Bal)) s.Add("位置不在序号圆旁");
                if (q.Box != null && (q.X < q.Box[0] - 0.003 || q.X > q.Box[2] + 0.003 || q.Y < q.Box[1] - 0.003 || q.Y > q.Box[3] + 0.003)) s.Add("位置不在文字框旁");
            }
            return string.Join(", ", s);
        }

        static string Fmt(AnnRead q)
        {
            Func<double, string> f = x => (x * 1000).ToString("F1");
            return string.Format("{0,-10} {1,-16} {2,-18} pos=({3},{4}){5}{6}{7} \"{8}\"", q.View, q.Name, q.Kind, f(q.X), f(q.Y),
                q.Bal == null ? "" : string.Format(" 圆=({0},{1}) r={2}", f(q.Bal[0]), f(q.Bal[1]), f(q.Bal[2])),
                q.Box == null ? "" : string.Format(" 框=({0},{1})-({2},{3})", f(q.Box[0]), f(q.Box[1]), f(q.Box[2]), f(q.Box[3])),
                q.Att == null ? "" : string.Format(" 附着=({0},{1})", f(q.Att[0]), f(q.Att[1])), q.Text.Replace("\n", " "));
        }

        /// <summary>
        /// 调试 (cad probe-asm): 按装配图流程 (关闭文档 → 打开装配体 → 新建工程图 → 主/俯视图 + 外形尺寸 + 自动序号 → 明细表) 重复 n 次,
        /// 逐个注释输出位置读数 (GetPosition、序号圆心与半径、文字框、附着点) 与视图组外框, 重建后再读一次比对;
        /// 外框与第一次相差 10 mm 以上、重建前后不一致或各来源读数矛盾时标 "!!"。每张图另存到 outDir 并重新打开导出 PDF, 看实际画在哪。
        /// 不写运行目录, 不保存装配体。preload 非空时每次先打开再关闭它, 按流水线的出图顺序 (先总装、后模块)。
        /// 装配体须取自一次完整运行的目录: 复制到别处的 models/ 中, 装配体记的是原运行目录的绝对路径, 零部件会混用两处的同名文件
        /// (2026-10-16 实测: 包围盒读成残缺的、俯视图压在主视图上, SolidWorks 两次在打开时崩溃)。
        /// </summary>
        public static void ProbeAsm(string path, string size, double scale, int n, string outDir, bool topBom, string preload = null)
        {
            double[] sh = AsmSheet(size);
            double[] g1 = null;
            var summary = new List<string>();
            for (int it = 1; it <= n; it++)
            {
                Sw.CloseAll();
                Sw.Recycle(5.0);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (preload != null) { ModelDoc2 pm = Sw.Open(preload); Sw.Close(pm); }
                ModelDoc2 am = Sw.Open(path);
                try { ((AssemblyDoc)am).ResolveAllLightWeightComponents(false); } catch { }
                double[] b = (double[])((AssemblyDoc)am).GetBox(0);
                ModelDoc2 d; DrawingDoc dd = NewDrawing(size, out d);
                View vf, vt; int nd, nb; List<ExtentMark> marks;
                AsmViews(dd, d, am, path, sh, scale, (b[3] - b[0]) / MM, (b[4] - b[1]) / MM, (b[5] - b[2]) / MM, false, topBom, out vf, out vt, out nd, out nb, out marks);
                var r1 = Readings(vf).Concat(Readings(vt)).ToList();
                double[] g = Union(Ext(vf), Ext(vt));
                d.ForceRebuild3(false);
                var r2 = Readings(vf).Concat(Readings(vt)).ToList();
                double[] g2 = Union(Ext(vf), Ext(vt));
                if (g1 == null) g1 = g;
                var bad = new List<string>();
                Func<double[], double> w = q => q[2] - q[0], h = q => q[3] - q[1];
                if (Math.Abs(w(g) - w(g1)) > 0.010 || Math.Abs(h(g) - h(g1)) > 0.010) bad.Add("外框与第 1 次相差 10 mm 以上");
                if (g.Zip(g2, (x, y) => Math.Abs(x - y)).Max() > 0.002) bad.Add("重建后外框变化");
                Sw.Log(string.Format("== 第 {0} 次: 外框 ({1:F1},{2:F1})-({3:F1},{4:F1}) = {5:F0}×{6:F0} mm; 重建后 {7:F0}×{8:F0}; 尺寸 {9} 序号 {10} 外形 [位置 x,y, 应有值] {11}",
                    it, g[0] * 1000, g[1] * 1000, g[2] * 1000, g[3] * 1000, w(g) * 1000, h(g) * 1000, w(g2) * 1000, h(g2) * 1000, nd, nb, MarksCol(marks)));
                foreach (View v in new[] { vf, vt })
                {
                    double[] o = (double[])v.GetOutline(), p = (double[])v.Position;
                    Sw.Log(string.Format("   视图 {0} 位置 ({1:F1},{2:F1}) 外框 ({3:F1},{4:F1})-({5:F1},{6:F1})", v.Name, p[0] * 1000, p[1] * 1000, o[0] * 1000, o[1] * 1000, o[2] * 1000, o[3] * 1000));
                }
                foreach (var q in r1)
                {
                    var q2 = r2.FirstOrDefault(x => x.View == q.View && x.Name == q.Name);
                    string c = Conflict(q);
                    if (q2 == null) c += (c == "" ? "" : ", ") + "重建后消失";
                    else if (Math.Abs(q2.X - q.X) > 0.002 || Math.Abs(q2.Y - q.Y) > 0.002) c += (c == "" ? "" : ", ") + string.Format("重建后位置变为 ({0:F1},{1:F1})", q2.X * 1000, q2.Y * 1000);
                    if (q.X < 0 || q.X > sh[0] || q.Y < 0 || q.Y > sh[1]) c += (c == "" ? "" : ", ") + "图纸外";
                    Sw.Log("   " + Fmt(q) + (c == "" ? "" : "   !! " + c));
                    if (c != "") bad.Add(q.Name + ": " + c);
                }
                int nb2 = AsmBom(am, d, vf, sh[0], sh[1], topBom, nb);
                var items = BomBalloons(vf).Select(x => (x.GetText() ?? "").Trim()).OrderBy(x => x.Length).ThenBy(x => x).ToList();
                Sw.Log(string.Format("   明细表后序号 {0} 个 (视图中 {1} 个): {2}", nb2, items.Count, string.Join(" ", items)));
                string line = string.Format("第 {0,2} 次 {1:F0}×{2:F0} mm  序号 {3}->{4}  {5:F0} s{6}", it, w(g) * 1000, h(g) * 1000, nb, nb2,
                    watch.Elapsed.TotalSeconds, bad.Count == 0 ? "" : "  !! " + string.Join("; ", bad));
                summary.Add(line);
                // 每次都另存并重新打开导出 PDF: 保存后的图纸按存储位置画序号, PDF 就是最终交付的样子
                string f = Path.Combine(outDir, string.Format("probe_{0:00}.SLDDRW", it));
                Sw.Save(d, f);
                Sw.App.CloseDoc(d.GetTitle());
                Pdf(f);
                Sw.Close(am);
            }
            Sw.Log("== 汇总 (" + Path.GetFileName(path) + " " + size.ToUpper() + " 1:" + scale + ")");
            foreach (string s in summary) Sw.Log("   " + s);
        }

        /// <summary>
        /// 核对视图实际位置 (图纸坐标, 视图中心), x / y 为 null 的方向不核对 (投影视图在该方向与父视图对齐, 不能自由移动)。
        /// 实测 (2026-10-10, m=1.5 内齿圈, 4 次运行中 2 次): 侧视图存盘时位于主视图另一侧 (主视图 x=0.141, 侧视图应在 0.250, 实际 0.031),
        /// 压住图框, 版面审核报出框; 建视图后立即读回的位置是对的, 是之后的重建把投影视图移到了对侧 (内齿圈两侧投影相同)。
        /// → 建视图后与存盘前各核对一次, 偏离 20 mm 以上即改回并记日志。返回是否移动过。
        /// </summary>
        static bool Place(View v, double? x, double? y)
        {
            if (v == null) return false;
            double[] p = (double[])v.Position;
            double nx = x ?? p[0], ny = y ?? p[1];
            if (Math.Abs(p[0] - nx) < 0.02 && Math.Abs(p[1] - ny) < 0.02) return false;
            Sw.Log(string.Format("   ! 视图 {0} 位置 ({1:F3},{2:F3}) 偏离请求 ({3:F3},{4:F3}), 已改回", v.Name, p[0], p[1], nx, ny));
            v.Position = new[] { nx, ny };
            return true;
        }

        /// <summary>把区间 [lo,hi] 移进 [a,b] 所需的位移; 区间比 [a,b] 长时居中 (两侧超出相等)</summary>
        static double Shift(double lo, double hi, double a, double b)
        {
            if (hi - lo > b - a) return (a + b) / 2 - (lo + hi) / 2;
            return lo < a ? a - lo : hi > b ? b - hi : 0;
        }

        static bool OnAnySheet(double x, double y) { return x > -0.5 && x < 1.189 + 0.5 && y > -0.5 && y < 0.841 + 0.5; }

        static double[] Union(double[] a, double[] b)
        {
            return new[] { Math.Min(a[0], b[0]), Math.Min(a[1], b[1]), Math.Max(a[2], b[2]), Math.Max(a[3], b[3]) };
        }

        /// <summary>明细表: 材料列改用 GB 牌号属性 "材料", 备注列改为 "类别"; 字高/行高按可用高度收缩</summary>
        static void FitBom(BomTableAnnotation bom, double availH)
        {
            TableAnnotation ta = (TableAnnotation)bom;
            for (int c = 0; c < ta.ColumnCount; c++)
            {
                string t = "";
                for (int r = 0; r < ta.RowCount && t == ""; r++) { string x = ta.get_Text(r, c) ?? ""; if (x == "材料" || x == "备注" || x == "单重") t = x; }
                if (t == "材料") bom.SetColumnCustomProperty(c, "材料");
                if (t == "单重") bom.SetColumnCustomProperty(c, "质量");       // 零件属性 "质量" (kg), 与图幅模板的质量单位无关
                if (t == "备注") bom.SetColumnCustomProperty(c, "类别");
            }
            // 总重 = 数量 × 单重 (kg); 模板中的总重列按文档质量单位计算, A0 模板为 g, 故直接写入数值
            int cQ = -1, cU = -1, cT = -1, hdr = -1;
            for (int r = 0; r < ta.RowCount && hdr < 0; r++)
                for (int c = 0; c < ta.ColumnCount; c++)
                {
                    string x = ta.get_Text(r, c) ?? "";
                    if (x == "数量") { cQ = c; hdr = r; } else if (x == "单重") cU = c; else if (x == "总重") cT = c;
                }
            if (cQ >= 0 && cU >= 0 && cT >= 0)
                for (int r = 0; r < ta.RowCount; r++)
                {
                    double q, u;
                    if (r == hdr || !double.TryParse(ta.get_Text(r, cQ), out q) || !double.TryParse(ta.get_Text(r, cU), out u)) continue;
                    ta.set_Text(r, cT, (q * u).ToString("0.00"));
                }
            int n = ta.RowCount;
            double rh = Math.Max(0.004, Math.Min(0.007, availH / n));
            try
            {
                TextFormat tf = ta.GetTextFormat(); tf.CharHeight = Math.Min(0.0035, rh * 0.5); ta.SetTextFormat(false, tf);
            }
            catch { }
            for (int r = 0; r < n; r++) try { ta.SetRowHeight(r, rh, 0); } catch { }
            if (rh * n > availH + 1e-4) Sw.Log(string.Format("   ! 明细表 {0} 行超出图幅可用高度", n));
        }

        /// <summary>
        /// 整圆边 (孔、凸台) 最多的零件表面: "X+"/"X-"/"Y+"/"Y-"/"Z+"/"Z-" (按圆心位于包围盒中面的哪一侧); 无则 ""。
        /// 例: 底层纵梁的地脚螺杆过孔只开在底面 → "Y-" → 主视图取下视, 否则三视图 (前/左/上) 中孔全为虚线。
        /// </summary>
        static string HoleFace(ModelDoc2 m, double[] box)
        {
            var cnt = new Dictionary<string, int>();
            foreach (string k in new[] { "Z+", "Y+", "X+", "Z-", "Y-", "X-" }) cnt[k] = 0;   // 同数时优先 +, Z
            foreach (object bo in (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true))
                foreach (object eo in (object[])((Body2)bo).GetEdges())
                {
                    Edge e = (Edge)eo;
                    if (e.GetStartVertex() != null) continue;           // 整圆边无顶点
                    Curve c = (Curve)e.GetCurve();
                    if (!c.IsCircle()) continue;
                    double[] cp = (double[])c.CircleParams;            // 圆心 xyz (m), 轴向 xyz, 半径
                    int a = Math.Abs(cp[3]) > 0.999 ? 0 : Math.Abs(cp[4]) > 0.999 ? 1 : Math.Abs(cp[5]) > 0.999 ? 2 : -1;
                    if (a < 0) continue;
                    double mid = (box[a] + box[a + 3]) / 2 * MM;
                    cnt["XYZ"[a] + (cp[a] >= mid ? "+" : "-")]++;
                }
            var best = cnt.OrderByDescending(q => q.Value).First();       // OrderBy 稳定: 同数保持上面的优先顺序
            return best.Value > 0 ? best.Key : "";
        }

        /// <summary>
        /// 断开画法可去掉的等截面段 (模型坐标 mm, 沿轴 a): 收集全部棱边端点、圆边的轴向位置及垂直孔的径向范围,
        /// 相邻特征位置间距 > 4 倍截面且 ≥ 150 时, 两侧各留 1 倍截面 (≥ 40) 后去掉中间段。
        /// </summary>
        static List<double[]> BreakSpans(ModelDoc2 m, int a, double[] box, double sec)
        {
            var pos = new List<double>();
            foreach (object bo in (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true))
                foreach (object eo in (object[])((Body2)bo).GetEdges())
                {
                    Edge e = (Edge)eo;
                    Curve c = (Curve)e.GetCurve();
                    if (c.IsCircle())
                    {
                        double[] cp = (double[])c.CircleParams;
                        double ctr = cp[a] / MM, r = cp[6] / MM;
                        if (Math.Abs(cp[3 + a]) > 0.999) pos.Add(ctr);            // 绕长轴的圆: 台阶/端面位置
                        else { pos.Add(ctr - r); pos.Add(ctr + r); }               // 垂直于长轴的孔
                    }
                    Vertex v0 = (Vertex)e.GetStartVertex(), v1 = (Vertex)e.GetEndVertex();
                    foreach (Vertex vx in new[] { v0, v1 }) if (vx != null) pos.Add(((double[])vx.GetPoint())[a] / MM);
                }
            pos.Add(box[a]); pos.Add(box[a + 3]);
            var ps = pos.Select(q => Math.Round(q, 1)).Distinct().OrderBy(q => q).ToList();
            var cuts = new List<double[]>();
            double keep = Math.Max(sec, 40), need = Math.Max(4 * sec, 150);
            for (int i = 0; i + 1 < ps.Count; i++)
                if (ps[i + 1] - ps[i] > need && ps[i + 1] - ps[i] - 2 * keep >= 2 * sec)
                    cuts.Add(new[] { ps[i] + keep, ps[i + 1] - keep });
            return cuts;
        }

        /// <summary>在视图中插入竖直断裂线 (位置 = 相对视图中心的图纸坐标, 实测标定) 并断开</summary>
        static void ApplyBreaks(ModelDoc2 d, DrawingDoc dd, View v, List<double[]> cuts, int a, double[] box)
        {
            double[] ps = (double[])v.Position;
            double[] mid = { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 };
            foreach (var c in cuts)
            {
                double[] p1 = (double[])mid.Clone(), p2 = (double[])mid.Clone();
                p1[a] = c[0]; p2[a] = c[1];
                double q1 = ToSheet(v, p1[0], p1[1], p1[2])[0] - ps[0], q2 = ToSheet(v, p2[0], p2[1], p2[2])[0] - ps[0];
                v.InsertBreak2((int)swBreakLineOrientation_e.swBreakLineVertical, Math.Min(q1, q2), Math.Max(q1, q2), (int)swBreakLineStyle_e.swBreakLine_ZigZag, 1);
            }
            d.ClearSelection2(true);
            d.Extension.SelectByID2(v.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
            dd.BreakView();
            d.ForceRebuild3(false);
        }

        /// <summary>主视图方向上的孔数 (轴线垂直于视图平面的整圆边, 按投影圆心去重)</summary>
        static int HoleCount(ModelDoc2 m, string view)
        {
            int a = view == "*前视" || view == "*后视" ? 2 : view == "*上视" || view == "*下视" ? 1 : 0;
            var set = new HashSet<string>();
            foreach (object bo in (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true))
                foreach (object eo in (object[])((Body2)bo).GetEdges())
                {
                    Edge e = (Edge)eo;
                    if (e.GetStartVertex() != null) continue;
                    Curve c = (Curve)e.GetCurve();
                    if (!c.IsCircle()) continue;
                    double[] cp = (double[])c.CircleParams;
                    if (Math.Abs(cp[3 + a]) < 0.999 || !IsHoleEdge(e)) continue;
                    int u = (a + 1) % 3, w = (a + 2) % 3;
                    set.Add(Math.Round(cp[u] * 1e4) + "," + Math.Round(cp[w] * 1e4));
                }
            return set.Count;
        }

        /// <summary>孔少 (≤4, 无坐标表) 时的孔位尺寸: 自视图左边 / 下边起链式标注到各孔中心 (水平链在外形尺寸内侧, 竖直链在视图右侧)</summary>
        static int HolePos(ModelDoc2 d, View v, List<Seg> holes, List<Seg> lines, double[] ol)
        {
            var usedH = new List<double[]>(); var usedV = new List<double[]>();   // 已标链式尺寸 [位置, 起, 止]
            int n = 0;
            var vl = lines.Where(s => Math.Abs(s.X1 - s.X2) < 1e-7 && Math.Abs(s.Y1 - s.Y2) > 1e-4).ToList();
            var hl = lines.Where(s => Math.Abs(s.Y1 - s.Y2) < 1e-7 && Math.Abs(s.X1 - s.X2) > 1e-4).ToList();
            var vm = new VMap(v);
            if (vl.Count > 0)
            {
                Seg prev = vl.OrderBy(s => s.X1).ThenByDescending(s => Math.Abs(s.Y1 - s.Y2)).First();
                double px = prev.X1;
                foreach (var h in holes.OrderBy(q => q.Cx))
                {
                    if (Math.Abs(h.Cx - px) < 5e-4) continue;             // 同一 X 只标一次
                    // 链节在图上短于 10 mm 时文字放不下, 该节移到外形尺寸外侧一排, 避免与相邻链节文字重叠
                    double yy = Math.Abs(h.Cx - px) * vm.S < 0.010 && (n % 2 == 1) ? ol[1] - 0.018 : ol[1] - 0.006;
                    double a0 = vm.X(Math.Min(px, h.Cx)), a1 = vm.X(Math.Max(px, h.Cx));
                    while (usedH.Any(u => Math.Abs(u[0] - yy) < 0.004 && u[1] < a1 && u[2] > a0)) yy -= 0.010;   // 与已标尺寸同一行且范围重叠 → 外移一行
                    if (DimH(d, v, prev, h, vm.X((px + h.Cx) / 2), yy) != null) { n++; usedH.Add(new[] { yy, a0, a1 }); }
                    prev = h; px = h.Cx;
                }
            }
            if (hl.Count > 0)
            {
                Seg prev = hl.OrderBy(s => s.Y1).ThenByDescending(s => Math.Abs(s.X1 - s.X2)).First();
                double py = prev.Y1;
                foreach (var h in holes.OrderBy(q => q.Cy))
                {
                    if (Math.Abs(h.Cy - py) < 5e-4) continue;
                    double xx = Math.Abs(h.Cy - py) * vm.S < 0.010 && (n % 2 == 1) ? ol[2] + 0.016 : ol[2] + 0.006;
                    double b0 = vm.Y(Math.Min(py, h.Cy)), b1 = vm.Y(Math.Max(py, h.Cy));
                    while (usedV.Any(u => Math.Abs(u[0] - xx) < 0.004 && u[1] < b1 && u[2] > b0)) xx += 0.010;
                    if (DimV(d, v, prev, h, xx, vm.Y((py + h.Cy) / 2)) != null) { n++; usedV.Add(new[] { xx, b0, b1 }); }
                    prev = h; py = h.Cy;
                }
            }
            return n;
        }

        /// <summary>
        /// 后处理: 把 python/audit_drawings.py 在 PDF 上按位置读出的外形尺寸标注值写入图纸目录 (外形尺寸读数, 供回归基准比对),
        /// 并与应有值 (视图几何) 核对: 相差超过显示精度的一半 (+0.02 mm) 即不符 (选到了别的实例等, api_calibration.md 第 2 节第 26 条)。
        /// 返回外形尺寸个数; unread = 读不出的个数 (PDF 缺失、位置附近没有数字), wrong = 不符的说明。读值在出图之后、不经 SolidWorks。
        /// </summary>
        public static int ApplyExtentReadings(string auditJson, out int unread, out List<string> wrong)
        {
            var byPdf = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            using (var doc = JsonDocument.Parse(File.ReadAllText(auditJson)))
                if (doc.RootElement.TryGetProperty("extents", out JsonElement ex))
                    foreach (var e in ex.EnumerateArray()) byPdf[e.GetProperty("file").GetString()] = e.Clone();
            int total = 0, un = 0;
            var bad = new List<string>();
            WriteIndex(all =>
            {
                foreach (var r in all.Values)
                {
                    var marks = ParseMarks(r[10]);
                    if (marks == null) continue;
                    var vals = new List<double>();
                    JsonElement[] reads = byPdf.TryGetValue(Path.ChangeExtension(r[4], ".pdf").Replace('\\', '/'), out JsonElement e)
                        ? e.GetProperty("marks").EnumerateArray().ToArray() : new JsonElement[0];
                    for (int i = 0; i < marks.Count; i++)
                    {
                        total++;
                        double geo = marks[i][2] ?? double.NaN;
                        JsonElement q = i < reads.Length ? reads[i] : default;
                        if (q.ValueKind != JsonValueKind.Object || q.GetProperty("read").ValueKind != JsonValueKind.Number) { un++; continue; }
                        double v = q.GetProperty("read").GetDouble(), tol = q.GetProperty("tol").GetDouble();
                        vals.Add(v);
                        if (!(Math.Abs(v - geo) <= tol))
                            bad.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} 标注 {1}, 应为 {2:0.##}", Path.GetFileName(r[4]), q.GetProperty("text").GetString(), geo));
                    }
                    r[9] = ExtentsCol(vals);
                }
            });
            unread = un; wrong = bad;
            return total;
        }

        /// <summary>
        /// 图纸目录: 与 index.tsv 中历次出图记录合并 (按文件), 以便局部出图不丢失其余条目; edit 非空时在写出前修改合并后的各行 (键 = 文件)
        /// </summary>
        static void WriteIndex(Action<Dictionary<string, string[]>> edit = null)
        {
            string tsv = Path.Combine(DrwRoot, "index.tsv");
            var all = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(tsv))
                foreach (string ln in File.ReadAllLines(tsv, Encoding.UTF8))
                {
                    string[] c = ln.Split('\t');
                    if (c.Length >= 6 && File.Exists(Path.Combine(DrwRoot, c[4]))) { Array.Resize(ref c, INDEX_COLS); all[c[4]] = c; }
                }
            foreach (var r in Index) all[r[4]] = r;
            edit?.Invoke(all);
            File.WriteAllLines(tsv, all.Values.Select(r => string.Join("\t", r.Select(x => x ?? ""))), new UTF8Encoding(false));
            var Index2 = all.Values.ToList();
            var sb = new StringBuilder();
            sb.AppendLine("# 图纸目录");
            sb.AppendLine();
            sb.AppendLine("> 由 `cad run` 自动生成。每张图同时输出 `.SLDDRW` 与 `.PDF`; 激光下料件另有 `exports/dxf/` 下 1:1 DXF。");
            sb.AppendLine();
            sb.AppendLine("| 序号 | 图号 | 名称 | 图幅 | 比例 | 文件 | 备注 |");
            sb.AppendLine("|---:|---|---|---|---|---|---|");
            int i = 0;
            foreach (var r in Index2.OrderBy(q => q[0])) sb.AppendLine(string.Format("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |", ++i, r[0], r[1], r[2], r[3], r[4].Replace('\\', '/'), r[5]));
            File.WriteAllText(Path.Combine(DrwRoot, "图纸目录.md"), sb.ToString(), new UTF8Encoding(false));
            // 机读目录: 供 python/merge_drawings.py 合订 PDF、Baseline 比对 (dims/holes/balloons 为 null = 该类图纸不统计;
            // extents = 外形尺寸读数 mm (后处理从 PDF 读出, 升序, 2 位小数), null = 尚未读出或旧代码出的图;
            // extent_marks = 各外形尺寸文字位置与应有值 [[x,y,应有值],…] (mm, 图纸坐标原点左下), 供 audit_drawings.py 在 PDF 上读值)
            Func<string, int?> num = s => { int v; return int.TryParse(s, out v) ? v : (int?)null; };
            var rows = Index2.OrderBy(q => q[0]).Select(r => new Dictionary<string, object> {
                { "code", r[0] }, { "name", r[1] }, { "size", r[2] }, { "scale", r[3] },
                { "file", r[4].Replace('\\', '/') }, { "pdf", Path.ChangeExtension(r[4], ".pdf").Replace('\\', '/') }, { "remark", r[5] },
                { "kind", r[4].StartsWith("装配图") ? "assembly" : "part" },
                { "dims", num(r[6]) }, { "holes", num(r[7]) }, { "balloons", num(r[8]) }, { "extents", ParseExtents(r[9]) },
                { "extent_marks", ParseMarks(r[10]) } }).ToList();
            File.WriteAllText(Path.Combine(DrwRoot, "index.json"),
                System.Text.Json.JsonSerializer.Serialize(rows, new System.Text.Json.JsonSerializerOptions {
                    WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
                new UTF8Encoding(false));
        }
    }
}
