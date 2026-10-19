using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;

namespace CadKernel
{
    /// <summary>截面轮廓的一段 (全局草图坐标 u,v, mm): L 直线 P=[u1,v1,u2,v2]; A 圆弧 C=[uc,vc], P=[起点, 终点], Dir=+1 逆时针 / -1 顺时针; O 整圆 C, R</summary>
    public sealed class Seg2
    {
        public string T { get; set; }
        public double[] P { get; set; }
        public double[] C { get; set; }
        public int Dir { get; set; }
        public double R { get; set; }
    }

    /// <summary>截面上的一个封闭环 (段按环的走向排列); Outer = 外轮廓, 否则为孔</summary>
    public sealed class Loop2D
    {
        public bool Outer { get; set; }
        public List<Seg2> Segs { get; set; } = new List<Seg2>();
    }

    /// <summary>
    /// 2.5D 实体: 截面沿坐标轴 Axis 从 From 拉伸到 To (零件坐标, mm), 再依次切去斜口 Cuts 与横穿孔 Holes (没有则为 null)。
    /// 草图坐标 (u,v) 与 Sk 一致: 轴 Z → XY 面 (X,Y); 轴 Y → XZ 面 (X,Z); 轴 X → YZ 面 (Z,Y)。
    /// VolumeMm3 = 原实体体积。没有切除时识别已用 截面面积 × 长度 核对; 有切除时导入时实际重建一次、与原实体做差集核对。
    /// </summary>
    public sealed class PrismDef : IBodyDef
    {
        public string Axis { get; set; }
        public double From { get; set; }
        public double To { get; set; }
        public List<Loop2D> Loops { get; set; } = new List<Loop2D>();
        public List<PlaneCut> Cuts { get; set; }
        public List<HoleCut> Holes { get; set; }
        public double VolumeMm3 { get; set; }
        /// <summary>原实体包围盒 (xmin,ymin,zmin,xmax,ymax,zmax, mm): 运行时据此 (连同体积) 从原几何中找出并删去被重建的实体; 也是斜口切除区的范围</summary>
        public double[] Box { get; set; }
    }

    /// <summary>斜口: 切去过点 P、外法向 N 一侧的部分 (零件坐标, mm)。N 恰有一个分量为 0, 即切口平面平行于该坐标轴</summary>
    public sealed class PlaneCut
    {
        public double[] N { get; set; }
        public double[] P { get; set; }
    }

    /// <summary>横穿孔: 轴线沿坐标轴 Axis, 草图坐标 C (同 PrismDef), 直径 D, 沿轴从 From 到 To 切除</summary>
    public sealed class HoleCut
    {
        public string Axis { get; set; }
        public double[] C { get; set; }
        public double D { get; set; }
        public double From { get; set; }
        public double To { get; set; }
    }

    /// <summary>
    /// 2.5D 识别与重建。一个实体若存在一根坐标轴, 使每个面属于下列之一, 它就是截面沿该轴的拉伸再切去斜口与横孔:
    /// - 端面: 垂直于该轴的平面, 只在实体两端;
    /// - 侧面: 法向垂直于该轴的平面、轴线平行于该轴的圆柱;
    /// - 斜口: 与该轴斜交、且平行于另一根坐标轴的平面 (型材斜口、拼角);
    /// - 横穿孔: 轴线沿另一根坐标轴的圆柱面。
    /// 截面取自端面 (没有斜口与横孔时), 或在既无斜口也无横孔处用薄板与实体求交得到。
    /// 没有切除时须满足 截面面积 × 长度 = 原实体体积 (±0.001%); 有切除时导入时实际建一次 (Verify), 与原实体差集为 0 才采用 ——
    /// 宁可保留原几何, 不产生似是而非的重建。
    /// </summary>
    public static class Prism
    {
        // ------------------------------------------------------------------ 纯几何 (可单元测试)
        /// <summary>一个环的有向面积 (格林公式, 段须按环的走向排列); 整圆按正值</summary>
        public static double SignedArea(Loop2D loop)
        {
            double s = 0;
            foreach (var g in loop.Segs)
            {
                if (g.T == "O") return Math.PI * g.R * g.R;
                double u1 = g.P[0], v1 = g.P[1], u2 = g.P[2], v2 = g.P[3];
                if (g.T == "L") { s += u1 * v2 - u2 * v1; continue; }
                double cu = g.C[0], cv = g.C[1], r = Math.Sqrt((u1 - cu) * (u1 - cu) + (v1 - cv) * (v1 - cv));
                double a1 = Math.Atan2(v1 - cv, u1 - cu), a2 = Math.Atan2(v2 - cv, u2 - cu);
                double sweep = g.Dir > 0 ? Mod2Pi(a2 - a1) : -Mod2Pi(a1 - a2);
                s += r * r * sweep + cu * (v2 - v1) - cv * (u2 - u1);
            }
            return s / 2;
        }

        static double Mod2Pi(double a)
        {
            a %= 2 * Math.PI;
            if (a <= 1e-12) a += 2 * Math.PI;
            return a;
        }

        /// <summary>
        /// 把一个环的各段按端点首尾相接排序并统一走向 (段内的起点、终点与圆弧旋向本身是对的; 反向时交换端点并取反旋向)。
        /// 接不上或不闭合返回 null。整圆单独成环, 原样返回。
        /// </summary>
        public static List<Seg2> Chain(List<Seg2> segs)
        {
            if (segs.Count == 0) return null;
            if (segs.Count == 1) return segs[0].T == "O" ? segs : null;
            if (segs.Any(g => g.T == "O")) return null;
            const double tol = 1e-3;
            Func<double[], double[], bool> same = (a, b) => Math.Abs(a[0] - b[0]) <= tol && Math.Abs(a[1] - b[1]) <= tol;
            Func<Seg2, double[]> start = g => new[] { g.P[0], g.P[1] }, end = g => new[] { g.P[2], g.P[3] };
            var left = segs.Skip(1).ToList();
            var r = new List<Seg2> { segs[0] };
            while (left.Count > 0)
            {
                double[] cur = end(r[r.Count - 1]);
                int k = left.FindIndex(g => same(start(g), cur));
                if (k < 0)
                {
                    k = left.FindIndex(g => same(end(g), cur));
                    if (k < 0) return null;
                    var g0 = left[k];
                    left[k] = new Seg2 { T = g0.T, C = g0.C, P = new[] { g0.P[2], g0.P[3], g0.P[0], g0.P[1] }, Dir = -g0.Dir };
                }
                r.Add(left[k]);
                left.RemoveAt(k);
            }
            return same(end(r[r.Count - 1]), start(r[0])) ? r : null;
        }

