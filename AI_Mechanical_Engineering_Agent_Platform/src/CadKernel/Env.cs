using System;
using System.IO;
using System.Text.Json;

namespace CadKernel
{
    /// <summary>
    /// 本机环境: SolidWorks 安装目录、Python 解释器。默认值是本机实测路径,
    /// 可由 config/cad.json 或环境变量 CAD_SOLIDWORKS_DIR / CAD_PYTHON 覆盖。
    /// </summary>
    public static class Env
    {
        public static string SolidWorksDir = @"C:\Program Files\SOLIDWORKS2023\SOLIDWORKS";
        public static string Python = "python";

        public static string SolidWorksExe { get { return Path.Combine(SolidWorksDir, "SLDWORKS.exe"); } }
        public static string MaterialDb
        {
            get { return Path.Combine(SolidWorksDir, @"lang\chinese-simplified\sldmaterials\solidworks materials.sldmat"); }
        }

        /// <summary>读取 &lt;平台根&gt;/config/cad.json (可选), 再应用环境变量</summary>
        public static void Load(string platformRoot)
        {
            string cfg = Path.Combine(platformRoot, "config", "cad.json");
            if (File.Exists(cfg))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(cfg));
                if (doc.RootElement.TryGetProperty("solidworks_dir", out var sw) && sw.ValueKind == JsonValueKind.String)
                    SolidWorksDir = sw.GetString();
                if (doc.RootElement.TryGetProperty("python", out var py) && py.ValueKind == JsonValueKind.String)
                    Python = py.GetString();
            }
            string e1 = Environment.GetEnvironmentVariable("CAD_SOLIDWORKS_DIR");
            if (!string.IsNullOrWhiteSpace(e1)) SolidWorksDir = e1;
            string e2 = Environment.GetEnvironmentVariable("CAD_PYTHON");
            if (!string.IsNullOrWhiteSpace(e2)) Python = e2;
        }

        /// <summary>
        /// SolidWorks 用户选项中的默认零件模板 (内核运行时经 API 读同一选项, 工程图模板 gb_a3.drwdot 等取其所在目录)。
        /// 从注册表 HKCU\Software\SolidWorks\SOLIDWORKS &lt;年份&gt;\Document Templates 读取, 年份 = SLDWORKS.exe 主版本号 + 1992。
        /// 读不到返回 null。
        /// </summary>
        public static string DefaultPartTemplate()
        {
            try
            {
                int major = System.Diagnostics.FileVersionInfo.GetVersionInfo(SolidWorksExe).FileMajorPart;
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\SolidWorks\SOLIDWORKS " + (major + 1992) + @"\Document Templates");
                string v = k == null ? null : k.GetValue("Default Part template") as string;
                return string.IsNullOrWhiteSpace(v) ? null : v;
            }
            catch { return null; }
        }

        /// <summary>自 start 向上查找含 AutoCADByAI.sln 的目录 (平台根)</summary>
        public static string FindPlatformRoot(string start)
        {
            var d = new DirectoryInfo(start);
            while (d != null)
            {
                if (File.Exists(Path.Combine(d.FullName, "AutoCADByAI.sln"))) return d.FullName;
                d = d.Parent;
            }
            throw new DirectoryNotFoundException("找不到平台根目录 (AutoCADByAI.sln), 起点: " + start);
        }
    }
}
