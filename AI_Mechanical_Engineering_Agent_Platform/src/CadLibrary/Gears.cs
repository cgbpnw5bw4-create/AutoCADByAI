using System;
using System.Collections.Generic;
using CadKernel;

namespace CadLibrary
{
    /// <summary>齿廓线段: 直线 / 圆弧 (圆心、起点、终点、旋向 +1 逆时针 -1 顺时针) / 过点样条。单位 mm。</summary>
    public sealed class GearSeg
    {
        public char Kind;                 // 'L' 直线, 'A' 圆弧, 'S' 样条
        public double[] P1, P2, C;        // 直线 P1→P2; 圆弧 C 圆心, P1 起点, P2 终点
        public int Dir;                   // 圆弧旋向
        public List<double[]> Pts;        // 样条点

        public double[] Start { get { return Kind == 'S' ? Pts[0] : P1; } }
        public double[] End { get { return Kind == 'S' ? Pts[Pts.Count - 1] : P2; } }
    }

    public sealed class GearInfo
    {
        public int Z; public double R, Rb, Ra, Rf;   // 齿数, 分度圆/基圆/齿顶圆/齿根圆半径 (内齿轮 Ra 为内侧齿顶)
    }

    /// <summary>
    /// 渐开线圆柱直齿轮齿廓 (2D, 齿轮轴线 = Z, 原点为轮心)。
    /// 来源: 行星齿轮减速器项目 gear_geom.py (2026-09-24 实测: 啮合/干涉校验、SolidWorks 齿轮配合运动学残差 &lt; 5e-5°)。
    /// 本类逐式移植, 由 CadTests 对照原 Python 输出逐段校验。
    /// 侧隙按分度圆弧长由两轮各减薄/加宽 j_t/2; 外齿轮齿根过渡为半径 rho·m 的圆弧 (近似, 非展成曲线)。
    /// </summary>
    public static class Gears
    {
        public const double Alpha20 = Math.PI / 9;   // 20°
        /// <summary>每侧渐开线采样点数 (CAD 样条插值点)</summary>
        public static int InvolutePoints = 12;

        static double Inv(double a) { return Math.Tan(a) - a; }
        static double[] Polar(double r, double th) { return new[] { r * Math.Cos(th), r * Math.Sin(th) }; }

        /// <summary>渐开线在半径 r 处相对齿(槽)中心线的半角</summary>
        static double HalfAngle(double r, double rb, double psi, double alpha)
        {
            double ar = Math.Acos(Math.Min(1.0, rb / r));
            return psi + Inv(alpha) - Inv(ar);
        }

        static List<double[]> InvolutePts(double rb, double r0, double r1, double center, int side, double psi, double alpha)
        {
            int n = InvolutePoints;
            double t0 = Math.Sqrt(Math.Max(0.0, (r0 / rb) * (r0 / rb) - 1));
            double t1 = Math.Sqrt(Math.Max(0.0, (r1 / rb) * (r1 / rb) - 1));
            var pts = new List<double[]>();
            for (int i = 0; i < n; i++)
            {
                double t = t0 + (t1 - t0) * i / (n - 1);
                double r = rb * Math.Sqrt(1 + t * t);
                pts.Add(Polar(r, center + side * HalfAngle(r, rb, psi, alpha)));
            }
            return pts;
        }

        static GearSeg Line(double[] a, double[] b) { return new GearSeg { Kind = 'L', P1 = a, P2 = b }; }
        static GearSeg Arc(double[] c, double[] a, double[] b, int dir) { return new GearSeg { Kind = 'A', C = c, P1 = a, P2 = b, Dir = dir }; }
        static GearSeg Spline(List<double[]> pts) { return new GearSeg { Kind = 'S', Pts = pts }; }

