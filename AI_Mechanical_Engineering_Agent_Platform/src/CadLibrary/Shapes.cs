using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using CadKernel;

namespace CadLibrary
{
    /// <summary>
    /// 细节造型工具: 特征着色、电机(散热筋/风罩/接线盒/底脚)、法兰、螺栓头阵列。
    /// 来源: CFC-650 铜箔清洗机 V2 (外购件电机、泵、风机外形), 已在该设备 238 个零件中实测。
    /// </summary>
    public static class Dt
    {
        public static readonly double[] C_CAST = Col.Cast;          // 铸件灰
        public static readonly double[] C_BASE = Col.Base;          // 底座黑
        public static readonly double[] C_RAL7035 = Col.Ral7035;    // 柜体浅灰
        public static readonly double[] C_ZINC = { 0.72, 0.73, 0.75 };

        public static Feature Paint(Feature f, double[] c)
        {
            if (f != null)
                f.SetMaterialPropertyValues2(new double[] { c[0], c[1], c[2], 1, 1, 0.45, 0.35, 0, 0 },
                    (int)swInConfigurationOpts_e.swThisConfiguration, null);
            return f;
        }

        /// <summary>
        /// 卧式电机, 轴线 // Z, 轴心 (cx,cy)。zD = 轴伸端端盖面, zF = 风罩末端 (可 zF&lt;zD)。
        /// 机座 φD 带 n 条散热筋 (避开顶部接线盒和底脚), 风罩带通风槽, 顶部接线盒 + 电缆格兰, 底部双底脚落在 footY。
        /// </summary>
        public static void Motor(ModelDoc2 m, double cx, double cy, double zD, double zF, double D, double[] col,
                                 double footY = double.NaN, bool box = true, bool shield = true)
        {
            double sg = Math.Sign(zF - zD), L = Math.Abs(zF - zD), fanL = Math.Max(0.28 * D, 50), fh = 0.06 * D;
            double z0 = zD, z1 = zF - sg * fanL;                        // 机座段
            Paint(Sk.XY(m, z0).Circ(cx, cy, D).Ext(Math.Abs(z1 - z0), (int)sg), col);
            if (shield) Paint(Sk.XY(m, z0).Circ(cx, cy, 0.86 * D).Ext(12, -(int)sg), col);    // 端盖止口
            // 散热筋
            var s = Sk.XY(m, z0 + sg * 18);
            int n = 20;
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                double deg = (a * 180 / Math.PI) % 360;
                if (Math.Abs(deg - 90) < 25) continue;                                            // 接线盒
                if (!double.IsNaN(footY) && Math.Abs(deg - 270) < 40) continue;                  // 底脚
                double r0 = D / 2 - 2, r1 = D / 2 + fh, t = 1.6;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                s.Poly(cx + r0 * ca - t * sa, cy + r0 * sa + t * ca, cx + r1 * ca - t * sa, cy + r1 * sa + t * ca,
                       cx + r1 * ca + t * sa, cy + r1 * sa - t * ca, cx + r0 * ca + t * sa, cy + r0 * sa - t * ca);
            }
            Paint(s.Ext(Math.Abs(z1 - z0) - 30, (int)sg), col);
            // 风罩 + 通风槽
            double Df = D + 2 * fh - 4;
            Paint(Sk.XY(m, z1).Circ(cx, cy, Df).Ext(fanL, (int)sg), col);
            var v = Sk.XY(m, zF);
            for (int i = -2; i <= 2; i++) v.RectC(cx + i * Df * 0.14, cy, Df * 0.06, Df * 0.55);
            v.Cut(3, -(int)sg);
            // 接线盒 + 格兰
            if (box)
            {
                double bw = 0.52 * D, bh = 0.26 * D, bl = Math.Min(0.45 * L, 0.55 * D), bz = z0 + sg * (0.35 * L);
                Paint(Sk.XZ(m, cy + D / 2 - 6).Rect(cx - bw / 2, bz - bl / 2, cx + bw / 2, bz + bl / 2).Ext(bh + 6, 1), col);
                Paint(Sk.YZ(m, cx + bw / 2).Circ(bz, cy + D / 2 + bh * 0.45, 0.1 * D + 6).Ext(0.08 * D + 6, 1), Dt.C_BASE);
            }
            // 底脚
            if (!double.IsNaN(footY))
            {
                double fw = 0.40 * D, fl = Math.Max(0.12 * L, 30), top = cy - 0.30 * D;
                foreach (double zc in new[] { z0 + sg * (0.15 * L), z1 - sg * (0.12 * L) })
                {
                    Paint(Sk.XZ(m, footY).Rect(cx - fw - 12, zc - fl / 2, cx + fw + 12, zc + fl / 2).Ext(12, 1), col);
                    Paint(Sk.XZ(m, footY + 12).Rect(cx - fw, zc - fl / 2, cx + fw, zc + fl / 2).Ext(top - footY - 12, 1), col);
                }
            }
        }

        /// <summary>法兰 (轴线 // 给定平面法向): 外径 Df, 厚 t, n 孔 φdh 于 PCD</summary>
        public static void FlangeXY(ModelDoc2 m, double cx, double cy, double z, double Df, double t, int dir, int nh, double pcd, double dh, double bore, double[] col)
        {
            Paint(Sk.XY(m, z).Circ(cx, cy, Df).Ext(t, dir), col);
            var s = Sk.XY(m, z);
            for (int i = 0; i < nh; i++) { double a = Math.PI / nh + 2 * Math.PI * i / nh; s.Circ(cx + pcd / 2 * Math.Cos(a), cy + pcd / 2 * Math.Sin(a), dh); }
            if (bore > 0) s.Circ(cx, cy, bore);
            s.Cut(t, dir);
        }

        /// <summary>圆周螺栓头 (六角, 对边 s, 高 k), 位于 XY 面 z, 朝 dir</summary>
        public static void BoltRing(ModelDoc2 m, double cx, double cy, double z, int n, double pcd, double sflat, double k, int dir, double phase = 0)
        {
            var s = Sk.XY(m, z);
            for (int i = 0; i < n; i++) { double a = phase + 2 * Math.PI * i / n; s.Hex(cx + pcd / 2 * Math.Cos(a), cy + pcd / 2 * Math.Sin(a), sflat); }
            Paint(s.Ext(k, dir), C_ZINC);
        }
    }
}