        /// <summary>截面面积 = Σ|外环| − Σ|孔|</summary>
        public static double Area(IEnumerable<Loop2D> loops)
        {
            return loops.Sum(l => (l.Outer ? 1 : -1) * Math.Abs(SignedArea(l)));
        }

        /// <summary>零件坐标点 (mm) → 草图坐标 (u,v)</summary>
        public static double[] UV(int axis, double[] p)
        {
            return axis == 2 ? new[] { p[0], p[1] } : axis == 1 ? new[] { p[0], p[2] } : new[] { p[2], p[1] };
        }

        /// <summary>凸多边形 (u,v 顶点依次排列) 与半平面 n·x ≥ c 的交 (Sutherland–Hodgman); 交为空时返回空表</summary>
        public static List<double[]> ClipHalfPlane(List<double[]> poly, double[] n, double c)
        {
            var r = new List<double[]>();
            for (int i = 0; i < poly.Count; i++)
            {
                double[] p = poly[i], q = poly[(i + 1) % poly.Count];
                double dp = n[0] * p[0] + n[1] * p[1] - c, dq = n[0] * q[0] + n[1] * q[1] - c;
                if (dp >= 0) r.Add(p);
                if ((dp >= 0) != (dq >= 0))
                {
                    double t = dp / (dp - dq);
                    r.Add(new[] { p[0] + t * (q[0] - p[0]), p[1] + t * (q[1] - p[1]) });
                }
            }
            return r;
        }

        /// <summary>
        /// 斜口切除区在草图 (垂直于切口平面所平行的坐标轴) 中的多边形: 原实体包围盒外扩 margin 后, 落在切口外侧的部分。
        /// 返回 k (草图基准面的法向轴) 与多边形顶点 (u,v); 切口不平行于任何坐标轴时 k = -1。
        /// </summary>
        public static List<double[]> CutRegion(PlaneCut c, double[] box, double margin, out int k)
        {
            k = ZeroAxis(c.N);
            if (k < 0) return new List<double[]>();
            double[] lo = UV(k, new[] { box[0] - margin, box[1] - margin, box[2] - margin });
            double[] hi = UV(k, new[] { box[3] + margin, box[4] + margin, box[5] + margin });
            var rect = new List<double[]> { new[] { lo[0], lo[1] }, new[] { hi[0], lo[1] }, new[] { hi[0], hi[1] }, new[] { lo[0], hi[1] } };
            double[] n = UV(k, c.N), p = UV(k, c.P);
            return ClipHalfPlane(rect, n, n[0] * p[0] + n[1] * p[1]);
        }

        /// <summary>恰有一个分量为 0 的方向: 返回该分量的序号; 否则 -1</summary>
        public static int ZeroAxis(double[] n)
        {
            const double tol = 1e-7;
            var zero = Enumerable.Range(0, 3).Where(i => Math.Abs(n[i]) < tol).ToList();
            return zero.Count == 1 ? zero[0] : -1;
        }

        /// <summary>沿某根坐标轴的方向: 返回该轴序号; 否则 -1</summary>
        public static int AlongAxis(double[] d)
        {
            const double tol = 1e-7;
            for (int i = 0; i < 3; i++) if (Math.Abs(Math.Abs(d[i]) - 1) < tol) return i;
            return -1;
        }

        // ------------------------------------------------------------------ 识别 (导入时, 读 SolidWorks 实体)
        /// <summary>实体的一个面: 类型 P 平面 / C 圆柱, Dir = 平面法向或圆柱轴向, Pt = 平面上或圆柱轴线上的点 (mm), R = 圆柱半径 (mm)</summary>
        sealed class FaceGeo
        {
            public Face2 F; public char Kind; public double[] Dir, Pt; public double R;
        }

        /// <summary>沿某根轴的分类结果</summary>
        sealed class AxisPlan
        {
            public int A; public List<double> Levels = new List<double>();
            public List<FaceGeo> Miters = new List<FaceGeo>(), Holes = new List<FaceGeo>();
            public int Bad; public bool Ends;
            public int Cuts { get { return Miters.Count + Holes.Count; } }
        }

