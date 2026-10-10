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
    /// 模型完整性检查 (像用户在 SolidWorks 中打开一样逐个打开): 打开错误/警告、缺失参考、特征重建错误 (What's Wrong)、
    /// 装配体中未还原 / 浮动 / 压缩 / 隐藏的零部件、零部件引用文件不存在。结果写 checks/integrity.md。
    /// </summary>
    public static class Integrity
    {
        /// <summary>返回问题条数</summary>
        public static int Models(string filter)
        {
            Sw.Head("模型完整性检查");
            var files = Cat.Reg.Values.Where(p => p.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase))
                                      .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)).ThenBy(p => p).ToList();
            if (filter != null) files = files.Where(p => p.Contains(filter)).ToList();
            var issues = new List<string>();
            int nOk = 0;
            foreach (string p in files)
            {
                if (!File.Exists(p)) { issues.Add("| " + Rel(p) + " | 文件不存在 | 登记表中有, 磁盘上没有 |"); continue; }
                bool asm = p.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase);
                Sw.CloseAll();
                Sw.Recycle(5.0);
                int err = 0, warn = 0;
                var opt = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_OverrideDefaultLoadLightweight);
                ModelDoc2 m = Sw.App.OpenDoc6(p, (int)(asm ? swDocumentTypes_e.swDocASSEMBLY : swDocumentTypes_e.swDocPART), opt, "", ref err, ref warn);
                var mine = new List<string>();
                if (m == null) { issues.Add(string.Format("| {0} | 打不开 | err={1} warn={2} |", Rel(p), err, warn)); continue; }
                // 只读打开 (文件被用户的 SolidWorks 占用) 不算问题
                int w2 = warn & ~(int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly & ~(int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen;
                if (err != 0 || w2 != 0) mine.Add(string.Format("打开错误/警告 err={0} warn={1} ({2})", err, w2, WarnText(w2)));
                if (asm)
                {
                    AssemblyDoc a = (AssemblyDoc)m;
                    try { a.ResolveAllLightWeightComponents(false); } catch { }
                    var cs = ((object[])a.GetComponents(true) ?? new object[0]).Cast<Component2>().ToList();     // 顶层
                    var all = ((object[])a.GetComponents(false) ?? new object[0]).Cast<Component2>().ToList();
                    int missing = 0, unresolved = 0, floating = 0, suppressed = 0, hidden = 0;
                    var ex = new List<string>();
                    foreach (var c in all)
                    {
                        string cp = c.GetPathName();
                        if (string.IsNullOrEmpty(cp) || !File.Exists(cp)) { missing++; if (ex.Count < 3) ex.Add("缺:" + Path.GetFileName(cp)); continue; }
                        if (c.IsSuppressed()) { suppressed++; if (ex.Count < 3) ex.Add("压缩:" + c.Name2); continue; }
                        if (c.GetModelDoc2() == null) { unresolved++; if (ex.Count < 3) ex.Add("未还原:" + c.Name2); }
                        if (c.IsHidden(false)) { hidden++; if (ex.Count < 3) ex.Add("隐藏:" + c.Name2); }
                    }
                    // 浮动 = 既未固定也没有任何配合 (位置无定义)。配合定位的机构件 (如齿轮系) 有意保留自由度, 不算浮动
                    foreach (var c in cs)
                    {
                        if (c.IsFixed() || c.IsSuppressed()) continue;
                        object[] mates = (object[])c.GetMates();
                        if (mates != null && mates.Length > 0) continue;
                        floating++; if (ex.Count < 3) ex.Add("浮动:" + c.Name2);
                    }
                    if (missing + unresolved + floating + suppressed + hidden > 0)
                        mine.Add(string.Format("零部件: 缺失 {0}, 未还原 {1}, 浮动 {2}, 压缩 {3}, 隐藏 {4}  ({5})", missing, unresolved, floating, suppressed, hidden, string.Join("; ", ex)));
                }
                m.ForceRebuild3(false);
                int nw = 0;
                try { nw = m.Extension.GetWhatsWrongCount(); } catch { }
                if (nw > 0)
                {
                    object fo = null, eo = null, wo = null;
                    m.Extension.GetWhatsWrong(out fo, out eo, out wo);
                    var fs = ((object[])fo ?? new object[0]).Cast<Feature>().ToList();
                    var es = (int[])eo ?? new int[0]; var ws = (bool[])wo ?? new bool[0];
                    var det = new List<string>();
                    for (int i = 0; i < fs.Count && i < 5; i++)
                        det.Add(string.Format("{0}[{1}]{2}", fs[i].Name, i < es.Length ? es[i] : 0, i < ws.Length && ws[i] ? "(警告)" : ""));
                    mine.Add("重建错误/警告 " + nw + ": " + string.Join(", ", det));
                }
                if (mine.Count == 0) nOk++;
                else foreach (string s in mine) { issues.Add("| " + Rel(p) + " | " + s + " |"); Sw.Log("  " + Path.GetFileName(p) + ": " + s); }
                Sw.App.CloseDoc(m.GetTitle());
            }
            var sb = new StringBuilder();
            sb.AppendLine("# 模型完整性检查");
            sb.AppendLine();
            sb.AppendLine(string.Format("> {0}  检查 {1} 个文件, 无问题 {2} 个, 问题 {3} 条。", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), files.Count, nOk, issues.Count));
            sb.AppendLine();
            sb.AppendLine("| 文件 | 问题 |");
            sb.AppendLine("|---|---|");
            foreach (string s in issues) sb.AppendLine(s.Replace(" | 文件不存在 | ", " | 文件不存在: ").Replace(" | 打不开 | ", " | 打不开: "));
            File.WriteAllText(Path.Combine(Run.Checks, "integrity.md"), sb.ToString(), new UTF8Encoding(false));
            Sw.Log(string.Format("检查完成: {0} 个文件, 无问题 {1}, 问题 {2} 条 -> checks\\integrity.md", files.Count, nOk, issues.Count));
            return issues.Count;
        }
        /// <summary>工程图检查: 打开时的参考警告 (ID 不匹配、缺参考、需重建), 用于装配体/零件重存后的复核</summary>
        /// <summary>返回有警告的工程图张数</summary>
        public static int Drawings(string filter)
        {
            Sw.Head("工程图参考检查");
            var files = Directory.GetFiles(Run.Drawings, "*.SLDDRW", SearchOption.AllDirectories).OrderBy(p => p).ToList();
            if (filter != null) files = files.Where(p => p.Contains(filter)).ToList();
            int bad = 0;
            foreach (string p in files)
            {
                Sw.CloseAll();
                Sw.Recycle(5.0);
                int err = 0, warn = 0;
                ModelDoc2 d = Sw.App.OpenDoc6(p, (int)swDocumentTypes_e.swDocDRAWING, (int)swOpenDocOptions_e.swOpenDocOptions_Silent, "", ref err, ref warn);
                int w2 = warn & ~(int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly & ~(int)swFileLoadWarning_e.swFileLoadWarning_AlreadyOpen;
                if (d == null || err != 0 || w2 != 0) { bad++; Sw.Log(string.Format("  {0}: err={1} warn={2} ({3})", Path.GetFileName(p), err, w2, WarnText(w2))); }
                if (d != null) Sw.App.CloseDoc(d.GetTitle());
                Sw.CloseAll();
            }
            Sw.Log(string.Format("工程图检查完成: {0} 张, 有警告 {1} 张", files.Count, bad));
            return bad;
        }

        /// <summary>
        /// 刷新装配体引用: 零件单独重建 (如 buy2) 后, 新文件的内部 ID 与装配体中记录的不一致 (IdMismatch),
        /// 用户打开时特征树出现红叉/黄色感叹号。按 紧固件组 → 模块 → 总装 的顺序打开、重建、保存, 装配体即记录新 ID。
        /// </summary>
        public static void Refresh(string filter)
        {
            Sw.Head("刷新装配体引用");
            Func<string, int> rank = p => p.Contains(@"\00_标准件\") ? 0 : 1;
            var asms = Cat.Reg.Values.Where(p => p.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
                                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(rank).ThenBy(p => p).ToList();
            if (filter != null) asms = asms.Where(p => p.Contains(filter)).ToList();
            int nFix = 0, nFail = 0;
            foreach (string p in asms)
            {
                Sw.CloseAll();
                Sw.Recycle(5.0);
                int err = 0, warn = 0;
                var opt = (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_OverrideDefaultLoadLightweight);
                ModelDoc2 m = Sw.App.OpenDoc6(p, (int)swDocumentTypes_e.swDocASSEMBLY, opt, "", ref err, ref warn);
                if (m == null) { Sw.Log("!! 打不开 " + Rel(p)); nFail++; continue; }
                if ((warn & (int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly) != 0) { Sw.Log("!! 只读 (被其他程序占用), 跳过: " + Rel(p)); nFail++; Sw.App.CloseDoc(m.GetTitle()); continue; }
                bool stale = (warn & ((int)swFileLoadWarning_e.swFileLoadWarning_IdMismatch | (int)swFileLoadWarning_e.swFileLoadWarning_NeedsRegen)) != 0;
                if (stale)
                {
                    // 只重建顶层再保存: 全部重建会把内存中的零件重建戳也更新, 而零件文件未存 → 再打开仍报 NeedsRegen (实测)
                    m.ForceRebuild3(true);
                    int e = 0, w = 0;
                    bool ok = m.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref e, ref w);
                    Sw.Log(string.Format("{0} {1}  (加载警告 {2})", ok && e == 0 ? "已刷新" : "!! 保存失败 err=" + e, Path.GetFileName(p), WarnText(warn)));
                    if (ok && e == 0) nFix++; else nFail++;
                }
                Sw.App.CloseDoc(m.GetTitle());
            }
            Sw.Log(string.Format("刷新完成: 装配体 {0} 个, 刷新 {1}, 失败 {2}", asms.Count, nFix, nFail));
        }

        static string Rel(string p) { return Run.Rel(p); }

        static string WarnText(int w)
        {
            var r = new List<string>();
            foreach (swFileLoadWarning_e e in Enum.GetValues(typeof(swFileLoadWarning_e)))
                if ((int)e != 0 && (w & (int)e) == (int)e) r.Add(e.ToString().Replace("swFileLoadWarning_", ""));
            return string.Join("+", r);
        }
    }
}