        /// <summary>外齿轮完整闭合齿廓。齿中心位于 phase + k·2π/z</summary>
        public static List<GearSeg> External(int z, double m, out GearInfo info, double alpha = Alpha20, double backlash = 0.10,
                                            double ha = 1.0, double hf = 1.25, double rho = 0.38, double phase = 0.0)
        {
            double r = m * z / 2, rb = r * Math.Cos(alpha), ra = r + ha * m, rf = r - hf * m;
            double psi = (Math.PI * m / 2 - backlash / 2) / (2 * r);
            double pitch = 2 * Math.PI / z;
            rho = rho * m;
            var segs = new List<GearSeg>();
            for (int k = 0; k < z; k++)
            {
                double th = phase + k * pitch, thNext = th + pitch;
                if (rf < rb)
                {
                    double t = Math.Sqrt((rf + rho) * (rf + rho) - rho * rho);   // 圆角与径向线切点半径
                    if (!(t < rb)) throw new ArgumentException("齿根圆角与基圆冲突 (t ≥ rb)");
                    double bR = HalfAngle(rb, rb, psi, alpha);
                    double thR = th - bR, thL = th + bR;
                    // 右侧圆角 (位于齿槽内, 顺时针绕圆心)
                    double[] u = { Math.Cos(thR), Math.Sin(thR) }, n_ = { Math.Sin(thR), -Math.Cos(thR) };
                    double[] c = { t * u[0] + rho * n_[0], t * u[1] + rho * n_[1] };
                    double[] T1 = { t * u[0], t * u[1] };
                    double s = rf / (rf + rho);
                    double[] T0 = { c[0] * s, c[1] * s };
                    // 左侧圆角
                    double[] uL = { Math.Cos(thL), Math.Sin(thL) }, nL = { -Math.Sin(thL), Math.Cos(thL) };
                    double[] cL = { t * uL[0] + rho * nL[0], t * uL[1] + rho * nL[1] };
                    double[] T1L = { t * uL[0], t * uL[1] };
                    double[] T0L = { cL[0] * s, cL[1] * s };
                    segs.Add(Arc(c, T0, T1, -1));
                    segs.Add(Line(T1, Polar(rb, thR)));
                    segs.Add(Spline(InvolutePts(rb, rb, ra, th, -1, psi, alpha)));
                    segs.Add(Arc(new double[] { 0, 0 }, Polar(ra, th - HalfAngle(ra, rb, psi, alpha)), Polar(ra, th + HalfAngle(ra, rb, psi, alpha)), 1));
                    segs.Add(Spline(InvolutePts(rb, ra, rb, th, +1, psi, alpha)));
                    segs.Add(Line(Polar(rb, thL), T1L));
                    segs.Add(Arc(cL, T1L, T0L, -1));
                    // 齿根圆弧连到下一个齿的右侧圆角起点
                    double[] un = { Math.Cos(thNext - bR), Math.Sin(thNext - bR) }, nn = { Math.Sin(thNext - bR), -Math.Cos(thNext - bR) };
                    double[] cn = { t * un[0] + rho * nn[0], t * un[1] + rho * nn[1] };
                    double[] T0n = { cn[0] * s, cn[1] * s };
                    segs.Add(Arc(new double[] { 0, 0 }, T0L, T0n, 1));
                }
                else
                {
                    segs.Add(Spline(InvolutePts(rb, rf, ra, th, -1, psi, alpha)));
                    segs.Add(Arc(new double[] { 0, 0 }, Polar(ra, th - HalfAngle(ra, rb, psi, alpha)), Polar(ra, th + HalfAngle(ra, rb, psi, alpha)), 1));
                    segs.Add(Spline(InvolutePts(rb, ra, rf, th, +1, psi, alpha)));
                    double b = HalfAngle(rf, rb, psi, alpha);
                    segs.Add(Arc(new double[] { 0, 0 }, Polar(rf, th + b), Polar(rf, thNext - b), 1));
                }
            }
            info = new GearInfo { Z = z, R = r, Rb = rb, Ra = ra, Rf = rf };
            return segs;
        }