        /// <summary>识别一个实体; 不是可重建的 2.5D 时返回 null, why 说明原因。有切除的结果还须经 Verify 实际重建核对</summary>
        public static PrismDef Recognize(Body2 body, out string why, List<string> trace = null)
        {
            why = null;
            var faces = ((object[])body.GetFaces() ?? new object[0]).Cast<Face2>().ToList();
            var info = new List<FaceGeo>();
            var other = new List<string>();
            foreach (var f in faces)
            {
                var s = (Surface)f.GetSurface();
                if (s.IsPlane()) { var q = (double[])s.PlaneParams; info.Add(new FaceGeo { F = f, Kind = 'P', Dir = new[] { q[0], q[1], q[2] }, Pt = Mm(new[] { q[3], q[4], q[5] }) }); }
                else if (s.IsCylinder()) { var q = (double[])s.CylinderParams; info.Add(new FaceGeo { F = f, Kind = 'C', Dir = new[] { q[3], q[4], q[5] }, Pt = Mm(new[] { q[0], q[1], q[2] }), R = q[6] / Sw.MM }); }
                else other.Add(s.IsTorus() ? "圆环面" : s.IsCone() ? "圆锥面" : s.IsSphere() ? "球面" : s.IsSwept() ? "拉伸曲面" : s.IsParametric() ? "样条面" : "其它曲面");
            }
            if (other.Count > 0)
            {
                why = "含平面与圆柱以外的面 (" + string.Join(", ", other.GroupBy(x => x).OrderBy(g => g.Key).Select(g => g.Key)) + ")";
                if (trace != null) trace.Add(string.Join(", ", other.GroupBy(x => x).Select(g => g.Key + "×" + g.Count())) + ", 平面/圆柱 " + info.Count);
                return null;
            }
            double vol = Vol(body);
            double[] box = Box(body);
            // 逐轴分类; 优先没有切除的 (纯拉伸), 其次切除最少、沿轴最长的
            const double tol = 1e-7;
            var plans = new List<AxisPlan>();
            for (int a = 0; a < 3; a++)
            {
                var pl = new AxisPlan { A = a };
                foreach (var t in info)
                {
                    double d = Math.Abs(t.Dir[a]);
                    if (t.Kind == 'P' && Math.Abs(d - 1) < tol) pl.Levels.Add(t.Pt[a]);
                    else if (t.Kind == 'P' && d < tol) { }
                    else if (t.Kind == 'C' && Math.Abs(d - 1) < tol) { }
                    else if (t.Kind == 'P' && ZeroAxis(t.Dir) >= 0) pl.Miters.Add(t);
                    else if (t.Kind == 'C' && AlongAxis(t.Dir) >= 0) pl.Holes.Add(t);
                    else pl.Bad++;
                }
                pl.Levels = pl.Levels.Select(x => Math.Round(x, 4)).Distinct().OrderBy(x => x).ToList();
                plans.Add(pl);
            }
            // 纯拉伸: 端面恰好两层 (必在两端)。有切除时端面须在沿轴的最低、最高处 —— 极值取自顶点 (端面、斜口都是平面, 极值在其边界的顶点上;
            // GetBodyBox 对曲面实体不一定紧)。逐边取样在大装配会话里每个实体要上千次 COM 调用 (实测 10 s 量级), 只在需要时取顶点
            foreach (var pl in plans) pl.Ends = pl.Cuts == 0 && pl.Levels.Count == 2;
            double[] lo = null, hi = null;
            if (!plans.Any(x => x.Bad == 0 && x.Ends) && plans.Any(x => x.Bad == 0 && x.Cuts > 0))
            {
                var vp = VertexPoints(body);
                lo = Enumerable.Range(0, 3).Select(i => vp.Min(q => q[i])).ToArray(); hi = Enumerable.Range(0, 3).Select(i => vp.Max(q => q[i])).ToArray();
                foreach (var pl in plans.Where(x => x.Cuts > 0))
                    pl.Ends = pl.Levels.All(z => Math.Abs(z - lo[pl.A]) < 1e-3 || Math.Abs(z - hi[pl.A]) < 1e-3);
            }
            if (trace != null)
                foreach (var pl in plans)
                    trace.Add(string.Format("轴 {0}: 端面 {1} 层{2}, 斜口 {3}, 横孔 {4}, 其它 {5}", "XYZ"[pl.A], pl.Levels.Count,
                        pl.Ends ? "" : " (不全在两端)", pl.Miters.Count, pl.Holes.Count, pl.Bad));
            var ok = plans.Where(x => x.Bad == 0 && x.Ends).ToList();
            var best = ok.FirstOrDefault(x => x.Cuts == 0) ?? ok.OrderBy(x => x.Cuts).ThenByDescending(x => hi[x.A] - lo[x.A]).FirstOrDefault();
            if (best == null) { why = "没有一根坐标轴使所有面都是端面、侧面、斜口或横孔"; return null; }
            int axis = best.A;
            var p = new PrismDef { Axis = "XYZ".Substring(axis, 1) };
            double predicted, actual;          // 截面算出的体积 / 实测体积
            if (best.Cuts == 0)
            {
                // 纯拉伸: 截面取自顶端面
                p.From = best.Levels[0]; p.To = best.Levels[1];
                var caps = info.Where(t => t.Kind == 'P' && Math.Abs(Math.Abs(t.Dir[axis]) - 1) < tol && Math.Abs(t.Pt[axis] - p.To) < 1e-3).Select(t => t.F);
                p.Loops = CapLoops(caps, axis, out why);
                if (p.Loops == null) return null;
                predicted = Area(p.Loops) * (p.To - p.From); actual = vol;
            }
            else
            {
                // 斜口与横孔: 先拉伸成直型材 (沿轴从最低点到最高点), 再切
                p.From = Math.Round(lo[axis], 6); p.To = Math.Round(hi[axis], 6);
                double[] cg = Mm((double[])body.GetMassProperties(1.0));
                var spans = new List<double[]>();                          // 沿主轴被斜口或横孔占据的区间
                p.Cuts = new List<PlaneCut>();
                foreach (var t in best.Miters)
                {
                    double nl = Math.Sqrt(t.Dir.Sum(x => x * x));
                    double[] n = t.Dir.Select(x => x / nl).ToArray();
                    if (Dot(n, Sub(cg, t.Pt)) > 0) n = n.Select(x => -x).ToArray();     // 外法向: 背离实体形心
                    var c = new PlaneCut { N = n.Select(x => Math.Round(x, 12) + 0.0).ToArray(), P = R6(t.Pt) };      // + 0.0: 不写出 -0
                    if (!p.Cuts.Any(o => Enumerable.Range(0, 3).All(i => Math.Abs(o.N[i] - c.N[i]) < 1e-9) && Math.Abs(Dot(o.N, Sub(c.P, o.P))) < 1e-4)) p.Cuts.Add(c);
                    var pts = FaceVertexPoints(t.F).Select(q => q[axis]).ToList();
                    spans.Add(new[] { pts.Min(), pts.Max() });
                }
                p.Holes = new List<HoleCut>();
                foreach (var t in best.Holes)
                {
                    int k = AlongAxis(t.Dir);
                    var pts = FacePoints(t.F).Select(q => q[k]).ToList();
                    double[] cu = UV(k, t.Pt);
                    var h = new HoleCut { Axis = "XYZ".Substring(k, 1), C = R6(cu[0], cu[1]), D = Math.Round(2 * t.R, 6), From = Math.Round(pts.Min(), 6), To = Math.Round(pts.Max(), 6) };
                    if (!(h.To > h.From)) { why = "横孔沿轴长度为 0"; return null; }
                    if (!p.Holes.Any(o => o.Axis == h.Axis && o.D == h.D && o.From == h.From && o.To == h.To && o.C[0] == h.C[0] && o.C[1] == h.C[1])) p.Holes.Add(h);
                    spans.Add(new[] { t.Pt[axis] - t.R, t.Pt[axis] + t.R });
                }
                if (p.Cuts.Count == 0) p.Cuts = null;
                if (p.Holes.Count == 0) p.Holes = null;
                // 截面: 在最长的一段无切除区间中部, 用 1 mm (或更薄) 的薄板与实体求交, 取薄板顶面的环
                double z0, dz;
                if (!FreeLevel(p.From, p.To, spans, out z0, out dz)) { why = "沿轴处处有斜口或横孔, 取不到完整截面"; return null; }
                double slabVol;
                p.Loops = Section(body, axis, z0, dz, box, out slabVol, out why);
                if (p.Loops == null) return null;
                if (trace != null) trace.Add(string.Format("截面取在 {0} = {1:F3} (厚 {2:F3})", "XYZ"[axis], z0, dz));
                predicted = Area(p.Loops) * dz; actual = slabVol;
                if (Area(p.Loops) * (p.To - p.From) < vol * (1 - 1e-6)) { why = "截面拉伸体积小于实体体积"; return null; }
            }
            if (trace != null)
            {
                trace.Add(string.Format("轴 {0}, {1} → {2}, 面共 {3} 个", p.Axis, p.From, p.To, faces.Count));
                foreach (var l in p.Loops)
                    trace.Add(string.Format("{0} {1} 段 [{2}] 有向面积 {3:F3}", l.Outer ? "外环" : "内环", l.Segs.Count,
                        string.Join("", l.Segs.Select(g => g.T)), SignedArea(l)));
                foreach (var c in p.Cuts ?? new List<PlaneCut>())
                    trace.Add("斜口 N=" + string.Join(",", c.N.Select(x => x.ToString("F4"))) + " P=" + string.Join(",", c.P.Select(x => x.ToString("F3"))));
                foreach (var h in p.Holes ?? new List<HoleCut>())
                    trace.Add(string.Format("横孔 {0} Ø{1} @ {2:F3},{3:F3}  {4:F3} → {5:F3}", h.Axis, h.D, h.C[0], h.C[1], h.From, h.To));
                trace.Add(string.Format("截面核对: 截面算出 {0:F3} / 实测 {1:F3} mm³", predicted, actual));
            }
            if (actual <= 0 || Math.Abs(predicted - actual) / actual > 1e-5) { why = p.Cuts == null && p.Holes == null ? "截面面积 × 长度与实体体积不符" : "截面面积与取样薄板体积不符"; return null; }
            p.VolumeMm3 = Math.Round(vol, 3);
            p.Box = box.Select(v => Math.Round(v, 4)).ToArray();
            return p;
        }

