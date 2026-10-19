using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    // ======================================================================== import.json
    /// <summary>
    /// 由 STEP 导入的设计数据 (input/&lt;任务&gt;/import.json, cad import 生成, 之后可手工或由 AI 编辑):
    /// 零件表 (名称/代号/类别/规格/材料 + 几何来源)、装配层级 (每个子件相对父装配的变换)、原 STEP 中每个零件实例的世界变换 (核对用)。
    /// 变换为 12 个数: 局部 X 轴、Y 轴、Z 轴在父坐标中的方向, 平移 (mm)。
    /// </summary>
    public sealed class ImportModel
    {
        public string Note { get; set; }
        public ImportSource Source { get; set; }
        public string Root { get; set; }
        public List<ImportPart> Parts { get; set; } = new List<ImportPart>();
        public List<ImportAsm> Assemblies { get; set; } = new List<ImportAsm>();
        /// <summary>原 STEP 中每个零件实例的世界变换 (零件 id → 列表)。改动装配后用 cad import 重新生成或同步修改, 否则位置核对不通过</summary>
        public Dictionary<string, List<double[]>> World { get; set; } = new Dictionary<string, List<double[]>>();
        /// <summary>导入时发现的问题 (如实例数与 SolidWorks 不符); 非空时 cad import 返回非 0</summary>
        public List<string> Warnings { get; set; } = new List<string>();
        /// <summary>
        /// 原模型自带的干涉 (cad import 时对原装配做干涉检查得到, 同一对零件、同一相对位置、同样体积的只记一条)。
        /// 运行时与之相符的干涉记为"已登记接受", 仍列入报告; 其余为新增, 判不通过。
        /// </summary>
        public List<KnownInterference> AcceptedInterferences { get; set; } = new List<KnownInterference>();

        /// <summary>
        /// 检出的干涉是否为已登记的原模型干涉: 同一对零件, B 相对 A 的位置一致 (±0.01 mm, 轴方向分量 ±1e-6),
        /// 体积一致 (±1%, 至少 ±0.5 mm³)。A、B 的先后不固定, 两种顺序都比。
        /// 移动了零件、改了尺寸 (如密封垫厚度) 的干涉都对不上 → 按新增处理。
        /// </summary>
        public bool Accepts(string partA, string partB, Xf rel, double volumeMm3)
        {
            if (partA == null || partB == null || rel == null) return false;
            Xf inv = null;
            foreach (var k in AcceptedInterferences)
            {
                if (Math.Abs(k.VolumeMm3 - volumeMm3) > Math.Max(0.5, 0.01 * k.VolumeMm3)) continue;
                double dt, dr;
                if (k.PartA == partA && k.PartB == partB)
                {
                    Xf.Diff(Xf.FromRow(k.Xf), rel, out dt, out dr);
                    if (dt <= 0.01 && dr <= 1e-6) return true;
                }
                if (k.PartA == partB && k.PartB == partA)
                {
                    Xf.Diff(Xf.FromRow(k.Xf), inv ?? (inv = rel.Inverse()), out dt, out dr);
                    if (dt <= 0.01 && dr <= 1e-6) return true;
                }
            }
            return false;
        }

        public const string FileName = "import.json";

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        };

        public static ImportModel Load(string dir)
        {
            string f = Path.Combine(dir, FileName);
            if (!File.Exists(f)) throw new FileNotFoundException("缺少 " + FileName + " (先运行 cad import): " + dir);
            return JsonSerializer.Deserialize<ImportModel>(File.ReadAllText(f), Json) ?? throw new Exception(FileName + " 为空");
        }

        public string ToJson() { return JsonSerializer.Serialize(this, Json); }

        public ImportPart Part(string id) { return Parts.FirstOrDefault(p => p.Id == id); }
        public ImportAsm Asm(string id) { return Assemblies.FirstOrDefault(a => a.Id == id); }

        /// <summary>纯数据校验 (不连接 SolidWorks): 编号唯一、引用存在、无循环、变换为刚体、材料与几何参数有效、几何文件存在 (dir 非空时)</summary>
        public List<string> Validate(string dir)
        {
            var r = new List<string>();
            foreach (var g in Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1)) r.Add("零件编号重复: " + g.Key);
            foreach (var g in Assemblies.GroupBy(a => a.Id).Where(g => g.Count() > 1)) r.Add("装配编号重复: " + g.Key);
            if (Asm(Root) == null) r.Add("根装配不存在: " + Root);
            foreach (var p in Parts)
            {
                try { Mat.ByKey(p.Material); } catch (Exception e) { r.Add(p.Id + ": " + e.Message); }
                if (p.Kind != "自制件" && p.Kind != "外购件") r.Add(p.Id + ": 类别应为 自制件 / 外购件");
                var m = p.Model;
                if (m == null) { r.Add(p.Id + ": 缺少 model"); continue; }
                if (m.Type == "tube")
                {
                    if (!(m.Dout > 0 && m.Din >= 0 && m.Din < m.Dout && m.Length > 0)) r.Add(p.Id + ": 管参数无效 (dout > din ≥ 0, length > 0)");
                    if (m.Axis != "X" && m.Axis != "Y" && m.Axis != "Z") r.Add(p.Id + ": 管轴向应为 X / Y / Z");
                    if (m.Center == null || m.Center.Length != 2) r.Add(p.Id + ": 管中心应为 2 个数");
                }
                else if (m.Type == "geometry" || m.Type == "bodies")
                {
                    if (string.IsNullOrEmpty(m.File)) r.Add(p.Id + ": 缺少几何文件");
                    else if (dir != null && !File.Exists(Path.Combine(dir, m.File))) r.Add(p.Id + ": 几何文件不存在 " + m.File);
                    if (m.Type == "bodies")
                    {
                        if (m.Rebuilt().Count == 0) r.Add(p.Id + ": bodies 缺少 prisms / sheets");
                        for (int i = 0; i < (m.Prisms?.Count ?? 0); i++) { string e = Prism.Check(m.Prisms[i]); if (e != null) r.Add(p.Id + " 截面拉伸 " + (i + 1) + ": " + e); }
                        for (int i = 0; i < (m.Sheets?.Count ?? 0); i++) { string e = SheetMetal.Check(m.Sheets[i]); if (e != null) r.Add(p.Id + " 钣金 " + (i + 1) + ": " + e); }
                        if (m.Rebuilt().Count > p.Bodies) r.Add(p.Id + ": 重建实体数多于实体数 " + p.Bodies);
                    }
                }
                else r.Add(p.Id + ": 未知 model.type " + m.Type);
            }
            foreach (var a in Assemblies)
                foreach (var c in a.Children)
                {
                    if (Part(c.Ref) == null && Asm(c.Ref) == null) r.Add(a.Id + ": 引用不存在 " + c.Ref);
                    string e = RigidError(c.Xf);
                    if (e != null) r.Add(a.Id + " → " + c.Ref + ": " + e);
                }
            foreach (var k in AcceptedInterferences)
            {
                if (Part(k.PartA) == null || Part(k.PartB) == null) r.Add("已知干涉引用了不存在的零件: " + k.PartA + " / " + k.PartB);
                string e = RigidError(k.Xf);
                if (e != null) r.Add("已知干涉 " + k.PartA + " / " + k.PartB + ": " + e);
                if (!(k.VolumeMm3 > 0)) r.Add("已知干涉 " + k.PartA + " / " + k.PartB + ": 体积应大于 0");
            }
            // 循环引用
            var state = new Dictionary<string, int>();
            Func<string, bool> cyc = null;
            cyc = id =>
            {
                int s; if (state.TryGetValue(id, out s)) return s == 1;
                state[id] = 1;
                var a = Asm(id);
                if (a != null) foreach (var c in a.Children) if (Asm(c.Ref) != null && cyc(c.Ref)) return true;
                state[id] = 2; return false;
            };
            foreach (var a in Assemblies) if (cyc(a.Id)) { r.Add("装配层级存在循环: " + a.Id); break; }
            return r;
        }

        /// <summary>12 数变换是否为刚体 (三轴单位长、两两正交、右手系); 否则返回原因</summary>
        public static string RigidError(double[] x)
        {
            if (x == null || x.Length != 12) return "变换应为 12 个数";
            Func<int, int, double> dot = (i, j) => x[i] * x[j] + x[i + 1] * x[j + 1] + x[i + 2] * x[j + 2];
            const double e = 1e-6;
            if (Math.Abs(dot(0, 0) - 1) > e || Math.Abs(dot(3, 3) - 1) > e || Math.Abs(dot(6, 6) - 1) > e) return "轴方向不是单位向量";
            if (Math.Abs(dot(0, 3)) > e || Math.Abs(dot(0, 6)) > e || Math.Abs(dot(3, 6)) > e) return "三轴不正交";
            double[] cr = { x[1] * x[5] - x[2] * x[4], x[2] * x[3] - x[0] * x[5], x[0] * x[4] - x[1] * x[3] };   // X × Y
            if (cr[0] * x[6] + cr[1] * x[7] + cr[2] * x[8] < 0) return "不是右手系 (镜像)";
            return null;
        }
    }

    /// <summary>一处已知干涉: 零件 A、B (零件编号), B 相对 A 的变换 (12 数), 干涉体积; note 为可读的零件名</summary>
    public sealed class KnownInterference
    {
        public string PartA { get; set; }
        public string PartB { get; set; }
        public double[] Xf { get; set; }
        public double VolumeMm3 { get; set; }
        public string Note { get; set; }
    }

    public sealed class ImportSource
    {
        public string File { get; set; }
        public long Bytes { get; set; }
        public string Modified { get; set; }
        public string ImportedAt { get; set; }
        public string SolidWorks { get; set; }
    }

    public sealed class ImportPart
    {
        public string Id { get; set; }
        /// <summary>原 STEP 中的零件名 (已解码)</summary>
        public string OriginalName { get; set; }
        public string Kind { get; set; }
        public string Code { get; set; }
        public string Name { get; set; }
        public string Spec { get; set; }
        public string Brand { get; set; }
        /// <summary>CadKernel.Mat 的字段名 (S304 / AL / PVC / UNKNOWN …)</summary>
        public string Material { get; set; }
        public string Remark { get; set; }
        public int Bodies { get; set; }
        /// <summary>原 STEP 几何的体积 (mm³) 与包围盒 (xmin,ymin,zmin,xmax,ymax,zmax, mm), 运行时核对</summary>
        public double VolumeMm3 { get; set; }
        public double[] BoxMm { get; set; }
        public ImportGeometry Model { get; set; }
    }

    /// <summary>
    /// 几何来源: geometry = 原几何 (Parasolid x_t); tube = 参数化直管 (沿坐标轴, 零件坐标);
    /// bodies = 按实体重建: prisms 中的实体用截面拉伸、sheets 中的用钣金特征重建; 未全部重建时, 其余实体取自 file
    /// (载入后按体积与包围盒删去被重建的实体)。file 也是运行时逐实体差集核对的依据。
    /// </summary>
    public sealed class ImportGeometry
    {
        public string Type { get; set; }
        public string File { get; set; }
        public List<PrismDef> Prisms { get; set; }
        public List<SheetDef> Sheets { get; set; }
        /// <summary>全部被重建的实体 (截面拉伸 + 钣金)</summary>
        public List<IBodyDef> Rebuilt() { return (Prisms ?? new List<PrismDef>()).Cast<IBodyDef>().Concat((Sheets ?? new List<SheetDef>()).Cast<IBodyDef>()).ToList(); }
        public string Axis { get; set; }
        /// <summary>管轴线在草图面上的坐标 (按 Sk 的 u,v: 轴 Z → (X,Y); 轴 Y → (X,Z); 轴 X → (Z,Y))</summary>
        public double[] Center { get; set; }
        public double Start { get; set; }
        public double Length { get; set; }
        public double Dout { get; set; }
        public double Din { get; set; }
    }

    public sealed class ImportAsm
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<ImportChild> Children { get; set; } = new List<ImportChild>();
    }

    public sealed class ImportChild
    {
        public string Ref { get; set; }
        public double[] Xf { get; set; }
    }

    // ======================================================================== 名称与分类 (纯逻辑)
    public static class ImportNames
    {
        static readonly Regex X2u = new Regex(@"_X2_([0-9A-Fa-f]+)_X0_", RegexOptions.Compiled);

        /// <summary>
        /// 还原中文名。实测 (2026-10-10): STEP 里中文以 \X2\…\X0\ 转义 (UTF-16 十六进制), SolidWorks 2023 导入时不解码,
        /// 把反斜杠换成下划线写进文档名 (_X2_4e0d…_X0_ = 不锈钢)。两种写法都还原。
        /// </summary>
        public static string Decode(string s)
        {
            s = StepCheck.Decode(s ?? "");
            return X2u.Replace(s, m =>
            {
                string h = m.Groups[1].Value; var sb = new StringBuilder();
                for (int i = 0; i + 4 <= h.Length; i += 4) sb.Append((char)int.Parse(h.Substring(i, 4), NumberStyles.HexNumber));
                return sb.ToString();
            });
        }

        /// <summary>
        /// 解码, 去掉扩展名与 SolidWorks 导出时附加的配置名: STEP 中为 "_默认&lt;按加工&gt;" / "_Default",
        /// 导入后的文档名里尖括号变成下划线 ("_默认_按加工_")
        /// </summary>
        public static string Clean(string s)
        {
            s = Decode(s).Trim();
            s = Regex.Replace(s, @"\.(sldprt|sldasm)$", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"_(默认|Default)(<[^<>]*>|_[^_]*_)?$", "");
            return s.Trim();
        }

        static string Lead(string name)
        {
            var m = Regex.Match(name, @"^[A-Za-z][A-Za-z0-9]*\d[A-Za-z0-9]*");
            return m.Success ? m.Value : null;
        }

        /// <summary>
        /// 本设备自制件的图号前缀: 名称开头的字母数字串 (≥8 位) 取前 8 位, 出现 ≥3 次中最多的一个 (如 RTWBL340)。
        /// 没有则返回 null (全部按外购件处理, 由人在 import.json 中改)。
        /// </summary>
        public static string ProjectPrefix(IEnumerable<string> names)
        {
            var c = names.Select(Lead).Where(t => t != null && t.Length >= 8).GroupBy(t => t.Substring(0, 8))
                         .Select(g => new { g.Key, N = g.Count() }).Where(g => g.N >= 3).OrderByDescending(g => g.N).FirstOrDefault();
            return c == null ? null : c.Key;
        }

        /// <summary>
        /// 拆分零件名: 以图号前缀开头的为自制件 (代号 = 图号, 名称 = 其后文字); 其余为外购件:
        /// "名称 规格… 来源" 三段以上 → 名称 / 规格 / 品牌(来源); 两段 → 名称 / 规格; 一段 → 名称。
        /// </summary>
        public static void Classify(string name, string prefix, out string kind, out string code, out string shortName, out string spec, out string brand)
        {
            spec = ""; brand = ""; code = "";
            string lead = Lead(name);
            if (prefix != null && lead != null && lead.StartsWith(prefix, StringComparison.Ordinal))
            {
                kind = "自制件"; code = lead;
                shortName = name.Substring(lead.Length).Trim(' ', '_', '-');
                if (shortName == "") shortName = lead;
                return;
            }
            kind = "外购件";
            var t = Regex.Split(name.Trim(), @"\s+").Where(x => x != "").ToList();
            if (t.Count >= 3) { shortName = t[0]; brand = t[t.Count - 1]; spec = string.Join(" ", t.Skip(1).Take(t.Count - 2)); }
            else if (t.Count == 2) { shortName = t[0]; spec = t[1]; }
            else shortName = name.Trim();
        }

        /// <summary>按名称推断材料 (Mat 字段名); 推断不出返回 UNKNOWN</summary>
        public static string Material(string name)
        {
            string n = name ?? "";
            if (Regex.IsMatch(n, @"316")) return "S316";
            if (Regex.IsMatch(n, @"304|不锈钢")) return "S304";
            if (Regex.IsMatch(n, @"铝")) return "AL";
            if (Regex.IsMatch(n, @"PVC", RegexOptions.IgnoreCase)) return "PVC";
            if (Regex.IsMatch(n, @"Q235|碳钢")) return "Q235";
            return "UNKNOWN";
        }
    }

    // ======================================================================== 直管识别 (纯逻辑)
    public sealed class FaceInfo
    {
        /// <summary>'C' 圆柱, 'P' 平面, 'O' 其它</summary>
        public char Kind;
        /// <summary>圆柱: 轴线上一点; 平面: 面上一点 (mm)</summary>
        public double[] P;
        /// <summary>圆柱: 轴向; 平面: 法向 (单位向量)</summary>
        public double[] D;
        public double R;
    }

    public static class TubeFit
    {
        /// <summary>
        /// 单实体只由同轴圆柱面 (1~2 种半径) 与两个垂直于轴的端平面组成, 且轴线平行于零件坐标轴 → 直管 (或圆棒, din=0);
        /// 按识别结果算出的体积须与原几何相差 ≤ 0.2%, 否则不认 (防止把带倒角、开槽的件误认成管)。
        /// </summary>
        public static ImportGeometry Recognize(IList<FaceInfo> faces, double volumeMm3)
        {
            if (faces == null || faces.Count == 0 || faces.Any(f => f.Kind == 'O')) return null;
            var cyl = faces.Where(f => f.Kind == 'C').ToList();
            var pl = faces.Where(f => f.Kind == 'P').ToList();
            if (cyl.Count == 0 || pl.Count < 2) return null;
            double[] ax = cyl[0].D;
            int a = -1;
            for (int i = 0; i < 3; i++) if (Math.Abs(Math.Abs(ax[i]) - 1) < 1e-6) a = i;
            if (a < 0) return null;
            int b = (a + 1) % 3, c = (a + 2) % 3;
            double pb = cyl[0].P[b], pc = cyl[0].P[c];
            foreach (var f in cyl)
                if (Math.Abs(Math.Abs(f.D[a]) - 1) > 1e-6 || Math.Abs(f.P[b] - pb) > 1e-3 || Math.Abs(f.P[c] - pc) > 1e-3) return null;
            var radii = cyl.Select(f => Math.Round(f.R, 4)).Distinct().OrderBy(r => r).ToList();
            if (radii.Count > 2) return null;
            foreach (var f in pl) if (Math.Abs(Math.Abs(f.D[a]) - 1) > 1e-6) return null;
            var levels = pl.Select(f => Math.Round(f.P[a], 4)).Distinct().OrderBy(v => v).ToList();
            if (levels.Count != 2) return null;
            double dout = 2 * radii[radii.Count - 1], din = radii.Count == 2 ? 2 * radii[0] : 0, len = levels[1] - levels[0];
            double v = Math.PI / 4 * (dout * dout - din * din) * len;
            if (volumeMm3 <= 0 || Math.Abs(v - volumeMm3) / volumeMm3 > 0.002) return null;
            // Sk 草图坐标: 轴 Z → XY 面 (u,v)=(X,Y); 轴 Y → XZ 面 (X,Z); 轴 X → YZ 面 (Z,Y)
            double[] p = new double[3]; p[b] = pb; p[c] = pc;
            double[] uv = a == 2 ? new[] { p[0], p[1] } : a == 1 ? new[] { p[0], p[2] } : new[] { p[2], p[1] };
            return new ImportGeometry
            {
                Type = "tube", Axis = "XYZ".Substring(a, 1), Center = uv.Select(x => Math.Round(x, 6)).ToArray(),
                Start = levels[0], Length = Math.Round(len, 6), Dout = Math.Round(dout, 6), Din = Math.Round(din, 6),
            };
        }
    }

    // ======================================================================== STEP 导入 (调用 SolidWorks)
    /// <summary>
    /// cad import: 在专用实例中导入 STEP, 拆出装配层级、实例位置与零件, 每个零件导出 Parasolid x_t (精确 B-rep),
    /// 写 input/&lt;任务&gt;/import.json。不保存任何 SolidWorks 文档; 结束时关闭专用实例中的全部文档。
    /// </summary>
    public static class StepImport
    {
        /// <summary>导入 STEP; 各零件原几何写入 geoDir (调用方用临时目录, 全部成功后再替换正式的 geometry/)</summary>
        public static ImportModel FromStep(string step, string geoDir)
        {
            step = Path.GetFullPath(step);
            docOf.Clear();
            Sw.Head("导入 STEP: " + Path.GetFileName(step));
            Directory.CreateDirectory(geoDir);
            var fi = new FileInfo(step);
            var model = new ImportModel
            {
                Note = "由 cad import 生成; 可编辑 (位置、替换零件、类别/名称/材料、直管参数), 改后 cad run 校核。world 为原 STEP 实例位置, 用于核对",
                Source = new ImportSource { File = step, Bytes = fi.Length, Modified = fi.LastWriteTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                                            ImportedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"), SolidWorks = Sw.Version },
            };
            var t0 = DateTime.Now;
            ModelDoc2 doc = Sw.WithImportPrefs(() =>
            {
                object data = Sw.App.GetImportFileData(step);
                int err = 0;
                var d = (ModelDoc2)Sw.App.LoadFile4(step, "r", data, ref err);
                if (d == null) throw new Exception("STEP 导入失败 err=" + err);
                return d;
            });
            Sw.Log(string.Format("SolidWorks 导入完成 ({0:F0} s)", (DateTime.Now - t0).TotalSeconds));
            try
            {
                var parts = new Dictionary<string, ImportPart>();
                var asms = new Dictionary<string, ImportAsm>();
                int skipped = 0;
                var root = new ImportAsm { Id = "A01", Name = ImportNames.Clean(doc.GetTitle()) };
                asms[doc.GetTitle()] = root;
                model.Root = root.Id;
                Action<IEnumerable<Component2>, ImportAsm, Xf, bool> walk = null;
                walk = (comps, owner, parentWorld, define) =>
                {
                    foreach (var c in comps)
                    {
                        var md = c.GetModelDoc2() as ModelDoc2;
                        var kids = ((object[])c.GetChildren() ?? new object[0]).Cast<Component2>().ToList();
                        // 实测 (微波实验炉): 一个子装配实例 GetModelDoc2() 为 null, 但其下零件都在 → 有子件就按装配处理, 按名称归到同一定义
                        if (c.IsSuppressed() || (md == null && kids.Count == 0))
                        {
                            skipped++;
                            Sw.Log(string.Format("!! 跳过零部件 {0} (压缩 {1}, 文档 {2}, 子件 {3})", ImportNames.Decode(c.Name2), c.IsSuppressed(), md == null ? "无" : md.GetTitle(), kids.Count));
                            continue;
                        }
                        var w = Xf.FromArray((double[])((MathTransform)c.GetTotalTransform(false)).ArrayData);
                        var rel = Xf.Relative(w, parentWorld);
                        string key = md != null ? md.GetTitle() : null;
                        if (md is PartDoc)
                        {
                            ImportPart p;
                            if (!parts.TryGetValue(key, out p)) { p = NewPart(md, "~" + (parts.Count + 1)); parts[key] = p; }
                            if (define) owner.Children.Add(new ImportChild { Ref = p.Id, Xf = rel.ToRow() });
                            if (!model.World.ContainsKey(p.Id)) model.World[p.Id] = new List<double[]>();
                            model.World[p.Id].Add(w.ToRow());
                        }
                        else
                        {
                            if (md == null) Sw.Log("   零部件 " + ImportNames.Decode(c.Name2) + " 无文档, 有 " + kids.Count + " 个子件, 按子装配处理");
                            // 子装配按零部件名 (实例路径最后一段去掉 -n, 即 STEP 产品名) 命名并归并定义: 文档名可能是 SolidWorks 自起的
                            // ("装配体41"), 而且同一子装配偶有实例没有文档 (实测, 出现在哪个实例不固定)
                            string seg = c.Name2.Substring(c.Name2.LastIndexOf('/') + 1);
                            string name = ImportNames.Clean(Regex.Replace(seg, @"-\d+$", ""));
                            ImportAsm a = (key != null && asms.ContainsKey(key)) ? asms[key] : asms.ContainsKey("comp:" + name) ? asms["comp:" + name] : null;
                            bool first = a == null;
                            if (first) a = new ImportAsm { Id = "A" + (asms.Values.Distinct().Count() + 1).ToString("00"), Name = name };
                            if (key != null) asms[key] = a;
                            asms["comp:" + name] = a;
                            if (define) owner.Children.Add(new ImportChild { Ref = a.Id, Xf = rel.ToRow() });
                            // 子装配的定义只取第一次出现; 之后的实例只记录其中零件的世界位置
                            walk(kids, a, w, first);
                        }
                    }
                };
                if (doc is AssemblyDoc ad)
                {
                    walk(((object[])ad.GetComponents(true) ?? new object[0]).Cast<Component2>(), root, Xf.T(0, 0, 0), true);
                    // 完整性: 写入的零件实例数须等于 SolidWorks 中全部 (未压缩) 零件实例数
                    int inSw = ((object[])ad.GetComponents(false) ?? new object[0]).Cast<Component2>().Count(c => !c.IsSuppressed() && c.GetModelDoc2() is PartDoc);
                    int got = model.World.Values.Sum(l => l.Count);
                    if (got != inSw) model.Warnings.Add(string.Format("零件实例数不符: 写入 {0}, SolidWorks 中 {1}", got, inSw));
                }
                else
                {
                    var p = NewPart(doc, "~1"); parts[doc.GetTitle()] = p;
                    root.Children.Add(new ImportChild { Ref = p.Id, Xf = Xf.T(0, 0, 0).ToRow() });
                    model.World[p.Id] = new List<double[]> { Xf.T(0, 0, 0).ToRow() };
                }
                // 编号按名称排序, 与 SolidWorks 的遍历顺序无关 (实测两次导入遍历顺序不同, 编号随之改变)
                Renumber(model, parts, asms, root);
                foreach (var kv in parts) Export(docOf[kv.Key], kv.Value, geoDir);
                // 名称、类别、材料
                string prefix = ImportNames.ProjectPrefix(model.Parts.Select(p => p.OriginalName).Concat(model.Assemblies.Select(a => a.Name)));
                int wg = 0;
                foreach (var p in model.Parts)
                {
                    string kind, code, nm, spec, brand;
                    ImportNames.Classify(p.OriginalName, prefix, out kind, out code, out nm, out spec, out brand);
                    p.Kind = kind; p.Name = nm; p.Spec = spec; p.Brand = brand;
                    p.Code = kind == "自制件" ? code : "WG-" + (++wg).ToString("000");
                    p.Material = ImportNames.Material(p.OriginalName);
                    p.Remark = "质量按体积×密度估算" + (p.Material == "UNKNOWN" ? ", 材料未定" : ", 材料按名称推断");
                }
                // 原模型自带的干涉: 对导入的原装配做与运行校核相同的干涉检查, 登记为已知 (运行时只有新增的判不通过)
                if (doc is AssemblyDoc whole)
                {
                    var t1 = DateTime.Now;
                    var byTitle = parts.ToDictionary(kv => kv.Key, kv => kv.Value.Id);
                    int found = 0, unknown = 0;
                    foreach (var d in Checks.Detect(whole, false))
                    {
                        found++;
                        string pa, pb;
                        if (d.Rel == null || d.TitleA == null || d.TitleB == null || !byTitle.TryGetValue(d.TitleA, out pa) || !byTitle.TryGetValue(d.TitleB, out pb))
                        { unknown++; continue; }
                        if (model.Accepts(pa, pb, d.Rel, d.VolumeMm3)) continue;          // 同样的一处 (如三个相同安装组件里的) 只记一条
                        model.AcceptedInterferences.Add(new KnownInterference
                        {
                            PartA = pa, PartB = pb, Xf = d.Rel.ToRow(), VolumeMm3 = Math.Round(d.VolumeMm3, 3),
                            Note = model.Part(pa).Name + " ↔ " + model.Part(pb).Name,
                        });
                    }
                    Sw.Log(string.Format("原模型干涉 {0} 处, 登记 {1} 条 (相同的合并) ({2:F0} s)", found, model.AcceptedInterferences.Count, (DateTime.Now - t1).TotalSeconds));
                    if (unknown > 0) model.Warnings.Add("原模型干涉中有 " + unknown + " 处无法对应到零件, 未登记 (运行时会按新增报出)");
                }
                // 特征识别不在大装配里做: 先关掉导入的装配, 再逐个载入导出的 x_t (运行时删实体、比对用的也是这份几何)。
                // 实测 (2026-10-14): 载着 102 MB 装配 (专用实例 6.6 GB) 核对冲孔板时零件实体的 COM 对象失效 (RPC_E_DISCONNECTED)
                docOf.Clear();
                Sw.App.CloseAllDocuments(true);
                ApplyRebuild(model.Parts, geoDir);
                Sw.Log(string.Format("零件 {0} 种 / {1} 件, 装配 {2} 种, 跳过 (压缩/未还原) {3}; 自制件图号前缀 {4}; 直管 {5} 种",
                    model.Parts.Count, model.World.Values.Sum(l => l.Count), model.Assemblies.Count, skipped, prefix ?? "(无)",
                    model.Parts.Count(p => p.Model.Type == "tube")));
                return model;
            }
            finally
            {
                docOf.Clear();
                Sw.App.CloseAllDocuments(true);       // 专用实例中的导入文档全部不保存关闭
            }
        }

        /// <summary>导入过程中的零件文档 (按文档名); 每次导入开始与结束时清空</summary>
        static readonly Dictionary<string, ModelDoc2> docOf = new Dictionary<string, ModelDoc2>();

        /// <summary>按名称 (再按体积、实体数) 排序编号: 零件 P001…, 装配 A01 = 根, 其余 A02…; 同步改写所有引用</summary>
        static void Renumber(ImportModel model, Dictionary<string, ImportPart> parts, Dictionary<string, ImportAsm> asms, ImportAsm root)
        {
            var pmap = new Dictionary<string, string>();
            int n = 0;
            foreach (var p in parts.Values.OrderBy(p => p.OriginalName, StringComparer.Ordinal).ThenBy(p => p.VolumeMm3).ThenBy(p => p.Bodies))
                pmap[p.Id] = "P" + (++n).ToString("000");
            var all = asms.Values.Distinct().ToList();
            var amap = new Dictionary<string, string> { { root.Id, "A01" } };
            n = 1;
            foreach (var a in all.Where(a => a != root).OrderBy(a => a.Name, StringComparer.Ordinal)) amap[a.Id] = "A" + (++n).ToString("00");
            foreach (var a in all) foreach (var c in a.Children) c.Ref = pmap.ContainsKey(c.Ref) ? pmap[c.Ref] : amap[c.Ref];
            model.World = model.World.ToDictionary(kv => pmap[kv.Key], kv => kv.Value);
            foreach (var p in parts.Values) p.Id = pmap[p.Id];
            foreach (var a in all) a.Id = amap[a.Id];
            model.Root = "A01";
            model.Parts = parts.Values.OrderBy(p => p.Id).ToList();
            model.Assemblies = all.OrderBy(a => a.Id).ToList();
        }

        /// <summary>零件原几何导出 Parasolid x_t (SolidWorks 的内核格式, 精确 B-rep, 与版本无关的文本文件)</summary>
        static void Export(ModelDoc2 md, ImportPart p, string geoDir)
        {
            string file = Path.Combine(geoDir, p.Id + ".x_t");
            int e = 0, w = 0;
            bool ok = md.Extension.SaveAs(file, (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)(swSaveAsOptions_e.swSaveAsOptions_Silent | swSaveAsOptions_e.swSaveAsOptions_Copy), null, ref e, ref w);
            if (!ok || !File.Exists(file)) throw new Exception("导出几何失败 err=" + e + ": " + p.OriginalName);
            p.Model.File = "geometry/" + p.Id + ".x_t";
        }

        /// <summary>
        /// 自制件逐实体识别, 识别出的改为特征重建 (model = bodies), 并逐件报告未重建实体的原因:
        /// 先试截面拉伸 (Prism), 不是再试钣金 (SheetMetal)。带斜口、横孔的截面拉伸与全部钣金, 先在临时零件中实际建一次,
        /// 与原实体差集为 0 才采用 (Prism.Verify)。外购件不识别、不重建 (外购件保留样本几何)。
        /// 在分类之后调用, 逐个载入 geoDir 中导出的零件几何 (x_t), 用完关闭; 专用实例内存超过 5 GB 时重启。
        /// </summary>
        static void ApplyRebuild(IEnumerable<ImportPart> parts, string geoDir)
        {
            int nParts = 0, nPrism = 0, nCut = 0, nSheet = 0, total = 0;
            foreach (var p in parts.OrderBy(x => x.Id))
            {
                if (p.Kind != "自制件" || p.Model.Type == "tube") continue;
                total += p.Bodies;
                Sw.Recycle(5);
                ModelDoc2 md = Sw.LoadGeometry(Path.Combine(geoDir, Path.GetFileName(p.Model.File)));
                try { RebuildPart(p, md, ref nParts, ref nPrism, ref nCut, ref nSheet); }
                finally { Sw.App.CloseDoc(md.GetTitle()); }
            }
            Sw.Log(string.Format("自制件特征重建: {0} 种零件, {1}/{2} 个实体 (截面拉伸 {3}, 其中带斜口 / 横孔 {4}; 钣金 {5})", nParts, nPrism + nSheet, total, nPrism, nCut, nSheet));
        }

        static void RebuildPart(ImportPart p, ModelDoc2 md, ref int nParts, ref int nPrism, ref int nCut, ref int nSheet)
        {
            {
                var prisms = new List<PrismDef>();
                var sheets = new List<SheetDef>();
                var why = new Dictionary<string, int>();
                Action<string> count = w => why[w] = why.TryGetValue(w, out int c) ? c + 1 : 1;
                foreach (Body2 b in (object[])((PartDoc)md).GetBodies2((int)swBodyType_e.swSolidBody, true) ?? new object[0])
                {
                    string w;
                    var pr = Prism.Recognize(b, out w);
                    if (pr != null && (pr.Cuts != null || pr.Holes != null))
                    {
                        string v = Prism.Verify(pr, b);
                        if (v != null)
                        {
                            Sw.Log(string.Format("      {0} 实体 (体积 {1:F0} mm³, 轴 {2}): {3}", p.Id, pr.VolumeMm3, pr.Axis, v));
                            pr = null; w = "带斜口 / 横孔, 重建后与原实体不符";
                        }
                        else nCut++;
                    }
                    if (pr != null) { prisms.Add(pr); continue; }
                    string ws;
                    var sh = SheetMetal.Recognize(b, out ws);
                    if (sh != null)
                    {
                        string v = Prism.Verify(m => SheetMetal.Build(m, sh), b);
                        if (v == null) { sheets.Add(sh); continue; }
                        Sw.Log(string.Format("      {0} 钣金实体 (体积 {1:F0} mm³, ⟂{2}): {3}", p.Id, sh.VolumeMm3, sh.Axis, v));
                        ws = "钣金, 重建后与原实体不符";
                    }
                    // 原因: 看起来像钣金 (有基板与法兰) 时报钣金的, 否则报截面拉伸的
                    count(ws != null && !ws.StartsWith("钣金: 找不到基板") && !ws.StartsWith("钣金: 基板没有") && !ws.StartsWith("钣金: 没有法兰") ? ws : w);
                }
                string reasons = string.Join("; ", why.OrderByDescending(x => x.Value).Select(x => x.Key + " ×" + x.Value));
                if (prisms.Count + sheets.Count > 0)
                {
                    p.Model = new ImportGeometry { Type = "bodies", File = p.Model.File, Prisms = prisms.Count > 0 ? prisms : null, Sheets = sheets.Count > 0 ? sheets : null };
                    nParts++; nPrism += prisms.Count; nSheet += sheets.Count;
                }
                Sw.Log(string.Format("   {0} {1}: 特征重建 {2}/{3} 个实体 (截面拉伸 {4}, 其中带斜口 / 横孔 {5}; 钣金 {6}){7}", p.Id, p.Name, prisms.Count + sheets.Count, p.Bodies,
                    prisms.Count, prisms.Count(x => x.Cuts != null || x.Holes != null), sheets.Count, reasons == "" ? "" : "; 未重建: " + reasons));
            }
        }

        static ImportPart NewPart(ModelDoc2 md, string tempId)
        {
            string key = md.GetTitle();
            var p = new ImportPart { Id = tempId, OriginalName = ImportNames.Clean(key) };
            p.Bodies = Sw.BodyCount(md);
            p.VolumeMm3 = Math.Round(Sw.Volume(md), 3);
            p.BoxMm = Sw.PartBox(md).Select(v => Math.Round(v, 4)).ToArray();
            docOf[key] = md;
            p.Model = (p.Bodies == 1 ? TubeFit.Recognize(Faces(md), p.VolumeMm3) : null) ?? new ImportGeometry { Type = "geometry" };
            Sw.Log(string.Format("{0,-50} 实体 {1,2} 体积 {2,12:F0} mm³ {3}", p.OriginalName, p.Bodies, p.VolumeMm3,
                p.Model.Type == "tube" ? string.Format("直管 Ø{0}/Ø{1}×{2}", p.Model.Dout, p.Model.Din, p.Model.Length) : ""));
            return p;
        }

        static List<FaceInfo> Faces(ModelDoc2 md)
        {
            var r = new List<FaceInfo>();
            var bodies = (object[])((PartDoc)md).GetBodies2((int)swBodyType_e.swSolidBody, true) ?? new object[0];
            foreach (Body2 b in bodies)
                foreach (Face2 f in (object[])b.GetFaces() ?? new object[0])
                {
                    var s = (Surface)f.GetSurface();
                    if (s.IsCylinder())
                    {
                        var q = (double[])s.CylinderParams;      // 轴上一点 xyz, 轴向 xyz, 半径 (m)
                        r.Add(new FaceInfo { Kind = 'C', P = new[] { q[0] / Sw.MM, q[1] / Sw.MM, q[2] / Sw.MM }, D = new[] { q[3], q[4], q[5] }, R = q[6] / Sw.MM });
                    }
                    else if (s.IsPlane())
                    {
                        var q = (double[])s.PlaneParams;         // 法向 xyz, 面上一点 xyz (m)
                        r.Add(new FaceInfo { Kind = 'P', D = new[] { q[0], q[1], q[2] }, P = new[] { q[3] / Sw.MM, q[4] / Sw.MM, q[5] / Sw.MM } });
                    }
                    else { r.Add(new FaceInfo { Kind = 'O' }); return r; }     // 有其它曲面即不是直管, 不必再看
                }
            return r;
        }
    }
}
