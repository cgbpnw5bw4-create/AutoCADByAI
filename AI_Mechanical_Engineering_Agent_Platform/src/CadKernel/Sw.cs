using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>SolidWorks API 通用封装 —— 内部单位米(m), 对外接口一律毫米(mm)</summary>
    public static class Sw
    {
        public const double MM = 0.001;
        public static SldWorks App;
        public static string PartTpl, AsmTpl, TplDir;
        public static string MatDb { get { return Env.MaterialDb; } }
        /// <summary>已连接实例的版本号 (RevisionNumber, 如 31.5.0)</summary>
        public static string Version { get; private set; }

        // ---------------- 连接 ----------------
        // 自动化只使用本程序启动的专用 SolidWorks 实例, 通过 ROT 中 "SolidWorks_PID_<pid>" 条目精确连接 ——
        // 绝不附着到用户正在使用的 SolidWorks 窗口。
        // 专用实例记录于 .cad\sw_pid.txt = "PID<TAB>进程启动时间 (UTC ticks)"。PID 会被系统复用: 只认 PID 与启动时间
        // 都一致的进程; 其它情况 (含旧格式只有 PID 的记录) 一律当作没有专用实例, 不附着、不退出、不结束, 另启动一个。
        // 专用实例内存超过 3.5 GB 时自动重启 (长时间批量建模后 SolidWorks 会因内存增长崩溃, 0x800706BA)。
        [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int r, out System.Runtime.InteropServices.ComTypes.IRunningObjectTable rot);
        [DllImport("ole32.dll")] static extern int CreateBindCtx(int r, out System.Runtime.InteropServices.ComTypes.IBindCtx ctx);
        static string SW_EXE { get { return Env.SolidWorksExe; } }
        static string PidFile { get { return Path.Combine(Run.StateDir, "sw_pid.txt"); } }

        static SldWorks FromPid(int pid)
        {
            System.Runtime.InteropServices.ComTypes.IRunningObjectTable rot;
            if (GetRunningObjectTable(0, out rot) != 0) return null;
            System.Runtime.InteropServices.ComTypes.IEnumMoniker en;
            rot.EnumRunning(out en);
            var mk = new System.Runtime.InteropServices.ComTypes.IMoniker[1];
            while (en.Next(1, mk, IntPtr.Zero) == 0)
            {
                System.Runtime.InteropServices.ComTypes.IBindCtx ctx; CreateBindCtx(0, out ctx);
                string name; mk[0].GetDisplayName(ctx, null, out name);
                if (RotPid(name) == pid)
                {
                    object o; rot.GetObject(mk[0], out o);
                    return o as SldWorks;
                }
            }
            return null;
        }

        /// <summary>ROT 显示名中的 PID ("!SolidWorks_PID_1234" → 1234), 不是该格式返回 -1。按整数比较: PID 123 不能匹配 1234</summary>
        internal static int RotPid(string displayName)
        {
            var m = System.Text.RegularExpressions.Regex.Match(displayName ?? "", @"SolidWorks_PID_(\d+)(?!\d)");
            int pid;
            return m.Success && int.TryParse(m.Groups[1].Value, out pid) ? pid : -1;
        }

        /// <summary>专用实例记录: "PID&lt;TAB&gt;启动时间 UTC ticks"</summary>
        internal static string Stamp(int pid, DateTime startUtc) { return pid + "\t" + startUtc.Ticks; }

        /// <summary>记录与进程 (PID + 启动时间) 是否一致; 旧格式 (只有 PID) 或无法解析的记录一律不一致</summary>
        internal static bool Matches(string record, int pid, DateTime startUtc)
        {
            string[] q = (record ?? "").Trim().Split('\t');
            int p; long t;
            return q.Length == 2 && int.TryParse(q[0], out p) && long.TryParse(q[1], out t) && p == pid && t == startUtc.Ticks;
        }

        /// <summary>进程仍是记录中的专用实例 (每次退出/结束之前都要再核对)</summary>
        static bool IsOwned(System.Diagnostics.Process p)
        {
            try { return File.Exists(PidFile) && !p.HasExited && Matches(File.ReadAllText(PidFile), p.Id, p.StartTime.ToUniversalTime()); }
            catch { return false; }
        }

        /// <summary>记录中的专用实例; 没有记录、进程已退出、或 PID 已被别的进程复用时返回 null</summary>
        static System.Diagnostics.Process Owned()
        {
            if (!File.Exists(PidFile)) return null;
            string rec = File.ReadAllText(PidFile);
            int pid;
            if (!int.TryParse(rec.Trim().Split('\t')[0], out pid)) return null;
            System.Diagnostics.Process p;
            try { p = System.Diagnostics.Process.GetProcessById(pid); } catch { return null; }
            if (IsOwned(p)) return p;
            try
            {
                if (p.ProcessName.StartsWith("SLDWORKS", StringComparison.OrdinalIgnoreCase))
                    Log("PID " + pid + " 的 SolidWorks 不是记录中的专用实例 (启动时间不符或旧格式记录), 不使用、不退出; 另启动专用实例");
            }
            catch { }
            return null;
        }

        /// <summary>专用实例记录的状态 (cad doctor 用: 只读, 不启动、不连接)</summary>
        public static string InstanceStatus()
        {
            if (!File.Exists(PidFile)) return "无记录 (运行时启动)";
            var p = Owned();
            return p != null ? string.Format("PID {0} 运行中, 身份已核对, 内存 {1:F1} GB", p.Id, p.WorkingSet64 / 1e9)
                             : "记录已失效 (进程已退出或不是专用实例), 运行时另启动";
        }

        /// <summary>正常退出专用实例, 超时才结束进程; 动手前与结束前都核对身份, 不是专用实例绝不动它</summary>
        static void Retire(System.Diagnostics.Process proc, int waitMs)
        {
            if (!IsOwned(proc)) { Log("!! PID " + proc.Id + " 已不是专用实例, 不退出、不结束"); App = null; return; }
            try
            {
                SldWorks app = App ?? FromPid(proc.Id);
                if (app != null) { app.CloseAllDocuments(true); app.ExitApp(); }    // 专用实例中的文档均已由程序保存
            }
            catch { }
            if (!proc.WaitForExit(waitMs) && IsOwned(proc)) proc.Kill();
            App = null;
            File.Delete(PidFile);
        }

        static System.Diagnostics.Process swProc;

        public static void Connect()
        {
            Directory.CreateDirectory(Run.StateDir);
            var proc = Owned();
            if (proc != null && proc.WorkingSet64 > 3.5e9)
            {
                Log(string.Format("专用 SolidWorks (PID {0}) 内存 {1:F1} GB, 重启", proc.Id, proc.WorkingSet64 / 1e9));
                Retire(proc, 60000);
                proc = null;
            }
            if (proc == null)
            {
                // UseShellExecute: 专用实例不继承本进程的标准输出句柄。否则 SolidWorks 比 cad 活得久, 一直占着调用方
                // 重定向输出的管道, 以管道读取输出的调用方 (PowerShell *>、自动化工具) 永远等不到结束 (2026-10-10 实测)
                proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SW_EXE) { UseShellExecute = true });
                if (proc == null) throw new Exception("无法启动 SolidWorks: " + SW_EXE);
                File.WriteAllText(PidFile, Stamp(proc.Id, proc.StartTime.ToUniversalTime()));
                Log("启动专用 SolidWorks 实例 PID " + proc.Id);
            }
            swProc = proc;
            int pid = proc.Id;
            for (int i = 0; i < 180 && App == null; i++)
            {
                App = FromPid(pid);
                if (App == null) System.Threading.Thread.Sleep(2000);
            }
            if (App == null) throw new Exception("无法连接专用 SolidWorks 实例 PID " + pid);
            App.Visible = true;
            // "输入尺寸值" 会在程序标注尺寸时弹出修改框, 运行期间关闭; 这是用户级选项 (用户自己的 SolidWorks 也读它),
            // 原值记录在 .cad\sw_pref_orig.txt, 程序结束时 (Disconnect) 恢复并删除记录; 上次异常退出未恢复时沿用旧记录。
            if (!File.Exists(PrefFile)) File.WriteAllText(PrefFile, App.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate).ToString());
            App.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
            PartTpl = App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplatePart);
            AsmTpl = App.GetUserPreferenceStringValue((int)swUserPreferenceStringValue_e.swDefaultTemplateAssembly);
            TplDir = Path.GetDirectoryName(PartTpl);
            Version = App.RevisionNumber();
            if (Run.Dir != null) { Run.Manifest.SolidWorks = Version; Run.Save(); }
            Log("已连接 SolidWorks " + Version + " (专用实例 PID " + pid + ")");
            StartWatchdog(pid);
        }

        static string PrefFile { get { return Path.Combine(Run.StateDir, "sw_pref_orig.txt"); } }

        /// <summary>
        /// 批量出图时专用实例内存持续增长 (11 张模块装配图后达 13 GB, 总装图因内存换页停滞数小时) →
        /// 每张图之前检查, 超过 gb 即正常退出并重启专用实例。
        /// </summary>
        public static void Recycle(double gb)
        {
            var proc = swProc;
            if (proc == null) return;
            try { proc.Refresh(); if (proc.HasExited || proc.WorkingSet64 < gb * 1e9) return; } catch { return; }
            Log(string.Format("专用 SolidWorks 内存 {0:F1} GB > {1} GB, 重启", proc.WorkingSet64 / 1e9, gb));
            Disconnect();
            Retire(proc, 90000);
            Connect();
        }

        /// <summary>恢复运行期间修改的用户选项, 恢复成功后删除原值记录 (下次运行重新记录当时的值)</summary>
        public static void Disconnect()
        {
            try
            {
                bool v;
                if (App != null && File.Exists(PrefFile) && bool.TryParse(File.ReadAllText(PrefFile).Trim(), out v))
                {
                    App.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, v);
                    File.Delete(PrefFile);
                }
            }
            catch { }
        }

        // ---------------- 模态对话框看门狗 ----------------
        // 零部件 > 500 的装配体按"大型装配体模式"轻化加载, SOLIDWORKS CAM 插件随即弹出模态框
        // "SOLIDWORKS CAM 错误 / 由于已检测到加载了轻化零件…", API 调用被阻塞直到有人点"确定"。
        // 只在专用实例 (pid) 中查找该标题的窗口并点击"确定"; 不修改用户的 SolidWorks 选项。
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr w, EnumProc f, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        static System.Threading.Timer watchdog;
        static readonly string[] AUTO_OK = { "SOLIDWORKS CAM 错误" };

        static int swPid;

        static void StartWatchdog(int pid)
        {
            swPid = pid;
            if (watchdog != null) return;
            watchdog = new System.Threading.Timer(_ =>
            {
                try
                {
                    EnumWindows((h, l) =>
                    {
                        uint p; GetWindowThreadProcessId(h, out p);
                        if (p != (uint)swPid) return true;
                        var t = new System.Text.StringBuilder(256); GetWindowText(h, t, 256);
                        bool ok = Array.IndexOf(AUTO_OK, t.ToString()) >= 0;
                        if (!ok && t.ToString() == "SOLIDWORKS")
                        {
                            // 仅限这一条提示 (上一个专用实例尚在退出时启动新实例出现, 无害): 按正文精确匹配, 其它 SOLIDWORKS 消息框一律不动
                            EnumChildWindows(h, (c, l2) =>
                            {
                                var st = new System.Text.StringBuilder(512); GetWindowText(c, st, 512);
                                if (st.ToString().StartsWith("无法生成日志文件。自动恢复将不可使用。")) ok = true;
                                return true;
                            }, IntPtr.Zero);
                        }
                        if (!ok) return true;
                        EnumChildWindows(h, (c, l2) =>
                        {
                            var k = new System.Text.StringBuilder(64); GetClassName(c, k, 64);
                            var bt = new System.Text.StringBuilder(64); GetWindowText(c, bt, 64);
                            if (k.ToString() == "Button" && bt.ToString() == "确定")
                            {
                                SendMessage(c, 0x00F5, IntPtr.Zero, IntPtr.Zero);   // BM_CLICK
                                Log("[看门狗] 已确认专用实例中的对话框: " + t);
                            }
                            return true;
                        }, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }
            }, null, 2000, 2000);
        }

        public static void Log(string s) { Console.WriteLine("  " + s); }
        public static void Head(string s) { Console.WriteLine(); Console.WriteLine("=== " + s + " ==="); }

        // ---------------- 文档 ----------------
        public static ModelDoc2 NewPart()
        {
            ModelDoc2 m = (ModelDoc2)App.NewDocument(PartTpl, 0, 0, 0);
            if (m == null) throw new Exception("NewDocument(零件) 返回 null");
            m.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            return m;
        }

        public static ModelDoc2 NewAsm()
        {
            ModelDoc2 m = (ModelDoc2)App.NewDocument(AsmTpl, 0, 0, 0);
            if (m == null) throw new Exception("NewDocument(装配体) 返回 null");
            m.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swUnitsLinear, (int)swLengthUnit_e.swMM);
            return m;
        }

        public static string Save(ModelDoc2 m, string fullPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            int err = 0, warn = 0;
            // 大装配体的工程图偶发 err=1 (一般性保存错误), 稍候重试即可成功 (M05/M09 实测)
            for (int i = 0; i < 3; i++)
            {
                err = 0;
                m.Extension.SaveAs(fullPath, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref err, ref warn);
                if (err == 0) return fullPath;
                Log("   保存 err=" + err + ", 重试 " + (i + 1));
                System.Threading.Thread.Sleep(3000);
                m.ForceRebuild3(false);
            }
            if (fullPath.EndsWith(".SLDDRW", StringComparison.OrdinalIgnoreCase))
            {
                // 工程图: 先存为临时名, 再替换目标文件 (工程图本身不被其它文件引用, 改名无影响)
                string tmp = Path.Combine(Path.GetDirectoryName(fullPath), "_tmp_" + Path.GetFileName(fullPath));
                err = 0;
                m.Extension.SaveAs(tmp, (int)swSaveAsVersion_e.swSaveAsCurrentVersion, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, null, ref err, ref warn);
                if (err == 0)
                {
                    App.CloseDoc(m.GetTitle());
                    if (File.Exists(fullPath)) File.Delete(fullPath);
                    File.Move(tmp, fullPath);
                    Log("   经临时文件保存: " + Path.GetFileName(fullPath));
                    return fullPath;
                }
            }
            throw new Exception("保存失败 err=" + err + " -> " + fullPath + " (文件被占用? 先运行 clean)");
        }

        /// <summary>关闭文档: 标题带/不带扩展名各试一次, 并以路径复核 (否则文档滞留, 批量建模时内存持续增长至崩溃)</summary>
        public static void Close(ModelDoc2 m)
        {
            string t = m.GetTitle(), p = m.GetPathName();
            App.CloseDoc(t);
            if (!string.IsNullOrEmpty(p) && App.GetOpenDocumentByName(p) != null)
            {
                App.CloseDoc(Path.GetFileNameWithoutExtension(t));
                if (App.GetOpenDocumentByName(p) != null) App.CloseDoc(Path.GetFileName(p));
            }
        }

        public static int OpenCount()
        {
            int k = 0; ModelDoc2 d = (ModelDoc2)App.GetFirstDocument();
            while (d != null) { k++; d = (ModelDoc2)d.GetNext(); }
            return k;
        }

        public static ModelDoc2 Open(string path)
        {
            int e = 0, w = 0;
            string ext = Path.GetExtension(path).ToLower();
            int type = ext == ".sldasm" ? (int)swDocumentTypes_e.swDocASSEMBLY
                     : ext == ".slddrw" ? (int)swDocumentTypes_e.swDocDRAWING : (int)swDocumentTypes_e.swDocPART;
            // 强制完全还原打开 (不走轻化): 大型装配体模式下轻化加载会触发 SOLIDWORKS CAM 插件的模态错误框, 阻塞 API
            int opt = (int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_OverrideDefaultLoadLightweight;
            ModelDoc2 d = (ModelDoc2)App.OpenDoc6(path, type, opt, "", ref e, ref w);
            if (d == null) throw new Exception("打开失败(err=" + e + "): " + path);
            return d;
        }

        public static void Activate(ModelDoc2 m)
        {
            int e = 0;
            App.ActivateDoc3(m.GetTitle(), true, (int)swRebuildOnActivation_e.swDontRebuildActiveDoc, ref e);
        }

        /// <summary>只关闭平台 output 目录下的文档 (历次运行的产物), 不影响用户自己打开的文件</summary>
        public static void CloseAll()
        {
            var titles = new List<string>();
            ModelDoc2 d = (ModelDoc2)App.GetFirstDocument();
            while (d != null)
            {
                string p = d.GetPathName();
                if (!string.IsNullOrEmpty(p) && p.StartsWith(Run.OutputRoot, StringComparison.OrdinalIgnoreCase)) titles.Add(d.GetTitle());
                d = (ModelDoc2)d.GetNext();
            }
            foreach (string t in titles)
            {
                try { App.CloseDoc(t); string t2 = Path.GetFileNameWithoutExtension(t); if (t2 != t) App.CloseDoc(t2); }
                catch (Exception e) { Log("!! 关闭失败 " + t + ": " + e.Message); }
            }
        }

        // ---------------- 基准面 (按特征树顺序, 与界面语言无关) ----------------
        public static List<Feature> Planes(ModelDoc2 m)
        {
            var list = new List<Feature>();
            Feature f = (Feature)m.FirstFeature();
            while (f != null && list.Count < 3)
            {
                if (f.GetTypeName2() == "RefPlane") list.Add(f);
                f = (Feature)f.GetNextFeature();
            }
            return list;
        }

        /// <summary>0=前视(XY, 法向+Z) 1=上视(XZ, 法向+Y) 2=右视(YZ, 法向+X); off=沿法向偏移(mm)</summary>
        public static Feature Plane(ModelDoc2 m, int idx, double off)
        {
            Feature b = Planes(m)[idx];
            if (Math.Abs(off) < 1e-9) return b;
            m.ClearSelection2(true);
            b.Select2(false, 0);
            int c = (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_Distance;
            if (off < 0) c |= (int)swRefPlaneReferenceConstraints_e.swRefPlaneReferenceConstraint_OptionFlip;
            Feature p = (Feature)m.FeatureManager.InsertRefPlane(c, Math.Abs(off) * MM, 0, 0, 0, 0);
            if (p == null) throw new Exception("基准面偏移失败 idx=" + idx + " off=" + off);
            return p;
        }

        public static Feature LastFeature(ModelDoc2 m)
        {
            Feature last = null, f = (Feature)m.FirstFeature();
            while (f != null) { last = f; f = (Feature)f.GetNextFeature(); }
            return last;
        }

        public static double Volume(ModelDoc2 m)
        {
            try { MassProperty mp = (MassProperty)m.Extension.CreateMassProperty(); return mp.Volume / (MM * MM * MM); }
            catch { return 0; }
        }

        /// <summary>未被特征消费的顶层草图 = 静默失败的特征。返回其名称列表</summary>
        public static List<string> OrphanSketches(ModelDoc2 m)
        {
            var r = new List<string>();
            Feature f = (Feature)m.FirstFeature();
            while (f != null)
            {
                if (f.GetTypeName2() == "ProfileFeature")
                {
                    object[] ch = (object[])f.GetChildren();
                    if (ch == null || ch.Length == 0) r.Add(f.Name);
                }
                f = (Feature)f.GetNextFeature();
            }
            return r;
        }

        public static int BodyCount(ModelDoc2 m)
        {
            object[] b = (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true);
            return b == null ? 0 : b.Length;
        }

        // ---------------- 属性 / 质量 / 外观 ----------------
        public static void Prop(ModelDoc2 m, string name, string val)
        {
            CustomPropertyManager cpm = m.Extension.get_CustomPropertyManager("");
            cpm.Add3(name, (int)swCustomInfoType_e.swCustomInfoText, val ?? "",
                (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);
        }

        public static string GetProp(ModelDoc2 m, string field)
        {
            try
            {
                CustomPropertyManager cpm = m.Extension.get_CustomPropertyManager("");
                string v, rv; cpm.Get4(field, false, out v, out rv);
                return string.IsNullOrEmpty(rv) ? (v ?? "") : rv;
            }
            catch { return ""; }
        }

        public static void SetColor(ModelDoc2 m, double[] c)
        {
            double[] p = new double[] { c[0], c[1], c[2], 1.0, 1.0, 0.45, 0.35, 0.0, 0.0 };
            m.Extension.SetMaterialPropertyValues(p, (int)swInConfigurationOpts_e.swThisConfiguration, null);
        }

        public static void SetBodyColor(Body2 b, double[] c)
        {
            b.MaterialPropertyValues2 = new double[] { c[0], c[1], c[2], 1.0, 1.0, 0.45, 0.35, 0.0, 0.0 };
        }

        public static void SetMaterial(ModelDoc2 m, string swMat)
        {
            ((PartDoc)m).SetMaterialPropertyName2("", MatDb, swMat);
        }

        /// <summary>质量覆盖: 自制件 = 实际体积 × GB 牌号密度; 外购件 = 样本质量</summary>
        public static void OverrideMass(ModelDoc2 m, double kg)
        {
            MassProperty mp = (MassProperty)m.Extension.CreateMassProperty();
            mp.SetOverrideMassValue(kg, (int)swInConfigurationOpts_e.swThisConfiguration, null);
            mp.OverrideMass = true;
        }

        public static double[] PartBox(ModelDoc2 m)
        {
            double[] b = (double[])((PartDoc)m).GetPartBox(true);
            return new double[] { b[0] / MM, b[1] / MM, b[2] / MM, b[3] / MM, b[4] / MM, b[5] / MM };
        }

        public static string BoxStr(double[] b)
        {
            return string.Format("X[{0:F0},{1:F0}] Y[{2:F0},{3:F0}] Z[{4:F0},{5:F0}]", b[0], b[3], b[1], b[4], b[2], b[5]);
        }
    }

    /// <summary>
    /// 草图上下文: 以全局坐标作图, 自动换算到基准面局部坐标 (标定结果见 docs/04):
    ///   XY 面(前视): (u,v)=(X,Y) -> 局部 (u, v),  法向 +Z
    ///   XZ 面(上视): (u,v)=(X,Z) -> 局部 (u,-v),  法向 +Y
    ///   YZ 面(右视): (u,v)=(Z,Y) -> 局部 (-u, v), 法向 +X
    /// 每个特征执行后检查体积变化, 特征静默失败或切除方向错误(切到空气)立即抛异常。
    /// </summary>
    public class Sk
    {
        public readonly ModelDoc2 M;
        readonly int pl;
        readonly SketchManager sm;
        const double K = Sw.MM;

        Sk(ModelDoc2 m, int plane, double off)
        {
            M = m; pl = plane; sm = m.SketchManager;
            Feature p = Sw.Plane(m, plane, off);
            m.ClearSelection2(true);
            p.Select2(false, 0);
            sm.InsertSketch(true);
            sm.AddToDB = true;   // 绕过推理/捕捉, 且逐段折线端点自动合并
        }

        public static Sk XY(ModelDoc2 m, double z = 0) { return new Sk(m, 0, z); }
        public static Sk XZ(ModelDoc2 m, double y = 0) { return new Sk(m, 1, y); }
        public static Sk YZ(ModelDoc2 m, double x = 0) { return new Sk(m, 2, x); }

        double LX(double u, double v) { return (pl == 2 ? -u : u) * K; }
        double LY(double u, double v) { return (pl == 1 ? -v : v) * K; }

        public Sk Line(double u1, double v1, double u2, double v2)
        { sm.CreateLine(LX(u1, v1), LY(u1, v1), 0, LX(u2, v2), LY(u2, v2), 0); return this; }

        public Sk CLine(double u1, double v1, double u2, double v2)
        { sm.CreateCenterLine(LX(u1, v1), LY(u1, v1), 0, LX(u2, v2), LY(u2, v2), 0); return this; }

        /// <summary>闭合折线 (u0,v0,u1,v1,...)</summary>
        public Sk Poly(params double[] p)
        {
            int n = p.Length / 2;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; Line(p[2 * i], p[2 * i + 1], p[2 * j], p[2 * j + 1]); }
            return this;
        }

        public Sk Rect(double u1, double v1, double u2, double v2)
        { return Poly(u1, v1, u2, v1, u2, v2, u1, v2); }

        public Sk RectC(double uc, double vc, double w, double h)
        { return Rect(uc - w / 2, vc - h / 2, uc + w / 2, vc + h / 2); }

        public Sk Circ(double u, double v, double d)
        { sm.CreateCircleByRadius(LX(u, v), LY(u, v), 0, d / 2 * K); return this; }

        /// <summary>圆弧: 圆心 + 起点 + 终点, 逆时针(全局 u-v 平面)</summary>
        public Sk Arc(double uc, double vc, double u1, double v1, double u2, double v2)
        {
            // YZ/XZ 面局部坐标有一轴取反 -> 旋向反转
            short dir = (short)(pl == 0 ? 1 : -1);
            sm.CreateArc(LX(uc, vc), LY(uc, vc), 0, LX(u1, v1), LY(u1, v1), 0, LX(u2, v2), LY(u2, v2), 0, dir);
            return this;
        }

        /// <summary>圆弧, 旋向显式给定 (全局 u-v 平面): dir=+1 逆时针, -1 顺时针。用于原样绘制外部几何 (如渐开线齿廓)</summary>
        public Sk ArcDir(double uc, double vc, double u1, double v1, double u2, double v2, int dir)
        {
            // YZ/XZ 面局部坐标有一轴取反 -> 旋向反转 (与 Arc 相同的标定)
            short d = (short)(pl == 0 ? dir : -dir);
            sm.CreateArc(LX(uc, vc), LY(uc, vc), 0, LX(u1, v1), LY(u1, v1), 0, LX(u2, v2), LY(u2, v2), 0, d);
            return this;
        }

        /// <summary>过点样条 (全局 u-v 坐标, mm)</summary>
        public Sk Spline(IReadOnlyList<double[]> pts)
        {
            var data = new double[pts.Count * 3];
            for (int i = 0; i < pts.Count; i++) { data[3 * i] = LX(pts[i][0], pts[i][1]); data[3 * i + 1] = LY(pts[i][0], pts[i][1]); }
            if (sm.CreateSpline2(data, true) == null) throw new Exception("样条创建失败");
            return this;
        }

        /// <summary>长圆孔 (两端圆心 + 宽度)</summary>
        public Sk Slot(double u1, double v1, double u2, double v2, double w)
        {
            double dx = u2 - u1, dy = v2 - v1, L = Math.Sqrt(dx * dx + dy * dy);
            double nx = -dy / L * w / 2, ny = dx / L * w / 2;
            Line(u1 + nx, v1 + ny, u2 + nx, v2 + ny);
            Line(u2 - nx, v2 - ny, u1 - nx, v1 - ny);
            Arc(u2, v2, u2 - nx, v2 - ny, u2 + nx, v2 + ny);
            Arc(u1, v1, u1 + nx, v1 + ny, u1 - nx, v1 - ny);
            return this;
        }

        /// <summary>圆角矩形</summary>
        public Sk RRect(double u1, double v1, double u2, double v2, double r)
        {
            Line(u1 + r, v1, u2 - r, v1); Line(u2, v1 + r, u2, v2 - r);
            Line(u2 - r, v2, u1 + r, v2); Line(u1, v2 - r, u1, v1 + r);
            Arc(u2 - r, v1 + r, u2 - r, v1, u2, v1 + r);
            Arc(u2 - r, v2 - r, u2, v2 - r, u2 - r, v2);
            Arc(u1 + r, v2 - r, u1 + r, v2, u1, v2 - r);
            Arc(u1 + r, v1 + r, u1, v1 + r, u1 + r, v1);
            return this;
        }

        /// <summary>正六边形 (对边宽 s)</summary>
        public Sk Hex(double uc, double vc, double s)
        {
            double r = s / Math.Sqrt(3);
            var p = new double[12];
            for (int i = 0; i < 6; i++) { double a = Math.PI / 6 + i * Math.PI / 3; p[2 * i] = uc + r * Math.Cos(a); p[2 * i + 1] = vc + r * Math.Sin(a); }
            return Poly(p);
        }

        void End()
        {
            sm.AddToDB = false;
            sm.InsertSketch(true);
            M.ClearSelection2(true);
            Feature last = Sw.LastFeature(M);
            if (last == null || last.GetTypeName2() != "ProfileFeature") throw new Exception("草图未创建");
            last.Select2(false, 0);
        }

        Feature Check(Feature f, double v0, bool add, string what)
        {
            if (f == null) throw new Exception("特征失败(返回 null): " + what);
            double v1 = Sw.Volume(M);
            if (add && v1 <= v0 + 1e-3) throw new Exception("特征未增加材料: " + what);
            if (!add && v1 >= v0 - 1e-3) throw new Exception("切除未去除材料(方向或位置错误): " + what);
            return f;
        }

        /// <summary>拉伸: dir=+1 沿法向, -1 反向, 0 两侧对称</summary>
        public Feature Ext(double d, int dir = 1, bool merge = true)
        {
            End(); double v0 = Sw.Volume(M);
            int ec = dir == 0 ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind;
            Feature f = M.FeatureManager.FeatureExtrusion3(true, false, dir < 0, ec, 0, d * K, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                merge, true, true, (int)swStartConditions_e.swStartSketchPlane, 0, false);
            return Check(f, v0, true, "拉伸 " + d);
        }

        /// <summary>切除: dir=+1 沿法向, -1 反向, 0 两侧对称(总深 d)</summary>
        public Feature Cut(double d, int dir = 1)
        {
            End(); double v0 = Sw.Volume(M);
            int ec = dir == 0 ? (int)swEndConditions_e.swEndCondMidPlane : (int)swEndConditions_e.swEndCondBlind;
            Feature f = M.FeatureManager.FeatureCut4(true, false, dir > 0, ec, 0, d * K, 0,
                false, false, false, false, 0, 0, false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            return Check(f, v0, false, "切除 " + d);
        }

        /// <summary>完全贯穿切除: dir=0 两侧, +1/-1 单侧。
        /// 双侧贯穿必须用"双端"(Sd=false)+两个方向各自 ThroughAll —— Sd=true 配 ThroughAllBoth 会静默失败。</summary>
        public Feature CutThru(int dir = 0)
        {
            End(); double v0 = Sw.Volume(M);
            int ta = (int)swEndConditions_e.swEndCondThroughAll;
            Feature f = M.FeatureManager.FeatureCut4(dir != 0, false, dir > 0,
                ta, dir == 0 ? ta : 0, 0, 0, false, false, false, false, 0, 0,
                false, false, false, false,
                false, true, true, false, false, false,
                (int)swStartConditions_e.swStartSketchPlane, 0, false, false);
            return Check(f, v0, false, "贯穿切除");
        }

        /// <summary>绕草图中唯一中心线旋转 360°</summary>
        public Feature Rev(bool cut = false, bool merge = true)
        {
            End(); double v0 = Sw.Volume(M);
            Feature f = M.FeatureManager.FeatureRevolve2(true, true, false, cut, false, false,
                (int)swEndConditions_e.swEndCondBlind, 0, 2 * Math.PI, 0, false, false, 0, 0, 0, 0, 0, merge, true, true);
            return Check(f, v0, !cut, cut ? "旋转切除" : "旋转");
        }
    }

    /// <summary>回转体: 轮廓 (轴向 a, 半径 r) 对, 轴线可取 X/Y/Z, 过原点</summary>
    public static class Turn
    {
        /// <summary>axis: 'X','Y','Z'; prof: a0,r0,a1,r1,... 闭合轮廓(可含 r=0 段)</summary>
        public static Feature Solid(ModelDoc2 m, char axis, double[] prof, bool cut = false, bool merge = true)
        {
            int n = prof.Length / 2;
            double amin = double.MaxValue, amax = double.MinValue;
            for (int i = 0; i < n; i++) { amin = Math.Min(amin, prof[2 * i]); amax = Math.Max(amax, prof[2 * i]); }
            Sk s;
            var p = new double[prof.Length];
            if (axis == 'Z')
            {   // YZ 面: u=Z, v=Y
                s = Sk.YZ(m);
                s.CLine(amin - 10, 0, amax + 10, 0);
                for (int i = 0; i < n; i++) { p[2 * i] = prof[2 * i]; p[2 * i + 1] = prof[2 * i + 1]; }
            }
            else if (axis == 'X')
            {   // XY 面: u=X, v=Y
                s = Sk.XY(m);
                s.CLine(amin - 10, 0, amax + 10, 0);
                for (int i = 0; i < n; i++) { p[2 * i] = prof[2 * i]; p[2 * i + 1] = prof[2 * i + 1]; }
            }
            else
            {   // Y 轴: XY 面, u=X(半径), v=Y(轴向)
                s = Sk.XY(m);
                s.CLine(0, amin - 10, 0, amax + 10);
                for (int i = 0; i < n; i++) { p[2 * i] = prof[2 * i + 1]; p[2 * i + 1] = prof[2 * i]; }
            }
            s.Poly(p);
            return s.Rev(cut, merge);
        }

        /// <summary>阶梯轴轮廓生成: 段 (长度, 直径) 依次排列, 起点 a0; 返回闭合轮廓(实心)</summary>
        public static double[] Steps(double a0, params double[] segs)
        {
            var pts = new List<double>();
            double a = a0;
            pts.Add(a); pts.Add(0);
            for (int i = 0; i < segs.Length / 2; i++)
            {
                double L = segs[2 * i], r = segs[2 * i + 1] / 2;
                pts.Add(a); pts.Add(r);
                a += L;
                pts.Add(a); pts.Add(r);
            }
            pts.Add(a); pts.Add(0);
            // 去掉重复点
            var q = new List<double>();
            for (int i = 0; i < pts.Count / 2; i++)
            {
                if (q.Count >= 2 && Math.Abs(q[q.Count - 2] - pts[2 * i]) < 1e-9 && Math.Abs(q[q.Count - 1] - pts[2 * i + 1]) < 1e-9) continue;
                q.Add(pts[2 * i]); q.Add(pts[2 * i + 1]);
            }
            return q.ToArray();
        }
    }
}