        /// <summary>端面 (可多个) 的各个环 → 截面; 出错返回 null 并给出原因</summary>
        internal static List<Loop2D> CapLoops(IEnumerable<Face2> caps, int axis, out string why)
        {
            why = null;
            var loops = new List<Loop2D>();
            foreach (var f in caps)
                foreach (Loop2 lp in (object[])f.GetLoops() ?? new object[0])
                {
                    var loop = new Loop2D { Outer = lp.IsOuter() };
                    foreach (CoEdge ce in (object[])lp.GetCoEdges() ?? new object[0])
                    {
                        var seg = EdgeSeg((Edge)ce.GetEdge(), ce.GetSense(), axis);
                        if (seg == null) { why = "截面边不是直线、圆弧或整圆"; return null; }
                        loop.Segs.Add(seg);
                    }
                    // 实测 (2026-10-11): 边端点与共边走向标志对不上, 各段方向不一致 → 按端点首尾相接重新排序定向 (面积计算依赖走向)
                    var chained = Chain(loop.Segs);
                    if (chained == null) { why = "截面环不闭合"; return null; }
                    loop.Segs = chained;
                    loops.Add(loop);
                }
            if (loops.Count == 0) { why = "没有端面"; return null; }
            // 多个端面共边时, 共有边会画两遍 → 不认
            var keys = loops.SelectMany(l => l.Segs).Where(g => g.T != "O").Select(g => Key(g)).ToList();
            if (keys.Count != keys.Distinct().Count()) { why = "端面由多个面拼成"; return null; }
            return loops;
        }

        /// <summary>[from, to] 中去掉 spans 后最长的一段: 取其中部 z0 起厚 dz (≤ 1 mm, 且两侧各留出至少 dz) 的薄层; 最长段短于 0.3 mm 时返回 false</summary>
        public static bool FreeLevel(double from, double to, List<double[]> spans, out double z0, out double dz)
        {
            double best = 0, at = 0, cur = from;
            foreach (var s in spans.OrderBy(s => s[0]))
            {
                if (s[0] - cur > best) { best = s[0] - cur; at = cur; }
                cur = Math.Max(cur, s[1]);
            }
            if (to - cur > best) { best = to - cur; at = cur; }
            dz = Math.Min(1.0, best / 3);
            z0 = at + best / 2 - dz / 2;
            return best >= 0.3;
        }