        /// <summary>内齿圈的内侧闭合齿廓。齿槽中心位于 phase + k·2π/z</summary>
        public static List<GearSeg> Internal(int z, double m, out GearInfo info, double alpha = Alpha20, double backlash = 0.10,
                                            double ha = 1.0, double hf = 1.25, double phase = 0.0)
        {
            double r = m * z / 2, rb = r * Math.Cos(alpha);
            double rLo = r - ha * m;      // 齿顶圆 (内)
            double rHi = r + hf * m;      // 齿根圆 (外)
            if (rLo < rb) throw new ArgumentException("内齿轮齿顶圆小于基圆");
            double psi = (Math.PI * m / 2 + backlash / 2) / (2 * r);   // 齿槽半角
            double pitch = 2 * Math.PI / z;
            var segs = new List<GearSeg>();
            for (int k = 0; k < z; k++)
            {
                double th = phase + k * pitch;
                double bLo = HalfAngle(rLo, rb, psi, alpha), bHi = HalfAngle(rHi, rb, psi, alpha);
                segs.Add(Spline(InvolutePts(rb, rLo, rHi, th, -1, psi, alpha)));
                segs.Add(Arc(new double[] { 0, 0 }, Polar(rHi, th - bHi), Polar(rHi, th + bHi), 1));
                segs.Add(Spline(InvolutePts(rb, rHi, rLo, th, +1, psi, alpha)));
                segs.Add(Arc(new double[] { 0, 0 }, Polar(rLo, th + bLo), Polar(rLo, th + pitch - bLo), 1));
            }
            info = new GearInfo { Z = z, R = r, Rb = rb, Ra = rLo, Rf = rHi };
            return segs;
        }

        /// <summary>首尾相连检查, 返回最大间隙 (mm)</summary>
        public static double MaxGap(List<GearSeg> segs)
        {
            double gap = 0;
            for (int i = 0; i < segs.Count; i++)
            {
                double[] e = segs[i].End, b = segs[(i + 1) % segs.Count].Start;
                gap = Math.Max(gap, Math.Sqrt((e[0] - b[0]) * (e[0] - b[0]) + (e[1] - b[1]) * (e[1] - b[1])));
            }
            return gap;
        }

        /// <summary>在草图中原样绘制齿廓 (不闭合草图, 调用方继续 Ext/Cut)</summary>
        public static Sk Draw(Sk s, IEnumerable<GearSeg> segs)
        {
            foreach (var g in segs)
            {
                if (g.Kind == 'L') s.Line(g.P1[0], g.P1[1], g.P2[0], g.P2[1]);
                else if (g.Kind == 'A') s.ArcDir(g.C[0], g.C[1], g.P1[0], g.P1[1], g.P2[0], g.P2[1], g.Dir);
                else s.Spline(g.Pts);
            }
            return s;
        }

        /// <summary>
        /// 2K-H 行星轮系 (齿圈固定) 的几何条件: 同心 zr = zs + 2zp; 装配 (zs + zr) / n 为整数。
        /// 返回不满足项 (空 = 满足)。传动比 i = 1 + zr/zs。
        /// </summary>
        public static List<string> PlanetaryConditions(int zs, int zp, int zr, int n)
        {
            var r = new List<string>();
            if (zs < 12 || zp < 12) r.Add("齿数过少 (外齿轮 z ≥ 12, 否则根切)");
            if (zr != zs + 2 * zp) r.Add(string.Format("同心条件不满足: zr={0} ≠ zs+2zp={1}", zr, zs + 2 * zp));
            if (n < 2 || (zs + zr) % n != 0) r.Add(string.Format("装配条件不满足: (zs+zr)/n = {0}/{1} 不是整数", zs + zr, n));
            // 邻接条件: 相邻行星轮齿顶圆不相碰 (按 ha=1)
            double a = (zs + zp) / 2.0, dap = zp + 2.0;     // 以模数为单位
            if (n >= 2 && 2 * a * Math.Sin(Math.PI / n) <= dap) r.Add("邻接条件不满足: 相邻行星轮齿顶相碰");
            foreach (int z in new[] { zs, zp }) { string e = ExternalApplicable(z); if (e != null) r.Add(e); }
            return r;
        }

        /// <summary>
        /// 本齿廓算法的适用性: 齿根圆小于基圆但相差不足以容纳 0.38m 过渡圆角时 (α=20°, hf=1.25: z=30~41), 圆角与基圆处的径向线无法相切, 不能生成。
        /// 返回 null = 适用; 否则为原因。(原 Python 版本有同样的限制, 以断言报错)
        /// </summary>
        public static string ExternalApplicable(int z, double alpha = Alpha20, double hf = 1.25, double rho = 0.38)
        {
            try { GearInfo gi; External(z, 1.0, out gi, alpha, 0, 1.0, hf, rho); return null; }
            catch (ArgumentException) { return string.Format("z={0}: 齿根过渡圆角与基圆冲突, 本齿廓算法不适用 (α=20° 时请避开 z=30~41, 或改用展成齿根)", z); }
        }
    }
}
