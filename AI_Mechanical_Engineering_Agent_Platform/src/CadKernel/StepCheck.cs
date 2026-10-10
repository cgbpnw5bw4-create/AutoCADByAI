using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

namespace CadKernel
{
    /// <summary>
    /// STEP 导出物的身份与内容核对 (不依赖 SolidWorks 读回):
    ///   文件头为 ISO-10303-21; 含实体 (MANIFOLD_SOLID_BREP); 根产品名 = 源文件名;
    ///   源装配体的每个零件名都出现在 STEP 的 PRODUCT 中。
    /// 背景: 旧平台 V2.2-C 发现多文档会话里 STEP 导出了别的文档 (格式合法、字节稳定, 但几何不是本次模型)。
    /// 本检查只读文件文本, 能直接抓住"导出了错的文档"这一类假成功。
    /// </summary>
    public static class StepCheck
    {
        static readonly Regex Product = new Regex(@"PRODUCT\s*\(\s*'((?:[^']|'')*)'", RegexOptions.Compiled);
        static readonly Regex X2 = new Regex(@"\\X2\\([0-9A-Fa-f]+)\\X0\\", RegexOptions.Compiled);

        /// <summary>解码 STEP 字符串中的 \X2\..\X0\ (UTF-16 十六进制) 转义</summary>
        public static string Decode(string s)
        {
            return X2.Replace(s.Replace("''", "'"), m =>
            {
                string h = m.Groups[1].Value;
                var sb = new StringBuilder();
                for (int i = 0; i + 4 <= h.Length; i += 4) sb.Append((char)int.Parse(h.Substring(i, 4), NumberStyles.HexNumber));
                return sb.ToString();
            });
        }

        /// <summary>STEP 文本中的产品名集合 (已解码)</summary>
        public static HashSet<string> Products(string stepText)
        {
            var r = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Product.Matches(stepText)) r.Add(Decode(m.Groups[1].Value));
            return r;
        }

        /// <summary>实体个数 (MANIFOLD_SOLID_BREP)</summary>
        public static int Breps(string stepText) { return Regex.Matches(stepText, @"MANIFOLD_SOLID_BREP\s*\(").Count; }

        /// <summary>纯文本核对: 期望的根产品名与零件名 (不含扩展名)</summary>
        public static bool VerifyText(string stepText, string rootName, IEnumerable<string> partNames, out string summary)
        {
            var issues = new List<string>();
            if (!stepText.StartsWith("ISO-10303-21", StringComparison.Ordinal)) issues.Add("文件头不是 ISO-10303-21");
            int breps = Breps(stepText);
            if (breps == 0) issues.Add("没有实体 (MANIFOLD_SOLID_BREP)");
            var prods = Products(stepText);
            if (!prods.Contains(rootName)) issues.Add("根产品名不是 " + rootName);
            var parts = partNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var missing = parts.Where(p => !prods.Contains(p)).ToList();
            if (missing.Count > 0) issues.Add(string.Format("缺少零件 {0}/{1}: {2}", missing.Count, parts.Count, string.Join(", ", missing.Take(5))));
            summary = issues.Count == 0
                ? string.Format("根产品 {0}, 零件 {1} 种全部在内, 实体 {2} 个", rootName, parts.Count, breps)
                : string.Join("; ", issues);
            return issues.Count == 0;
        }

        /// <summary>按源文件 (装配体或零件) 核对; 装配体的零件清单从 SolidWorks 读出</summary>
        public static bool Verify(string stepPath, string sourcePath, out string summary)
        {
            string root = Path.GetFileNameWithoutExtension(sourcePath);
            var names = new List<string>();
            if (sourcePath.EndsWith(".SLDASM", StringComparison.OrdinalIgnoreCase))
            {
                Sw.CloseAll();
                ModelDoc2 m = Sw.Open(sourcePath);
                AssemblyDoc a = (AssemblyDoc)m;
                try { a.ResolveAllLightWeightComponents(false); } catch { }
                foreach (object o in (object[])a.GetComponents(false) ?? new object[0])
                {
                    string p = ((Component2)o).GetPathName() ?? "";
                    if (p.EndsWith(".SLDPRT", StringComparison.OrdinalIgnoreCase)) names.Add(Path.GetFileNameWithoutExtension(p));
                }
                Sw.CloseAll();
            }
            return VerifyText(File.ReadAllText(stepPath, Encoding.Latin1), root, names, out summary);
        }
    }
}