        /// <summary>
        /// 截面: 实体 ∩ 薄板 (主轴方向 [z0, z0+dz], 横向足够大), 取薄板顶面的环。
        /// 薄板用 Modeler.CreateBodyFromBox (前 3 个数为底面中心, 再 3 个为轴向, 宽、长、高; 实测见 api_calibration), 建出后核对其沿轴范围。
        /// </summary>
        static List<Loop2D> Section(Body2 body, int axis, double z0, double dz, double[] box, out double slabVol, out string why)
        {
            slabVol = 0;
            double w = 2 * Enumerable.Range(0, 3).Max(i => box[i + 3] - box[i]) + 20;
            var c = new[] { (box[0] + box[3]) / 2, (box[1] + box[4]) / 2, (box[2] + box[5]) / 2 };
            c[axis] = z0;
            var d = new double[3]; d[axis] = 1;
            var dims = new[] { c[0] * Sw.MM, c[1] * Sw.MM, c[2] * Sw.MM, d[0], d[1], d[2], w * Sw.MM, w * Sw.MM, dz * Sw.MM };
            var slab = (Body2)((Modeler)Sw.App.GetModeler()).CreateBodyFromBox(dims);
            if (slab == null) { why = "截面取样失败 (薄板建不出)"; return null; }
            double[] sb = Box(slab);
            if (Math.Abs(sb[axis] - z0) > 1e-6 || Math.Abs(sb[axis + 3] - z0 - dz) > 1e-6) { why = "截面取样失败 (薄板位置与标定不符)"; return null; }
            int code;
            var outs = ((Body2)body.Copy()).Operations2((int)SolidWorks.Interop.swconst.swBodyOperationType_e.SWBODYINTERSECT, slab, out code) as object[];
            if (code != 0 || outs == null || outs.Length != 1) { why = "截面不是一个连通区域 (求交结果 " + (outs?.Length ?? 0) + " 个, 错误码 " + code + ")"; return null; }
            var s = (Body2)outs[0];
            slabVol = Vol(s);
            var caps = ((object[])s.GetFaces() ?? new object[0]).Cast<Face2>().Where(f =>
            {
                var su = (Surface)f.GetSurface();
                if (!su.IsPlane()) return false;
                var q = (double[])su.PlaneParams;
                return Math.Abs(Math.Abs(q[axis]) - 1) < 1e-7 && Math.Abs(q[3 + axis] / Sw.MM - (z0 + dz)) < 1e-4;
            });
            return CapLoops(caps, axis, out why);
        }

        /// <summary>边上的取样点 (9 点, 含两端; mm)</summary>
        internal static IEnumerable<double[]> EdgePoints(Edge e)
        {
            var cp = e.GetCurveParams3();
            for (int k = 0; k <= 8; k++)
                yield return Mm((double[])e.Evaluate2(cp.UMinValue + (cp.UMaxValue - cp.UMinValue) * k / 8, 0));
        }

        /// <summary>面上各条边的取样点 (横孔面的边常是没有顶点的整圆, 须取样)</summary>
        internal static IEnumerable<double[]> FacePoints(Face2 f)
        {
            return ((object[])f.GetEdges() ?? new object[0]).Cast<Edge>().SelectMany(EdgePoints);
        }

        /// <summary>实体全部顶点 (mm); 没有顶点 (如只由整圆围成) 时退回逐边取样</summary>
        internal static List<double[]> VertexPoints(Body2 b)
        {
            var r = ((object[])b.GetVertices() ?? new object[0]).Cast<Vertex>().Select(v => Mm((double[])v.GetPoint())).ToList();
            return r.Count > 0 ? r : ((object[])b.GetEdges() ?? new object[0]).Cast<Edge>().SelectMany(EdgePoints).ToList();
        }

        /// <summary>面上各边端点 (mm); 没有顶点时退回取样</summary>
        internal static List<double[]> FaceVertexPoints(Face2 f)
        {
            var r = new List<double[]>();
            foreach (Edge e in (object[])f.GetEdges() ?? new object[0])
                foreach (var v in new[] { e.GetStartVertex(), e.GetEndVertex() })
                    if (v is Vertex vx) r.Add(Mm((double[])vx.GetPoint()));
            return r.Count > 0 ? r : FacePoints(f).ToList();
        }

        static double Dot(double[] a, double[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }
        static double[] Sub(double[] a, double[] b) { return new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] }; }

        /// <summary>
        /// 端面上的一条边 → 截面段 (按共边走向)。直线、圆直接取; 其它曲线 (导入件常见: 直线与圆弧以样条存储) 取 9 个点判别:
        /// 共线 → 直线; 与过起点/中点/终点的圆相距 ≤ 1e-4 mm → 圆弧 (首尾重合为整圆); 否则返回 null (真样条, 不重建)。
        /// </summary>
        static Seg2 EdgeSeg(Edge e, bool sense, int axis)
        {
            var c = (Curve)e.GetCurve();
            var cp = e.GetCurveParams3();
            double[] s0 = Mm((double[])cp.StartPoint), e0 = Mm((double[])cp.EndPoint);
            if (!sense) { var t = s0; s0 = e0; e0 = t; }
            double[] su = UV(axis, s0), eu = UV(axis, e0);
            bool closed = Math.Abs(su[0] - eu[0]) < 1e-6 && Math.Abs(su[1] - eu[1]) < 1e-6;
            if (c.IsLine() && !closed) return new Seg2 { T = "L", P = R6(su[0], su[1], eu[0], eu[1]) };
            if (c.IsCircle())
            {
                // 圆直接读参数; 圆弧只取中点定旋向 (冲孔板一块就有上千个孔, 每次 COM 调用约 10 ms)
                var q = (double[])c.CircleParams;
                double[] cu = UV(axis, Mm(new[] { q[0], q[1], q[2] }));
                if (closed) return new Seg2 { T = "O", C = R6(cu[0], cu[1]), R = Math.Round(q[6] / Sw.MM, 6) };
                return Arc(cu, su, eu, UV(axis, Mm((double[])e.Evaluate2((cp.UMinValue + cp.UMaxValue) / 2, 0))));
            }
            // 其它曲线: 采样 (按走向排列)
            var pts = new List<double[]>();
            for (int k = 0; k <= 8; k++)
            {
                double t = cp.UMinValue + (cp.UMaxValue - cp.UMinValue) * k / 8;
                pts.Add(UV(axis, Mm((double[])e.Evaluate2(t, 0))));
            }
            if (!sense) pts.Reverse();
            const double tol = 1e-4;
            if (!closed)
            {
                double dx = eu[0] - su[0], dy = eu[1] - su[1], len = Math.Sqrt(dx * dx + dy * dy);
                if (len > 1e-9 && pts.All(p => Math.Abs((p[0] - su[0]) * dy - (p[1] - su[1]) * dx) / len <= tol))
                    return new Seg2 { T = "L", P = R6(su[0], su[1], eu[0], eu[1]) };
            }
            double[] cc = Center(pts[0], pts[closed ? 3 : 4], pts[closed ? 6 : 8]);
            if (cc == null) return null;
            double r = Dist(cc, pts[0]);
            if (pts.Any(p => Math.Abs(Dist(cc, p) - r) > tol)) return null;
            if (closed) return new Seg2 { T = "O", C = R6(cc[0], cc[1]), R = Math.Round(r, 6) };
            return Arc(cc, su, eu, pts[4]);
        }

