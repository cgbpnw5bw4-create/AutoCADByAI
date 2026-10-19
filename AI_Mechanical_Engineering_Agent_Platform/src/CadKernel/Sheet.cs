using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>按体积与包围盒在原几何中找回的被重建实体 (截面拉伸、钣金)</summary>
    public interface IBodyDef
    {
        double VolumeMm3 { get; }
        double[] Box { get; }
    }

    /// <summary>
    /// 钣金实体: 基体法兰 + 边线法兰 + 孔 (零件坐标, mm)。
    /// 基板垂直于坐标轴 Axis, 上表面在 Top (草图画在这里), 板材占 [Top − T, Top]; 草图坐标 (u,v) 同 Prism.UV。
    /// Loops: 外环为基体法兰草图 (外部虚拟交点轮廓, 即材料在内的法兰外表面所在处), 内环为基板上的孔与切口。
    /// </summary>
    public sealed class SheetDef : IBodyDef
    {
        public string Axis { get; set; }
        public double Top { get; set; }
        public double T { get; set; }
        /// <summary>折弯内半径</summary>
        public double R { get; set; }
        /// <summary>相邻法兰在角部的间隙</summary>
        public double Gap { get; set; }
        public List<Loop2D> Loops { get; set; } = new List<Loop2D>();
        public List<FlangeDef> Flanges { get; set; } = new List<FlangeDef>();
        /// <summary>法兰上的孔与缺口 (只切本实体)</summary>
        public List<ProfileCut> Cutouts { get; set; }
        public double VolumeMm3 { get; set; }
        public double[] Box { get; set; }
    }

    /// <summary>轮廓切除: 草图 (垂直于坐标轴 Axis, 坐标同 Prism.UV) 中的一个封闭环, 沿该轴从 From 切到 To (零件坐标, mm)</summary>
    public sealed class ProfileCut
    {
        public string Axis { get; set; }
        public double From { get; set; }
        public double To { get; set; }
        public Loop2D Loop { get; set; }
    }

    /// <summary>
    /// 边线法兰: 外环上的一段 Edge (u1,v1,u2,v2), 朝 Dir (+1 = +Axis, -1 = −Axis) 折 90°, 高 Length (从基板另一侧表面量起, 外部虚拟交点), 材料在内。
    /// Return: 法兰末端再向内折 90° 的回折边宽度 (从法兰外表面量起, 外部虚拟交点); 没有为 null。
    /// </summary>
    public sealed class FlangeDef
    {
        public double[] Edge { get; set; }
        public int Dir { get; set; }
        public double Length { get; set; }
        public double? Return { get; set; }
    }

    /// <summary>
    /// SolidWorks 钣金: 识别与重建。参数一律 mm; 特征后读回体积核对 (与 Sk 相同的防线)。
    /// 实测标定 (2026-10-14, 见 api_calibration): 基体法兰总是朝草图面法向的反方向加厚 (ReverseThickness 无效);
    /// 边线法兰须选法兰那一侧表面上的边、InsertSketchForEdgeFlange 的反向取 true, 长度才是从基板另一侧量起的总高;
    /// 边与轮廓草图须以强类型数组 (Edge[] / Sketch[]) 传给 AddEdges, 否则 CreateFeature 返回 null。
    /// </summary>
    public static class SheetMetal
    {

        // ------------------------------------------------------------------ 识别
        sealed class PF { public Face2 F; public int Ax; public double Level, Area; }
        sealed class CF { public Face2 F; public int Ax; public double[] Pt; public double R; }

        /// <summary>
        /// 识别钣金实体 (第一版): 矩形基板 (垂直于坐标轴) + 若干条整边、90°、材料在内的边线法兰 + 基板上的孔与切口 + 法兰上的孔。
        /// 板厚取面积最大的一对反向平行平面之距; 折弯内半径取成对的圆柱 (r 与 r + 板厚); 角部间隙由法兰端面到相邻法兰内表面之距 × √2 反算。
        /// 折弯交汇处的小样条面 (SolidWorks 钣金自动生成的过渡面) 允许存在。不是这一类时返回 null; 结果须经实际重建核对 (Prism.Verify)。
        /// </summary>
        public static SheetDef Recognize(Body2 body, out string why, List<string> trace = null)
        {
            why = null;
            var planes = new List<PF>(); var cyls = new List<CF>(); var other = new List<string>();
            var miters = new List<double[]>();                                     // 斜平面的法向 (回折边角部的斜接面), 定出基板方向后再核对
            foreach (Face2 f in (object[])body.GetFaces() ?? new object[0])
            {
                var s = (Surface)f.GetSurface();
                double area = f.GetArea() / (Sw.MM * Sw.MM);
                if (s.IsPlane())
                {
                    var q = (double[])s.PlaneParams;
                    int ax = Prism.AlongAxis(new[] { q[0], q[1], q[2] });
                    if (ax < 0 && area < 100 && Prism.ZeroAxis(new[] { q[0], q[1], q[2] }) >= 0) { miters.Add(new[] { q[0], q[1], q[2] }); continue; }
                    if (ax < 0) { other.Add("斜平面"); continue; }
                    planes.Add(new PF { F = f, Ax = ax, Level = q[3 + ax] / Sw.MM, Area = area });
                }
                else if (s.IsCylinder())
                {
                    var q = (double[])s.CylinderParams;
                    int ax = Prism.AlongAxis(new[] { q[3], q[4], q[5] });
                    if (ax < 0) { other.Add("斜圆柱面"); continue; }
                    cyls.Add(new CF { F = f, Ax = ax, Pt = Prism.Mm(new[] { q[0], q[1], q[2] }), R = q[6] / Sw.MM });
                }
                else if (s.IsParametric() && area < 100) { }             // 折弯交汇处的过渡面
                else other.Add(s.IsTorus() ? "圆环面" : s.IsCone() ? "圆锥面" : s.IsParametric() ? "大样条面" : "其它曲面");
            }
            if (other.Count > 0) { why = "钣金: 含" + string.Join("、", other.Distinct()); return null; }

            // 基板: 平面总面积最大的方向; 该方向最大的面与其对面 (等面积、相距 < 10 mm) 之距为板厚, 上表面为两者中较高者
            int a = Enumerable.Range(0, 3).OrderByDescending(k => planes.Where(p => p.Ax == k).Sum(p => p.Area)).First();
            var big = planes.Where(p => p.Ax == a).OrderByDescending(p => p.Area).ToList();
            if (big.Count < 2) { why = "钣金: 找不到基板"; return null; }
            var f0 = big[0];
            var f1 = big.Skip(1).Where(p => Math.Abs(p.Level - f0.Level) > 1e-4 && Math.Abs(p.Level - f0.Level) < 10).OrderByDescending(p => p.Area).FirstOrDefault();
            if (f1 == null || f1.Area < 0.5 * f0.Area) { why = "钣金: 基板没有等面积的对面"; return null; }
            double t = Math.Round(Math.Abs(f0.Level - f1.Level), 4), top = Math.Max(f0.Level, f1.Level);
            if (miters.Any(n => Math.Abs(n[a]) > 1e-7)) { why = "钣金: 含不垂直于基板的斜平面"; return null; }
            var vp = Prism.VertexPoints(body);
            double[] lo = Enumerable.Range(0, 3).Select(i => vp.Min(q => q[i])).ToArray(), hi = Enumerable.Range(0, 3).Select(i => vp.Max(q => q[i])).ToArray();
            int ku = a == 0 ? 2 : 0, kv = a == 1 ? 2 : 1;                       // 草图 (u,v) 对应的坐标轴, 同 Prism.UV
            double u0 = lo[ku], u1 = hi[ku], v0 = lo[kv], v1 = hi[kv];
            var d = new SheetDef { Axis = "XYZ".Substring(a, 1), Top = Math.Round(top, 6), T = t };
            d.Loops.Add(new Loop2D { Outer = true, Segs = { Line(u0, v0, u1, v0), Line(u1, v0, u1, v1), Line(u1, v1, u0, v1), Line(u0, v1, u0, v0) } });

            // 折弯内半径: 不平行于基板法向、半径 r 与 r + 板厚 成对的圆柱中最小的 r
            var radii = cyls.Where(c => c.Ax != a).Select(c => Math.Round(c.R, 4)).Distinct().ToList();
            var rin = radii.Where(r => radii.Any(r2 => Math.Abs(r2 - r - t) < 1e-3)).OrderBy(r => r).ToList();
            if (rin.Count == 0) { why = "钣金: 找不到成对的折弯圆柱 (内半径 r、外半径 r + 板厚)"; return null; }
            d.R = rin[0];

            // 四边: 法兰外表面 = 法向沿该边法向轴、恰在外轮廓处的平面; 伸出基板的一侧定方向。
            // 高度 (外部虚拟交点) 取该边一带 (离外轮廓 ≤ 板厚 + 半径 + 0.5) 顶点沿基板法向的极值 —— 有回折边时量到回折边外表面
            var sides = new[] { Tuple.Create(ku, u0, new[] { u0, v0, u0, v1 }), Tuple.Create(ku, u1, new[] { u1, v0, u1, v1 }),
                                Tuple.Create(kv, v0, new[] { u0, v0, u1, v0 }), Tuple.Create(kv, v1, new[] { u0, v1, u1, v1 }) };
            var flanged = new List<Tuple<int, double, int>>();                // 有法兰的边: 法向轴, 位置, 方向
            foreach (var sd in sides)
            {
                int k = sd.Item1; double pos = sd.Item2, inward = pos == lo[k] ? 1 : -1;
                var outer = planes.Where(p => p.Ax == k && Math.Abs(p.Level - pos) < 1e-3).ToList();
                if (outer.Count == 0) continue;
                var ext = outer.SelectMany(p => Prism.FaceVertexPoints(p.F)).Select(q => q[a]).ToList();
                double mn = ext.Min(), mx = ext.Max();
                int dir = mn < top - t - 1e-3 ? -1 : mx > top + 1e-3 ? 1 : 0;
                if (dir == 0) continue;                                       // 只是基板的侧边
                if (mn < top - t - 1e-3 && mx > top + 1e-3) { why = "钣金: 一条边上的法兰同时伸向两侧"; return null; }
                var strip = vp.Where(q => Math.Abs(q[k] - pos) <= t + d.R + 0.5).Select(q => q[a]).ToList();
                double end = dir < 0 ? strip.Min() : strip.Max();          // 法兰 (连同回折边) 的外端
                var fd = new FlangeDef { Edge = Prism.R6(sd.Item3), Dir = dir, Length = Math.Round(dir < 0 ? top - end : end - (top - t), 6) };
                // 回折边: 法兰外端向内的平板 —— 其末端面 (法向沿该边法向轴) 在外轮廓以内超过 板厚 + 半径 处, 且紧贴外端 (板厚范围内)
                var lip = planes.Where(p => p.Ax == k && (p.Level - pos) * inward > t + d.R + 1e-3).Where(p =>
                {
                    var z = Prism.FaceVertexPoints(p.F).Select(q => q[a]).ToList();
                    return dir < 0 ? z.Min() > end - 1e-3 && z.Max() < end + t + 1e-3 : z.Max() < end + 1e-3 && z.Min() > end - t - 1e-3;
                }).Select(p => Math.Abs(p.Level - pos)).OrderBy(x => x).ToList();
                if (lip.Count > 0) fd.Return = Math.Round(lip[0], 6);
                d.Flanges.Add(fd);
                flanged.Add(Tuple.Create(k, pos, dir));
            }
            if (d.Flanges.Count == 0) { why = "钣金: 没有法兰"; return null; }

            // 法兰上的孔与缺口: 取每条法兰外表面的环 —— 内环是孔 (任意形状), 外环偏离矩形的部分是从边缘切进去的缺口。
            // 都作为轮廓切除穿过法兰板厚。(旧做法按圆柱面猜孔, 会把缺口的倒圆当成孔: 实测电控箱密封门 2026-10-16)
            d.Cutouts = new List<ProfileCut>();
            foreach (var fl in flanged)
            {
                int k = fl.Item1; double pos = fl.Item2, inward = pos == lo[k] ? 1 : -1;
                double from = Math.Round(Math.Min(pos, pos + inward * t), 6), to = Math.Round(Math.Max(pos, pos + inward * t), 6);
                foreach (var face in planes.Where(p => p.Ax == k && Math.Abs(p.Level - pos) < 1e-3))
                {
                    string w;
                    var loops = Prism.CapLoops(new[] { face.F }, k, out w);
                    if (loops == null) { why = "钣金: 法兰面 " + w; return null; }
                    foreach (var l in loops.Where(l => !l.Outer).Concat(loops.Where(l => l.Outer).SelectMany(Notches)))
                        d.Cutouts.Add(new ProfileCut { Axis = "XYZ".Substring(k, 1), From = from, To = to, Loop = new Loop2D { Outer = true, Segs = l.Segs } });
                }
            }
            if (d.Cutouts.Count == 0) d.Cutouts = null;

            // 角部间隙: 相邻两边都有同向法兰时, 一条法兰的端面到另一条法兰内表面之距 = 间隙 / √2
            Func<double> gap = () =>
            {
                foreach (var A in flanged)
                    foreach (var B in flanged.Where(x => x.Item1 != A.Item1 && x.Item3 == A.Item3))
                    {
                        double inward = B.Item2 == lo[B.Item1] ? 1 : -1, innerB = B.Item2 + inward * t;
                        var end = planes.Where(p => p.Ax == B.Item1 && (p.Level - innerB) * inward > 1e-4 && (p.Level - innerB) * inward < 1)
                                        .Select(p => Math.Abs(p.Level - innerB)).OrderBy(x => x).ToList();
                        if (end.Count > 0) return Math.Round(end[0] * Math.Sqrt(2), 3);
                    }
                return 0.2;                                                   // 没有相邻法兰时不起作用
            };
            d.Gap = gap();
            // 基板上的孔与切口: 上表面的内环
            var topFaces = planes.Where(p => p.Ax == a && Math.Abs(p.Level - top) < 1e-4).Select(p => p.F).ToList();
            var capLoops = Prism.CapLoops(topFaces, a, out why);
            if (capLoops == null) { why = "钣金: 基板 " + why; return null; }
            d.Loops.AddRange(capLoops.Where(l => !l.Outer));
            d.VolumeMm3 = Math.Round(Prism.Vol(body), 3);
            d.Box = Prism.Box(body).Select(v => Math.Round(v, 4)).ToArray();
            if (trace != null)
            {
                trace.Add(string.Format("基板 ⟂{0} 上表面 {1} 板厚 {2} 折弯 R{3} 间隙 {4}; 外形 {5:F2}..{6:F2} × {7:F2}..{8:F2}; 孔与切口 {9}", d.Axis, d.Top, d.T, d.R, d.Gap,
                    u0, u1, v0, v1, d.Loops.Count - 1));
                foreach (var fl in d.Flanges) trace.Add(string.Format("法兰 {0} 朝 {1} 高 {2}{3}", string.Join(",", fl.Edge), fl.Dir > 0 ? "+" : "-", fl.Length, fl.Return == null ? "" : " 回折 " + fl.Return));
                foreach (var c in d.Cutouts ?? new List<ProfileCut>())
                    trace.Add(string.Format("法兰切除 ⟂{0} {1} → {2}: [{3}]", c.Axis, c.From, c.To, string.Join("", c.Loop.Segs.Select(g => g.T))));
            }
            return d;
        }

        static Seg2 Line(double u1, double v1, double u2, double v2) { return new Seg2 { T = "L", P = Prism.R6(u1, v1, u2, v2) }; }

        /// <summary>import.json 手工编辑后的格式校验</summary>
        public static string Check(SheetDef d)
        {
            if (d.Axis != "X" && d.Axis != "Y" && d.Axis != "Z") return "钣金轴应为 X / Y / Z";
            if (!(d.T > 0) || d.R < 0 || d.Gap < 0) return "钣金应有 t > 0、r ≥ 0、gap ≥ 0";
            if (d.Loops == null || d.Loops.Count(l => l.Outer) != 1) return "钣金基板应有且只有一个外环";
            if (d.Box == null || d.Box.Length != 6) return "缺少原实体包围盒 box (6 个数)";
            foreach (var f in d.Flanges ?? new List<FlangeDef>())
                if (f.Edge == null || f.Edge.Length != 4 || (f.Dir != 1 && f.Dir != -1) || !(f.Length > d.T) || f.Return != null && !(f.Return > d.T))
                    return "法兰应有 edge (4 个数)、dir ±1、length > 板厚, 回折边宽 return > 板厚";
            foreach (var c in d.Cutouts ?? new List<ProfileCut>())
                if ("XYZ".IndexOf(c.Axis ?? "?") < 0 || !(c.To > c.From) || c.Loop?.Segs == null || c.Loop.Segs.Count == 0) return "法兰切除应有 axis、to > from 与非空的 loop";
            return null;
        }

        /// <summary>
        /// 法兰面外环中的缺口: 外环的包围矩形上的直线段之外, 连续的一串段即一个从边缘切进去的缺口, 用矩形边上的一段直线封口。
        /// 外环须已按走向首尾相接 (Prism.Chain)。没有缺口返回空表。
        /// </summary>
        public static List<Loop2D> Notches(Loop2D outer)
        {
            const double tol = 1e-3;
            var segs = outer.Segs;
            var lines = segs.Where(g => g.T == "L").ToList();
            if (lines.Count == 0) return new List<Loop2D>();
            double u0 = lines.Min(g => Math.Min(g.P[0], g.P[2])), u1 = lines.Max(g => Math.Max(g.P[0], g.P[2]));
            double v0 = lines.Min(g => Math.Min(g.P[1], g.P[3])), v1 = lines.Max(g => Math.Max(g.P[1], g.P[3]));
            Func<double, double, bool> onBox = (u, v) => Math.Abs(u - u0) < tol || Math.Abs(u - u1) < tol || Math.Abs(v - v0) < tol || Math.Abs(v - v1) < tol;
            Func<Seg2, bool> boundary = g => g.T == "L" && (
                Math.Abs(g.P[0] - u0) < tol && Math.Abs(g.P[2] - u0) < tol || Math.Abs(g.P[0] - u1) < tol && Math.Abs(g.P[2] - u1) < tol ||
                Math.Abs(g.P[1] - v0) < tol && Math.Abs(g.P[3] - v0) < tol || Math.Abs(g.P[1] - v1) < tol && Math.Abs(g.P[3] - v1) < tol);
            int n = segs.Count, s0 = segs.FindIndex(g => boundary(g));
            var r = new List<Loop2D>();
            if (s0 < 0) return r;
            List<Seg2> run = null;
            for (int i = 1; i <= n; i++)
            {
                var g = segs[(s0 + i) % n];
                if (!boundary(g)) { (run = run ?? new List<Seg2>()).Add(g); continue; }
                if (run == null) continue;
                double[] a = { run[0].P[0], run[0].P[1] }, b = { run[run.Count - 1].P[2], run[run.Count - 1].P[3] };
                if (onBox(a[0], a[1]) && onBox(b[0], b[1]))
                    r.Add(new Loop2D { Outer = true, Segs = run.Concat(new[] { new Seg2 { T = "L", P = new[] { b[0], b[1], a[0], a[1] } } }).ToList() });
                run = null;
            }
            return r;
        }

        // ------------------------------------------------------------------ 重建
        public static void Build(ModelDoc2 m, SheetDef d)
        {
            int a = "XYZ".IndexOf(d.Axis);
            // 基体法兰先只画外环, 法兰与回折边都建完后再把孔与切口补画进基体法兰的草图 (实测 2026-10-15, 封板 4 有 1856 个孔):
            // - 孔一开始就在基体法兰里: 之后每张法兰轮廓草图、每次压缩都连孔一起重建 (四条法兰 249 s, 回折边 1284 s);
            // - 孔用一个切除特征切出: 建模快, 但含此零件的工程图出视图 23 s、保存 40 min 以上不返回 (普通零件里也要 54 s);
            // - 孔在基体法兰草图里: 出视图 5 s、保存 1 s。
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var f = Draw(m, a, d.Top, d.Loops.Where(l => l.Outer)).BaseFlange(d.T, d.R, false);
            var baseFlange = f;
            Sw.Log(string.Format("    钣金 ⟂{0} 上表面 {1}: 基体法兰 ({2} 个孔与切口) {3:F0} s", d.Axis, d.Top, d.Loops.Count - 1, clock.Elapsed.TotalSeconds));
            // 后续各步按坐标找边, 都要求零件处于折叠状态; 每步后核对本实体的平板型式 (总在特征树最后) 仍是压缩的
            var flatPattern = m.FeatureByPositionReverse(0) as Feature;
            if (flatPattern == null || flatPattern.GetTypeName2() != "FlatPattern") throw new Exception("钣金: 基体法兰后特征树最后不是平板型式");
            Action<string> folded = stage =>
            {
                if (((bool[])flatPattern.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]) return;
                Sw.Fold(m);
                Sw.Log("    钣金: " + stage + "后零件处于展平状态 (平板型式 " + flatPattern.Name + " 未压缩), 已压缩回折叠");
            };
            folded("基体法兰");
            foreach (var grp in d.Flanges.GroupBy(x => Tuple.Create(x.Dir, x.Length)))
            {
                Body2 b = BodyOf(f);
                double level = grp.Key.Item1 < 0 ? d.Top - d.T : d.Top;
                var onFace = LinearEdgesOn(b, a, level);
                var edges = new List<double[][]>();
                foreach (var fl in grp)
                {
                    // 先建的法兰会把基板边截短 (折弯占去角部): 取与法兰边共线、有重叠的最长直线边
                    var e = onFace.Where(x => OnSegment(x, a, fl.Edge)).OrderByDescending(LineLength).FirstOrDefault();
                    if (e == null) throw new Exception("钣金: 基板上找不到法兰边 " + string.Join(",", fl.Edge));
                    edges.Add(Ends(e));
                }
                clock.Restart();
                f = EdgeFlanges(m, f, edges, grp.Key.Item2, d.R, d.Gap, true);
                Sw.Log(string.Format("    钣金: 法兰 {0} 条 (高 {1}) {2:F0} s", edges.Count, grp.Key.Item2, clock.Elapsed.TotalSeconds));
                folded("法兰");
            }
            // 回折边: 在法兰外端、法兰内表面一侧的那条边上再折 90° 向内 (同样选法兰那一侧表面上的边、反向取 true)
            var outerLoop = d.Loops.First(l => l.Outer);
            double umin = outerLoop.Segs.Min(g => Math.Min(g.P[0], g.P[2])), vmin = outerLoop.Segs.Min(g => Math.Min(g.P[1], g.P[3]));
            int ku = a == 0 ? 2 : 0, kv = a == 1 ? 2 : 1;
            foreach (var grp in d.Flanges.Where(x => x.Return != null).GroupBy(x => x.Return.Value))
            {
                Body2 b = BodyOf(f);
                var edges = new List<double[][]>();
                foreach (var fl in grp)
                {
                    bool uConst = Math.Abs(fl.Edge[0] - fl.Edge[2]) < 1e-6;
                    int k = uConst ? ku : kv;
                    double pos = uConst ? fl.Edge[0] : fl.Edge[1], inward = Math.Abs(pos - (uConst ? umin : vmin)) < 1e-6 ? 1 : -1;
                    double kAt = pos + inward * d.T, aAt = fl.Dir < 0 ? d.Top - fl.Length : d.Top - d.T + fl.Length;
                    var e = LinesAt(b, k, kAt, a, aAt).OrderByDescending(LineLength).FirstOrDefault();
                    if (e == null) throw new Exception("钣金: 找不到回折边所在的法兰端边 " + string.Join(",", fl.Edge));
                    edges.Add(Ends(e));
                }
                clock.Restart();
                f = EdgeFlanges(m, f, edges, grp.Key, d.R, d.Gap, true);
                Sw.Log(string.Format("    钣金: 回折边 {0} 条 (宽 {1}) {2:F0} s", edges.Count, grp.Key, clock.Elapsed.TotalSeconds));
                folded("回折边");
            }
            var inner = d.Loops.Where(l => !l.Outer).ToList();
            if (inner.Count > 0)
            {
                clock.Restart();
                var sketch = baseFlange.GetFirstSubFeature() as Feature;
                if (sketch == null || sketch.GetTypeName2() != "ProfileFeature") throw new Exception("钣金: 取不到基体法兰的草图");
                double v0 = Sw.Volume(m);
                var s = Sk.Edit(m, sketch, a == 2 ? 0 : a == 1 ? 1 : 2);
                DrawLoops(s, inner);
                s.Close();
                m.ForceRebuild3(false);
                double v1 = Sw.Volume(m);
                if (v1 >= v0 - 1e-3) throw new Exception("钣金: 补画进基体法兰的孔与切口没有去除材料");
                if (baseFlange.GetErrorCode2(out bool warn) != 0) throw new Exception("钣金: 补画孔与切口后基体法兰出错");
                Sw.Log(string.Format("    钣金: 基板上的孔与切口 {0} 个 {1:F0} s", inner.Count, clock.Elapsed.TotalSeconds));
                folded("补画基板孔");
            }
            // 法兰上的孔与缺口: 同一法兰 (同轴、同一板厚范围) 的画在一张草图里, 一次切出, 只切本实体
            Body2 body = BodyOf(f);
            foreach (var grp in (d.Cutouts ?? new List<ProfileCut>()).GroupBy(c => Tuple.Create(c.Axis, c.From, c.To)))
                body = BodyOf(Draw(m, "XYZ".IndexOf(grp.Key.Item1), grp.Key.Item2, grp.Select(c => c.Loop)).Cut(grp.Key.Item3 - grp.Key.Item2, 1, body));
            folded("法兰上的孔与缺口");
        }

        /// <summary>在垂直于轴 a、位于 at 处的草图上画出这些环</summary>
        static Sk Draw(ModelDoc2 m, int a, double at, IEnumerable<Loop2D> loops)
        {
            Sk s = a == 2 ? Sk.XY(m, at) : a == 1 ? Sk.XZ(m, at) : Sk.YZ(m, at);
            DrawLoops(s, loops);
            return s;
        }

        static void DrawLoops(Sk s, IEnumerable<Loop2D> loops)
        {
            foreach (var l in loops)
                foreach (var g in l.Segs)
                {
                    if (g.T == "L") s.Line(g.P[0], g.P[1], g.P[2], g.P[3]);
                    else if (g.T == "A") s.ArcDir(g.C[0], g.C[1], g.P[0], g.P[1], g.P[2], g.P[3], g.Dir);
                    else s.Circ(g.C[0], g.C[1], 2 * g.R);
                }
        }

        static Body2 BodyOf(Feature f)
        {
            var faces = f.GetFaces() as object[];
            var b = faces == null || faces.Length == 0 ? null : ((Face2)faces[0]).GetBody() as Body2;
            if (b == null) throw new Exception("找不到特征 " + f.Name + " 所在的实体");
            return b;
        }

        /// <summary>直线边的两端点 (mm)</summary>
        static double[][] Ends(Edge e)
        {
            var cp = e.GetCurveParams3();
            return new[] { Mm((double[])cp.StartPoint), Mm((double[])cp.EndPoint) };
        }

        static double LineLength(Edge e)
        {
            var cp = e.GetCurveParams3();
            double[] s = (double[])cp.StartPoint, t = (double[])cp.EndPoint;
            return Math.Sqrt(Enumerable.Range(0, 3).Sum(i => (s[i] - t[i]) * (s[i] - t[i])));
        }

        /// <summary>边 (直线) 在草图坐标中是否与线段 seg (u1,v1,u2,v2) 共线且有重叠, 允差 1e-3 mm</summary>
        static bool OnSegment(Edge e, int axis, double[] seg)
        {
            var cp = e.GetCurveParams3();
            double[] s = Prism.UV(axis, Mm((double[])cp.StartPoint)), t = Prism.UV(axis, Mm((double[])cp.EndPoint));
            double dx = seg[2] - seg[0], dy = seg[3] - seg[1], len = Math.Sqrt(dx * dx + dy * dy);
            Func<double[], double> off = p => Math.Abs((p[0] - seg[0]) * dy - (p[1] - seg[1]) * dx) / len;     // 到直线的距离
            Func<double[], double> along = p => ((p[0] - seg[0]) * dx + (p[1] - seg[1]) * dy) / len;          // 沿线段的位置
            if (off(s) > 1e-3 || off(t) > 1e-3) return false;
            double a0 = Math.Min(along(s), along(t)), a1 = Math.Max(along(s), along(t));
            return a1 > 1e-3 && a0 < len - 1e-3;
        }

        /// <summary>
        /// 一个特征同时在多条边上做 90° 边线法兰: 长度 length 按外部虚拟交点量, 材料在内 (法兰外表面与原边外侧齐平),
        /// 折弯内半径 r, 相邻法兰在角部留 gap。reverse 即 InsertSketchForEdgeFlange 的同名参数。
        /// 每条边的轮廓草图: 先转换该边, 再沿草图 +Y 补成长 length 的矩形 (官方示例的做法)。
        /// </summary>
        public static Feature EdgeFlanges(ModelDoc2 m, Feature after, IList<double[][]> segs, double length, double r, double gap, bool reverse)
        {
            double K = Sw.MM, v0 = Sw.Volume(m);
            // 多实体零件中, 建轮廓草图、进入草图编辑后先前取到的 Edge 指针会失效 (RPC_E_DISCONNECTED, 实测 2026-10-14)
            // → 法兰边只记两端点, 每次用时从实体上按坐标重新找
            Func<double[][], Edge> find = s =>
            {
                foreach (Edge e in (object[])BodyOf(after).GetEdges() ?? new object[0])
                {
                    if (!((Curve)e.GetCurve()).IsLine()) continue;
                    var cp = e.GetCurveParams3();
                    double[] p = Mm((double[])cp.StartPoint), q = Mm((double[])cp.EndPoint);
                    Func<double[], double[], bool> same = (x, y) => Enumerable.Range(0, 3).All(i => Math.Abs(x[i] - y[i]) < 1e-4);
                    if (same(p, s[0]) && same(q, s[1]) || same(p, s[1]) && same(q, s[0])) return e;
                }
                throw new Exception("边线法兰: 找不到边 " + string.Join(",", s[0]) + " → " + string.Join(",", s[1]));
            };
            var sketchFeats = new List<Feature>();
            foreach (var s in segs)
            {
                var sf = m.InsertSketchForEdgeFlange(find(s), Math.PI / 2, reverse) as Feature;
                if (sf == null) throw new Exception("边线法兰轮廓草图建不出");
                sf.Select2(false, 0);
                m.EditSketch();
                var sk = (Sketch)m.GetActiveSketch2();
                ((Entity)find(s)).Select4(false, null);
                if (!m.SketchManager.SketchUseEdge3(false, false)) throw new Exception("边线法兰: 转换边失败");
                var line = ((object[])sk.GetSketchSegments()).OfType<SketchLine>().FirstOrDefault();
                if (line == null) throw new Exception("边线法兰: 转换后没有直线");
                var a = (SketchPoint)line.GetStartPoint2(); var b = (SketchPoint)line.GetEndPoint2();
                var sm = m.SketchManager;
                sm.AddToDB = true;
                sm.CreateLine(a.X, a.Y, 0, a.X, a.Y + length * K, 0);
                sm.CreateLine(a.X, a.Y + length * K, 0, b.X, b.Y + length * K, 0);
                sm.CreateLine(b.X, b.Y + length * K, 0, b.X, b.Y, 0);
                sm.AddToDB = false;
                sm.InsertSketch(true);
                sketchFeats.Add(sf);
            }
            var edges = segs.Select(find).ToArray();
            var sketches = sketchFeats.Select(x => (Sketch)x.GetSpecificFeature2()).ToArray();
            var d = (EdgeFlangeFeatureData)m.FeatureManager.CreateDefinition((int)swFeatureNameID_e.swFmEdgeFlange);
            d.AddEdges(edges, sketches);
            d.UseDefaultBendRadius = false;
            d.BendRadius = r * K;
            d.GapDistance = gap * K;
            d.BendAngle = Math.PI / 2;
            d.OffsetType = (int)swFlangeOffsetTypes_e.swFlangeOffsetBlind;
            d.OffsetDistance = length * K;
            d.OffsetDimType = (int)swFlangeDimTypes_e.swFlangeDimTypeOuterVirtualSharp;
            d.PositionType = (int)swFlangePositionTypes_e.swFlangePositionTypeMaterialInside;
            d.UsePositionOffset = false;
            Feature f = m.FeatureManager.CreateFeature(d) as Feature;
            if (f == null) throw new Exception("边线法兰失败 (返回 null)");
            double v1 = Sw.Volume(m);
            if (v1 <= v0 + 1e-3) throw new Exception("边线法兰未增加材料");
            // 实测 (2026-10-14): 多条边的边线法兰只吸收第一张轮廓草图, 其余留在特征树顶层、没有子特征; 其余边用的是整边默认轮廓
            // (压缩这些草图几何不变, 与原件差集为 0)。它们删不掉 (DeleteSelection / DeleteSelection2 各选项、释放 COM 引用后均失败),
            // 分开逐边建法兰则角部几何不同 → 压缩它们, 并核对压缩前后体积不变 (变了说明法兰其实用到了, 报错而不是掩盖)。
            // Sw.OrphanSketches 不把已压缩的草图算作未消费草图。
            var unused = sketchFeats.Where(sf => !(sf.GetChildren() is object[] ch && ch.Length > 0)).ToList();
            if (unused.Count > 0)
            {
                // 一次选中、一次压缩 (每次压缩都会重建整个零件)
                m.ClearSelection2(true);
                foreach (var sf in unused) sf.Select2(true, 0);
                if (!m.EditSuppress2()) throw new Exception("边线法兰: 压缩未用的轮廓草图失败 " + string.Join(",", unused.Select(x => x.Name)));
                m.ClearSelection2(true);
                if (unused.Any(sf => !((bool[])sf.IsSuppressed2((int)swInConfigurationOpts_e.swThisConfiguration, null))[0]))
                    throw new Exception("边线法兰: 未用的轮廓草图没有全部压缩");
                double v2 = Sw.Volume(m);
                if (Math.Abs(v2 - v1) > 1e-6 * v1) throw new Exception(string.Format("边线法兰: 压缩未用的轮廓草图后体积变了 ({0:F3} → {1:F3} mm³)", v1, v2));
                if (f.GetErrorCode2(out bool warn) != 0) throw new Exception("边线法兰: 压缩未用的轮廓草图后法兰出错");
            }
            return f;
        }

        /// <summary>实体上两端都在 坐标 k1 = c1、k2 = c2 处的直线边 (即平行于第三根坐标轴、位于这两个坐标处)</summary>
        static List<Edge> LinesAt(Body2 b, int k1, double c1, int k2, double c2)
        {
            var r = new List<Edge>();
            foreach (Edge e in (object[])b.GetEdges() ?? new object[0])
            {
                if (!((Curve)e.GetCurve()).IsLine()) continue;
                var cp = e.GetCurveParams3();
                double[] s = Mm((double[])cp.StartPoint), t = Mm((double[])cp.EndPoint);
                if (Math.Abs(s[k1] - c1) < 1e-3 && Math.Abs(t[k1] - c1) < 1e-3 && Math.Abs(s[k2] - c2) < 1e-3 && Math.Abs(t[k2] - c2) < 1e-3) r.Add(e);
            }
            return r;
        }

        /// <summary>实体上位于平面 (法向沿坐标轴 axis, 位置 at mm) 内的直线边</summary>
        public static List<Edge> LinearEdgesOn(Body2 b, int axis, double at)
        {
            var r = new List<Edge>();
            foreach (Edge e in (object[])b.GetEdges() ?? new object[0])
            {
                if (!((Curve)e.GetCurve()).IsLine()) continue;
                var cp = e.GetCurveParams3();
                double[] s = (double[])cp.StartPoint, t = (double[])cp.EndPoint;
                if (Math.Abs(s[axis] / Sw.MM - at) < 1e-4 && Math.Abs(t[axis] / Sw.MM - at) < 1e-4) r.Add(e);
            }
            return r;
        }

        static double[] Mm(double[] m) { return new[] { m[0] / Sw.MM, m[1] / Sw.MM, m[2] / Sw.MM }; }
    }
}
