using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>交付文件导出。来源: CFC-650 Export.cs (总装 24 MB STEP 回读后 3547 个零部件颜色完整)。</summary>
    public static class Export
    {
        /// <summary>
        /// 另存 STEP AP214 (含颜色) 到 exports/。颜色要求: AP214 (AP203 不含颜色) + "输出面/边属性" + "输出外观";
        /// 这三项是用户级系统选项, 导出前记下原值, 导出后恢复。被隐藏的零部件先显示 (隐藏件不写入 STEP)。
        /// 返回导出文件路径。
        /// </summary>
        public static string Step(string src)
        {
            Sw.Head("STEP 导出: " + Path.GetFileName(src));
            Directory.CreateDirectory(Run.Exports);
            Sw.CloseAll();
            ModelDoc2 m = Sw.Open(src);
            if (m.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                AssemblyDoc a = (AssemblyDoc)m;
                try { a.ResolveAllLightWeightComponents(false); } catch { }      // 轻化零部件无几何, 不能导出
                int shown = 0, total = 0;
                foreach (object o in (object[])a.GetComponents(false) ?? new object[0])
                {
                    Component2 c = (Component2)o;
                    total++;
                    if (c.Visible == (int)swComponentVisibilityState_e.swComponentHidden)
                    {
                        c.Visible = (int)swComponentVisibilityState_e.swComponentVisible;
                        shown++;
                    }
                }
                Sw.Log(string.Format("零部件 {0} (显示被隐藏的 {1})", total, shown));
            }

            var app = Sw.App;
            int ap0 = app.GetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP);
            bool fe0 = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportFaceEdgeProps);
            bool ap0b = app.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAppearances);
            string outp = Path.Combine(Run.Exports, Path.GetFileNameWithoutExtension(src) + ".step");
            try
            {
                app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, 214);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportFaceEdgeProps, true);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAppearances, true);
                int e = 0, w = 0;
                var t0 = DateTime.Now;
                bool ok = m.Extension.SaveAs(outp, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null, ref e, ref w);
                if (!ok || e != 0) throw new Exception("STEP 导出失败 err=" + e + " warn=" + w);
                Sw.Log(string.Format("STEP AP214 -> {0}  {1:F1} MB  ({2:F1} min)", Run.Rel(outp), new FileInfo(outp).Length / 1048576.0, (DateTime.Now - t0).TotalMinutes));
            }
            finally
            {
                app.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swStepAP, ap0);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportFaceEdgeProps, fe0);
                app.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swStepExportAppearances, ap0b);
                Sw.Log(string.Format("已恢复导出选项: AP{0}, 面/边属性 {1}, 外观 {2}", ap0, fe0, ap0b));
            }
            Sw.App.CloseDoc(m.GetTitle());   // 不保存 (仅可见性改动)
            return outp;
        }
    }
}