        static Seg2 Arc(double[] c, double[] s, double[] e, double[] mid)
        {
            double cross = (s[0] - c[0]) * (mid[1] - c[1]) - (s[1] - c[1]) * (mid[0] - c[0]);
            return new Seg2 { T = "A", C = R6(c[0], c[1]), P = R6(s[0], s[1], e[0], e[1]), Dir = cross > 0 ? 1 : -1 };
        }

        /// <summary>过三点的圆心 (共线返回 null)</summary>
        internal static double[] Center(double[] a, double[] b, double[] c)
        {
            double d = 2 * (a[0] * (b[1] - c[1]) + b[0] * (c[1] - a[1]) + c[0] * (a[1] - b[1]));
            if (Math.Abs(d) < 1e-12) return null;
            double a2 = a[0] * a[0] + a[1] * a[1], b2 = b[0] * b[0] + b[1] * b[1], c2 = c[0] * c[0] + c[1] * c[1];
            return new[] { (a2 * (b[1] - c[1]) + b2 * (c[1] - a[1]) + c2 * (a[1] - b[1])) / d, (a2 * (c[0] - b[0]) + b2 * (a[0] - c[0]) + c2 * (b[0] - a[0])) / d };
        }

        static double Dist(double[] a, double[] b) { return Math.Sqrt((a[0] - b[0]) * (a[0] - b[0]) + (a[1] - b[1]) * (a[1] - b[1])); }

        internal static double[] Mm(double[] m) { return new[] { m[0] / Sw.MM, m[1] / Sw.MM, m[2] / Sw.MM }; }
        internal static double[] R6(params double[] v) { return v.Select(x => Math.Round(x, 6) + 0.0).ToArray(); }
        static string Key(Seg2 g)
        {
            var a = string.Format("{0:F4},{1:F4}", g.P[0], g.P[1]); var b = string.Format("{0:F4},{1:F4}", g.P[2], g.P[3]);
            return g.T + (string.CompareOrdinal(a, b) < 0 ? a + ";" + b : b + ";" + a);
        }

        // ------------------------------------------------------------------ 重建 (运行时)
        /// <summary>
        /// 按定义画截面并拉伸为独立实体 (不与零件中的其它实体合并), 再切斜口、横孔。
        /// 切除限定只作用于这个实体 (特征范围): 拼角处被切去的楔形正是相邻型材端头所在的位置。
        /// </summary>
        public static void Build(ModelDoc2 m, PrismDef p)
        {
            Sk s = Plane(m, p.Axis, p.From);
            foreach (var l in p.Loops)
                foreach (var g in l.Segs)
                {
                    if (g.T == "L") s.Line(g.P[0], g.P[1], g.P[2], g.P[3]);
                    else if (g.T == "A") s.ArcDir(g.C[0], g.C[1], g.P[0], g.P[1], g.P[2], g.P[3], g.Dir);
                    else s.Circ(g.C[0], g.C[1], 2 * g.R);
                }
            Body2 b = BodyOf(s.Ext(p.To - p.From, 1, false));
            foreach (var c in p.Cuts ?? new List<PlaneCut>())
            {
                int k;
                var poly = CutRegion(c, p.Box, 5, out k);
                if (poly.Count < 3) throw new Exception("斜口切除区为空");
                b = BodyOf(Plane(m, "XYZ".Substring(k, 1), 0).Poly(poly.SelectMany(q => q).ToArray()).CutThru(0, b));
            }
            foreach (var h in p.Holes ?? new List<HoleCut>())
                b = BodyOf(Plane(m, h.Axis, h.From).Circ(h.C[0], h.C[1], h.D).Cut(h.To - h.From, 1, b));
        }

        /// <summary>垂直于坐标轴 axis、位于 at 处的草图</summary>
        static Sk Plane(ModelDoc2 m, string axis, double at)
        {
            return axis == "Z" ? Sk.XY(m, at) : axis == "Y" ? Sk.XZ(m, at) : Sk.YZ(m, at);
        }

        /// <summary>特征所在的实体 (特征后实体会改名、旧指针失效, 每次从新特征的面取)</summary>
        static Body2 BodyOf(Feature f)
        {
            var faces = f.GetFaces() as object[];
            var b = faces == null || faces.Length == 0 ? null : ((Face2)faces[0]).GetBody() as Body2;
            if (b == null) throw new Exception("找不到特征 " + f.Name + " 所在的实体");
            return b;
        }

        /// <summary>
        /// 导入时核对有切除的定义: 在临时零件中实际建一次, 与原实体互相求差, 剩余体积 ≤ 原实体体积 × 1e-6 才算识别正确。
        /// 返回 null = 一致; 否则为原因。临时零件不保存。
        /// </summary>
        public static string Verify(PrismDef p, Body2 original) { return Verify(m => Build(m, p), original); }

        /// <summary>同上, 重建方式由 build 给出 (钣金等)</summary>
        public static string Verify(Action<ModelDoc2> build, Body2 original)
        {
            ModelDoc2 m = Sw.NewPart();
            try
            {
                try { build(m); }
                catch (Exception e) { return "重建失败: " + e.Message; }
                // 与运行时 Cat 的防线一致: 留下未消费草图 = 有特征静默失败
                m.ForceRebuild3(false);
                var orphan = Sw.OrphanSketches(m);
                if (orphan.Count > 0) return "重建后留下未消费草图 " + string.Join(",", orphan);
                var bs = Solids(m);
                if (bs.Count != 1) return "重建得到 " + bs.Count + " 个实体";
                string e1, e2;
                double x = CutVolume(original, bs[0], out e1) + CutVolume(bs[0], original, out e2);
                if (e1 != null || e2 != null) return "布尔比对失败: " + (e1 ?? e2);
                if (x > 1e-6 * Vol(original)) return string.Format("重建后与原实体差集 {0:F3} mm³", x);
                return null;
            }
            finally { Sw.App.CloseDoc(m.GetTitle()); }
        }

