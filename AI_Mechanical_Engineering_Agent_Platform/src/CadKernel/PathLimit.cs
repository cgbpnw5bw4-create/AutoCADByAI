using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CadKernel
{
    /// <summary>
    /// 运行前的模型路径长度检查 (不连接 SolidWorks; cad doctor 与 cad run / resume 开始时调用)。
    /// 依据 docs/api_calibration.md 第 4 节第 10 条: 平台放在 git worktree 深目录时, 微波实验炉箱体模块装配图在
    /// View.GetPolylines7 处无限挂起 (4 次 4 次), subst 映射短盘符后一次通过; 当时最长的模型路径 (A14 热电偶子装配)
    /// 按 GBK 263 字节, 主目录下同一文件 222 字节。推断 SolidWorks 某处按 ANSI 路径处理 (未证实), 判据只用 MAX_PATH 与这两个实测点:
    /// <list type="bullet">
    /// <item>≥ 260 字节 (MaxPath 含结尾 NUL, 路径本身最多 259 字节): 超出 ANSI 路径缓冲, 实测 263 字节挂起 → 要出图的运行不启动;</item>
    /// <item>222 (Verified) &lt; 字节 &lt; 260: 接近上限, 没有运行验证过 → 提示, 照常运行;</item>
    /// <item>≤ 222: 实测全流程 (含出图) 通过。</item>
    /// </list>
    /// 字节数按系统 ANSI 代码页 (GetACP, 本机 936 = GBK) 计。只算 models/ 下的零件与装配体 (挂起时超长的是被装配图引用的子装配),
    /// 工程图、导出文件与 SolidWorks 自建的临时文件不在内。
    /// </summary>
    public static class PathLimit
    {
        /// <summary>MAX_PATH: ANSI 路径缓冲的字节数, 含结尾 NUL</summary>
        public const int MaxPath = 260;
        /// <summary>实测全流程 (含出图) 通过的最长模型路径字节数: 主目录微波实验炉 2026-10-16 基准运行的 A14 热电偶子装配</summary>
        public const int Verified = 222;

        public enum Level { Ok, Unverified, Over }

        public sealed class Estimate
        {
            /// <summary>最长的模型文件完整路径 (不知道文件名时为 models 目录本身)</summary>
            public string FullPath;
            public string RunDir;
            public int Bytes, Chars, Files;
            /// <summary>文件名来源; null = 运行前不知道文件名 (设计在建模代码中命名, 且该任务没有运行过)</summary>
            public string Source;
            public Level Level { get { return Classify(Bytes); } }
        }

        [DllImport("kernel32.dll")] static extern int GetACP();
        static Encoding ansi;

        /// <summary>系统 ANSI 代码页 (非 Unicode 程序所用; 本机 936 = GBK)</summary>
        public static Encoding Ansi
        {
            get
            {
                if (ansi == null) { int cp = GetACP(); ansi = CodePagesEncodingProvider.Instance.GetEncoding(cp) ?? Encoding.GetEncoding(cp); }
                return ansi;
            }
        }

        public static int Bytes(string path, Encoding enc = null) { return (enc ?? Ansi).GetByteCount(path); }

        public static Level Classify(int bytes) { return bytes >= MaxPath ? Level.Over : bytes > Verified ? Level.Unverified : Level.Ok; }

        /// <summary>runDir/models 下各文件 (相对 models/ 的路径或完整路径) 中按字节最长者; 没有文件时取 models 目录本身</summary>
        public static Estimate Longest(string runDir, IEnumerable<string> modelFiles, string source, Encoding enc = null)
        {
            string models = Path.Combine(runDir, "models");
            var all = (modelFiles ?? Enumerable.Empty<string>()).Select(f => Path.Combine(models, f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string top = all.Count == 0 ? models : all.OrderByDescending(f => Bytes(f, enc)).ThenBy(f => f, StringComparer.Ordinal).First();
            return new Estimate { FullPath = top, RunDir = runDir, Bytes = Bytes(top, enc), Chars = top.Length, Files = all.Count, Source = source };
        }

        /// <summary>
        /// 估算一次运行的最长模型路径。resumeDir 为空 = 新运行 (运行目录按 Run.NewDirName 拼出, 长度与实际一致)。
        /// 文件名: 设计项目声明的 (CadProject.ModelFiles, 导入设计取自 import.json) + 续跑时运行目录 models/ 下已有的;
        /// 两者都没有时用该任务最近一次运行的 models/ (设计在建模代码中命名, 运行前不知道文件名)。
        /// </summary>
        public static Estimate ForRun(CadProject project, string task, string resumeDir = null)
        {
            string runDir = resumeDir != null ? Path.GetFullPath(resumeDir) : Path.Combine(Run.OutputRoot, Run.NewDirName(task));
            var files = new List<string>();
            var source = new List<string>();
            var planned = project.ModelFiles();         // 读不出照常抛出: 吞掉会把超长当作"不知道文件名"放过去
            if (planned != null) { files.AddRange(planned); source.Add("输入任务"); }
            if (resumeDir != null)
            {
                var have = ModelsIn(runDir);
                if (have.Count > 0) { files.AddRange(have); source.Add("运行目录现有文件"); }
            }
            if (source.Count == 0)
            {
                string last = LastRun(task);
                if (last != null)
                {
                    files.AddRange(ModelsIn(last).Select(f => Path.GetRelativePath(Path.Combine(last, "models"), f)));
                    source.Add("上次运行 " + Path.GetFileName(last));
                }
            }
            return Longest(runDir, files, source.Count == 0 ? null : string.Join(" + ", source));
        }

        static List<string> ModelsIn(string runDir)
        {
            string models = Path.Combine(runDir, "models");
            if (!Directory.Exists(models)) return new List<string>();
            return Directory.GetFiles(models, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("~$") &&
                            (f.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        /// <summary>output/ 下该任务最近一次有 models/ 的运行目录</summary>
        static string LastRun(string task)
        {
            if (!Directory.Exists(Run.OutputRoot)) return null;
            var re = new Regex(@"^\d{8}_\d{6}_" + Regex.Escape(Run.Safe(task)) + "$");
            return Directory.GetDirectories(Run.OutputRoot).Where(d => re.IsMatch(Path.GetFileName(d)) && ModelsIn(d).Count > 0)
                            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal).FirstOrDefault();
        }

        /// <summary>一行概要: 字节数、字符数、最长的文件 (相对运行目录)</summary>
        public static string Describe(Estimate e)
        {
            return string.Format("最长模型路径 {0} 字节 (ANSI {1}) / {2} 字符{3}: {4}", e.Bytes, Ansi.CodePage, e.Chars,
                e.Source == null ? " (运行前不知道文件名, 只算到 models 目录)" : " (文件名来自" + e.Source + ")",
                e.FullPath.StartsWith(e.RunDir, StringComparison.OrdinalIgnoreCase) ? "<运行目录>" + e.FullPath.Substring(e.RunDir.Length) : e.FullPath);
        }

        /// <summary>超限或接近上限时的说明与建议 (每条一行); Level.Ok 时为空</summary>
        public static List<string> Advice(Estimate e, string platformRoot, bool resume, Encoding enc = null)
        {
            var r = new List<string>();
            if (e.Level == Level.Ok) return r;
            r.Add(e.Level == Level.Over
                ? string.Format("≥ MAX_PATH {0} 字节: 实测此时出图在 View.GetPolylines7 处无限挂起 (docs/api_calibration.md 第 4 节第 10 条), 须缩短 {1} 字节以上",
                                MaxPath, e.Bytes - MaxPath + 1)
                : string.Format("接近 MAX_PATH {0} 字节: 超过实测可用的 {1} 字节, 这一区间未经运行验证; 出图若挂起, 先查路径长度", MaxPath, Verified));
            string root = Path.GetFullPath(platformRoot).TrimEnd('\\') + "\\";
            if (!e.FullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                r.Add("运行目录不在平台目录下: 缩短运行目录路径或零件 / 装配名称");
                return r;
            }
            int mapped = Bytes(@"W:\" + e.FullPath.Substring(root.Length), enc);
            if (Classify(mapped) < e.Level)
                r.Add(string.Format("建议把平台目录映射到短盘符 (盘符任选未占用的): subst W: \"{0}\", 再到 W:\\ 下执行 cad (映射后最长约 {1} 字节{2}){3}",
                                    root.TrimEnd('\\'), mapped, Classify(mapped) == Level.Ok ? "" : ", 仍超过实测可用的 " + Verified,
                                    resume ? "; 续跑须用新建运行时的同一盘符 (运行目录记录的是绝对路径), 长路径下建的运行须在短盘符下重新 cad run" : ""));
            else
                r.Add(string.Format("映射到盘符根后仍有约 {0} 字节: 须缩短零件 / 装配名称 (导入设计在 import.json 中改 name)", mapped));
            return r;
        }
    }
}