        /// <summary>
        /// 部分重建的零件: 从载入的原几何中删去将被重建的实体 —— 每个定义按体积 (±1e-6) 与包围盒 (±0.01 mm) 一一对应,
        /// 对应不上即报错 (不猜)。之后再用 Build 建出这些实体。
        /// </summary>
        public static void RemoveRebuilt(ModelDoc2 m, IList<IBodyDef> ps)
        {
            var all = Solids(m);
            var used = new bool[all.Count];
            m.ClearSelection2(true);
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                int j = -1;
                for (int k = 0; k < all.Count && j < 0; k++)
                    if (!used[k] && Is(p, Vol(all[k]), Box(all[k]))) j = k;
                if (j < 0) throw new Exception(string.Format("原几何中找不到与重建实体 {0} (体积 {1:F1} mm³) 对应的实体", i + 1, p.VolumeMm3));
                used[j] = true;
                all[j].Select2(true, null);
            }
            if (m.FeatureManager.InsertDeleteBody2(false) == null) throw new Exception("删除被重建的原实体失败");
            int left = Sw.BodyCount(m);
            if (left != all.Count - ps.Count) throw new Exception(string.Format("删除后剩余实体 {0}, 应为 {1}", left, all.Count - ps.Count));
        }

        /// <summary>在零件的实体中找出这个定义对应的原实体 (体积 + 包围盒); 找不到返回 null</summary>
        public static Body2 Find(ModelDoc2 m, IBodyDef d)
        {
            return Solids(m).FirstOrDefault(b => Is(d, Vol(b), Box(b)));
        }

        /// <summary>原几何的实体 (体积 mm³, 包围盒 mm) 是否就是这个定义要重建的那个: 体积 ±1e-6, 包围盒 ±0.01 mm</summary>
        internal static bool Is(IBodyDef p, double vol, double[] box)
        {
            return Math.Abs(vol - p.VolumeMm3) <= 1e-6 * p.VolumeMm3 + 1e-3 && p.Box != null
                && Enumerable.Range(0, 6).All(n => Math.Abs(box[n] - p.Box[n]) <= 0.01);
        }

        // ------------------------------------------------------------------ 与原几何逐实体比对 (运行时)
        public sealed class CompareResult
        {
            public int Original, Matched, Rebuilt, Kept;
            public double MaxXorMm3, MaxXorRel;
            public List<string> Problems = new List<string>();
        }

        /// <summary>
        /// 原几何的每个实体须在重建零件中找到对应实体:
        /// - 被重建的实体 (prisms 中有对应定义): 包围盒 (±0.01 mm) 相同、体积相差 ≤ 1e-3 的实体与之配对, 再互相做差 (A−B 与 B−A, 临时实体),
        ///   剩下的体积之和 ≤ 原实体体积的 1e-6 才算相同 —— 判定靠差集, 体积只用于配对。实测 (2026-10-13): 双向差集为 0 的斜口方管,
        ///   GetMassProperties 算出的体积原几何 252562.9、重建 252565.9 mm³ (相差 1.2e-5); 带 9 个孔的钣金托盘相差 1.1e-4 (体积积分的数值误差, 2026-10-14)。布尔运算本身失败记为问题, 不当作 0;
        /// - 保留原几何的实体: 体积 (±1e-6)、包围盒 (±0.01 mm)、面数都相同 (与未改动的原几何零件同一判据)。不对它做布尔: 它本来就是原实体,
        ///   而实测 (2026-10-11) 与自身完全重合的样条钣金实体求差、求交都会 BooleanFail (1058)。
        /// </summary>
        public static CompareResult Compare(ModelDoc2 original, ModelDoc2 rebuilt, IList<IBodyDef> prisms)
        {
            var res = new CompareResult();
            var o = Solids(original); var r = Solids(rebuilt);
            res.Original = o.Count;
            if (r.Count != o.Count) res.Problems.Add(string.Format("实体数 {0}, 原几何 {1}", r.Count, o.Count));
            var used = new bool[r.Count];
            var defUsed = new bool[prisms.Count];
            for (int i = 0; i < o.Count; i++)
            {
                double vo = Vol(o[i]); double[] bo = Box(o[i]);
                string what = string.Format("原实体 {0} (体积 {1:F1} mm³, 包围盒 {2})", i + 1, vo, string.Join(",", bo.Select(v => v.ToString("F1"))));
                int d = Enumerable.Range(0, prisms.Count).FirstOrDefault(n => !defUsed[n] && Is(prisms[n], vo, bo), -1);
                double vtol = d >= 0 ? 1e-3 * vo : 1e-6 * vo + 1e-6;
                int j = -1;
                for (int k = 0; k < r.Count && j < 0; k++)
                {
                    if (used[k] || Math.Abs(Vol(r[k]) - vo) > vtol) continue;
                    double[] bk = Box(r[k]);
                    if (Enumerable.Range(0, 6).All(n => Math.Abs(bk[n] - bo[n]) <= 0.01)) j = k;
                }
                if (j < 0)
                {
                    // 诊断: 包围盒相同、体积不同的重建实体 → 两个方向的差集各多少
                    int near = Enumerable.Range(0, r.Count).FirstOrDefault(k => !used[k] && Enumerable.Range(0, 6).All(n => Math.Abs(Box(r[k])[n] - bo[n]) <= 0.01), -1);
                    string more = "";
                    if (near >= 0)
                    {
                        string ea, eb;
                        double ab = CutVolume(o[i], r[near], out ea), ba = CutVolume(r[near], o[i], out eb);
                        more = string.Format("; 包围盒相同的 {0} 体积 {1:F1}, 原有而重建没有 {2:F3} mm³, 重建多出 {3:F3} mm³{4}", r[near].Name, Vol(r[near]), ab, ba, ea ?? eb ?? "");
                    }
                    res.Problems.Add(what + " 在重建零件中没有体积、包围盒相同的实体" + more);
                    continue;
                }
                used[j] = true;
                if (d < 0)
                {
                    if (o[i].GetFaceCount() != r[j].GetFaceCount())
                    { res.Problems.Add(string.Format("{0} 保留原几何, 但面数 {1} ≠ 原 {2}", what, r[j].GetFaceCount(), o[i].GetFaceCount())); continue; }
                    res.Kept++; res.Matched++;
                    continue;
                }
                defUsed[d] = true;
                string e1, e2;
                double x = CutVolume(o[i], r[j], out e1) + CutVolume(r[j], o[i], out e2);
                if (e1 != null || e2 != null) { res.Problems.Add(what + " 布尔比对失败: " + (e1 ?? e2)); continue; }
                res.Rebuilt++; res.Matched++;
                res.MaxXorMm3 = Math.Max(res.MaxXorMm3, x); res.MaxXorRel = Math.Max(res.MaxXorRel, x / vo);
                if (x > 1e-6 * vo) res.Problems.Add(string.Format("{0}: 差集体积 {1:F4} mm³ (形状不同)", what, x));
            }
            int missing = defUsed.Count(u => !u);
            if (missing > 0) res.Problems.Add(missing + " 个重建定义在原几何中没有对应实体");
            for (int k = 0; k < r.Count; k++)
                if (!used[k])
                    res.Problems.Add(string.Format("重建零件中多出的实体 {0} (体积 {1:F1} mm³, 包围盒 {2})", r[k].Name, Vol(r[k]),
                        string.Join(",", Box(r[k]).Select(v => v.ToString("F1")))));
            return res;
        }

        internal static List<Body2> Solids(ModelDoc2 m)
        {
            return ((object[])((PartDoc)m).GetBodies2((int)SolidWorks.Interop.swconst.swBodyType_e.swSolidBody, true) ?? new object[0]).Cast<Body2>().ToList();
        }

        internal static double Vol(Body2 b) { return ((double[])b.GetMassProperties(1.0))[3] / (Sw.MM * Sw.MM * Sw.MM); }
        internal static double[] Box(Body2 b) { return ((double[])b.GetBodyBox()).Select(v => v / Sw.MM).ToArray(); }

        /// <summary>对称差体积 (a−b)+(b−a) (mm³); 布尔运算失败时 err 非空</summary>
        public static double Xor(Body2 a, Body2 b, out string err)
        {
            string e1, e2;
            double x = CutVolume(a, b, out e1) + CutVolume(b, a, out e2);
            err = e1 ?? e2;
            return x;
        }

        /// <summary>a − b (临时实体) 剩下的体积 (mm³)。结果为空 = 0; 两者不相交 = a 的体积; 其余错误返回 err</summary>
        static double CutVolume(Body2 a, Body2 b, out string err)
        {
            err = null;
            var ac = (Body2)a.Copy(); var bc = (Body2)b.Copy();
            int code;
            var outs = ac.Operations2((int)SolidWorks.Interop.swconst.swBodyOperationType_e.SWBODYCUT, bc, out code) as object[];
            if (code == (int)SolidWorks.Interop.swconst.swBodyOperationError_e.swBodyOperationEmptyBody) return 0;
            if (code == (int)SolidWorks.Interop.swconst.swBodyOperationError_e.swBodyOperationDisjointBodies) return Vol(a);
            if (code != 0) { err = (SolidWorks.Interop.swconst.swBodyOperationError_e)code + " (" + code + ")"; return 0; }
            return outs == null ? 0 : outs.Cast<Body2>().Sum(Vol);
        }

        /// <summary>定义的数据校验 (import.json 手工编辑后): 轴、长度、环与段的格式</summary>
        public static string Check(PrismDef p)
        {
            if (p.Axis != "X" && p.Axis != "Y" && p.Axis != "Z") return "轴应为 X / Y / Z";
            if (!(p.To > p.From)) return "to 应大于 from";
            if (p.Loops == null || p.Loops.Count == 0 || !p.Loops.Any(l => l.Outer)) return "缺少外轮廓";
            if (p.Box == null || p.Box.Length != 6) return "缺少原实体包围盒 box (6 个数)";
            foreach (var g in p.Loops.SelectMany(l => l.Segs))
            {
                if (g.T == "L" && (g.P == null || g.P.Length != 4)) return "直线段应有 4 个数";
                if (g.T == "A" && (g.P == null || g.P.Length != 4 || g.C == null || g.C.Length != 2 || (g.Dir != 1 && g.Dir != -1))) return "圆弧段格式不对";
                if (g.T == "O" && (g.C == null || g.C.Length != 2 || !(g.R > 0))) return "整圆格式不对";
                if (g.T != "L" && g.T != "A" && g.T != "O") return "未知段类型 " + g.T;
            }
            foreach (var c in p.Cuts ?? new List<PlaneCut>())
            {
                if (c.N == null || c.N.Length != 3 || c.P == null || c.P.Length != 3) return "斜口应有 n (3 个数) 与 p (3 个数)";
                if (ZeroAxis(c.N) < 0) return "斜口平面须平行于某根坐标轴 (n 恰有一个分量为 0)";
                if (Math.Abs(Math.Sqrt(Dot(c.N, c.N)) - 1) > 1e-6) return "斜口法向 n 须为单位向量";
            }
            foreach (var h in p.Holes ?? new List<HoleCut>())
            {
                if (h.Axis != "X" && h.Axis != "Y" && h.Axis != "Z") return "横孔轴应为 X / Y / Z";
                if (h.Axis == p.Axis) return "横孔轴不能与拉伸轴相同";
                if (h.C == null || h.C.Length != 2 || !(h.D > 0) || !(h.To > h.From)) return "横孔应有 c (2 个数)、d > 0、to > from";
            }
            return null;
        }
    }
}
