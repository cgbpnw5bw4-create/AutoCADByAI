using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SolidWorks.Interop.sldworks;

namespace CadKernel
{
    /// <summary>
    /// 标准紧固件 (GB) —— 螺纹按公称直径光杆简化表达 (GB/T 4459.1 简化画法), 不建螺纹牙型。
    /// 局部坐标: +Z = 螺栓轴线指向被连接件内部; z=0 为螺栓头侧第一个被夹紧面。
    /// </summary>
    public static class Std
    {
        const string D = @"00_标准件";

        // d -> {s 对边, k 头高, m 螺母高, 垫圈 d1, d2, h, 弹垫 s, 螺距 P}
        static readonly Dictionary<int, double[]> T = new Dictionary<int, double[]> {
            { 5,  new double[]{ 8,   3.5, 4.7,  5.3, 10, 1.0, 1.3, 0.8 } },
            { 6,  new double[]{ 10,  4.0, 5.2,  6.4, 12, 1.6, 1.6, 1.0 } },
            { 8,  new double[]{ 13,  5.3, 6.8,  8.4, 16, 1.6, 2.1, 1.25 } },
            { 10, new double[]{ 16,  6.4, 8.4, 10.5, 20, 2.0, 2.6, 1.5 } },
            { 12, new double[]{ 18,  7.5, 10.8, 13,  24, 2.5, 3.1, 1.75 } },
            { 16, new double[]{ 24, 10.0, 14.8, 17,  30, 3.0, 4.1, 2.0 } },
            { 20, new double[]{ 30, 12.5, 18.0, 21,  37, 3.0, 5.0, 2.5 } },
        };
        static readonly int[] LEN = { 10, 12, 16, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 180, 200 };

        /// <summary>全部紧固件按不锈钢 A2-70 (湿区防锈); 机架地脚/机架拼接用 8.8 级发黑</summary>
        static Mat MatOf(bool carbon) { return carbon ? Mat.ST_88 : Mat.ST_A2; }
        static string Grade(bool carbon) { return carbon ? "8.8 级 发黑" : "A2-70"; }

        public static int StdLen(double need)
        {
            foreach (int l in LEN) if (l >= need - 0.01) return l;
            throw new Exception("螺栓长度超出标准系列: " + need);
        }

        public static string Bolt(int d, int L, bool carbon = false)
        {
            double[] t = T[d];
            string id = "GB5783_M" + d + "x" + L + (carbon ? "_C" : "");
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 5783", Name = "六角头螺栓 M" + d + "×" + L + (carbon ? " 8.8" : ""),
                Spec = "M" + d + "×" + L + " " + Grade(carbon), Mat = MatOf(carbon), Color = carbon ? Col.Black : Col.Zinc },
                m => {
                    Sk.XY(m, -t[1]).Hex(0, 0, t[0]).Ext(t[1], 1);
                    Sk.XY(m, 0).Circ(0, 0, d).Ext(L, 1);
                });
        }

        public static string Nut(int d, bool carbon = false)
        {
            double[] t = T[d];
            string id = "GB6170_M" + d + (carbon ? "_C" : "");
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 6170", Name = "六角螺母 M" + d + (carbon ? " 8级" : ""),
                Spec = "M" + d + " " + (carbon ? "8 级 发黑" : "A2-70"), Mat = MatOf(carbon), Color = carbon ? Col.Black : Col.Zinc },
                m => Sk.XY(m, 0).Hex(0, 0, t[0]).Circ(0, 0, d).Ext(t[2], 1));
        }

        public static string Washer(int d, bool carbon = false)
        {
            double[] t = T[d];
            string id = "GB97_" + d + (carbon ? "_C" : "");
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 97.1", Name = "平垫圈 " + d + (carbon ? " 钢" : ""),
                Spec = d + " " + (carbon ? "200HV 发黑" : "A2"), Mat = MatOf(carbon), Color = carbon ? Col.Black : Col.Zinc },
                m => Sk.XY(m, 0).Circ(0, 0, t[4]).Circ(0, 0, t[3]).Ext(t[5], 1));
        }

        public static string Spring(int d, bool carbon = false)
        {
            double[] t = T[d];
            string id = "GB93_" + d + (carbon ? "_C" : "");
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 93", Name = "弹簧垫圈 " + d + (carbon ? " 钢" : ""),
                Spec = d + " " + (carbon ? "65Mn 发黑" : "A2"), Mat = MatOf(carbon), Color = carbon ? Col.Black : Col.Zinc },
                m => Sk.XY(m, 0).Circ(0, 0, d + 0.2 + 2 * t[6]).Circ(0, 0, d + 0.2).Ext(t[6], 1));
        }

        // 内六角圆柱头螺钉 GB/T 70.1: d -> {dk, k}
        static readonly Dictionary<int, double[]> SH = new Dictionary<int, double[]> {
            { 5, new double[]{ 8.5, 5 } }, { 6, new double[]{ 10, 6 } }, { 8, new double[]{ 13, 8 } },
            { 10, new double[]{ 16, 10 } }, { 12, new double[]{ 18, 12 } }, { 16, new double[]{ 24, 16 } } };

        public static string Socket(int d, int L)
        {
            double[] t = SH[d];
            string id = "GB70_M" + d + "x" + L;
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 70.1", Name = "内六角圆柱头螺钉 M" + d + "×" + L,
                Spec = "M" + d + "×" + L + " 12.9 级 发黑", Mat = Mat.ST_88, Color = Col.Black },
                m => {
                    Sk.XY(m, -t[1]).Circ(0, 0, t[0]).Ext(t[1], 1);
                    Sk.XY(m, 0).Circ(0, 0, d).Ext(L, 1);
                });
        }

        /// <summary>拉铆螺母 (沉头, 用于方管单面连接): 套筒 外径 d+3, 长 13, 装于管壁 φ(d+3) 孔内</summary>
        public static string RivNut(int d)
        {
            string id = "GB17880_M" + d;
            return Cat.Make(new Part {
                Id = id, Dir = D, Kind = Kind.Std, Code = "GB/T 17880.2", Name = "沉头拉铆螺母 M" + d,
                Spec = "M" + d + " 碳钢镀锌, 适用板厚 1.5~4", Mat = Mat.STEEL, Color = Col.Zinc },
                m => Sk.XY(m, 0).Circ(0, 0, d + 3).Circ(0, 0, d).Ext(13, 1));
        }

        // ================= 紧固件组 (子装配) =================
        static readonly Dictionary<string, string> Sets = new Dictionary<string, string>();

        /// <summary>清空紧固件组缓存 (缓存的是上一个运行目录里的子装配路径)</summary>
        public static void Reset() { Sets.Clear(); }

        /// <summary>选长与组标识的纯定义, 生产建模和缓存共用; 不连接 SolidWorks。</summary>
        internal sealed class SetDefinition
        {
            public int Length;
            public string Key, FileName;
        }

        static SetDefinition DefineSet(bool tap, int d, int length, double grip, bool carbon)
        {
            // 整数夹厚保留原文件名的 .0; 其余保留 double 的完整精度, 不再把不同夹厚舍入到同一组。
            string g = grip.ToString(grip == Math.Truncate(grip) ? "F1" : "R", CultureInfo.InvariantCulture);
            return new SetDefinition
            {
                Length = length,
                Key = string.Format(CultureInfo.InvariantCulture, "{0}_M{1}{2}_G{3}{4}", tap ? "FT" : "FS", d,
                    tap ? "x" + length.ToString(CultureInfo.InvariantCulture) : "", g, carbon ? "_C" : ""),
                FileName = string.Format(CultureInfo.InvariantCulture, "{0}_M{1}x{2}_夹厚{3}{4}.SLDASM",
                    tap ? "螺钉组" : "螺栓组", d, length, g, carbon ? "_8.8" : ""),
            };
        }

        internal static SetDefinition DefineBoltNut(int d, double grip, bool carbon = false)
        {
            double[] t = T[d];
            return DefineSet(false, d, StdLen(t[5] + grip + t[5] + t[6] + t[2] + 2 * t[7]), grip, carbon);
        }

        internal static SetDefinition DefineBoltTap(int d, double grip, double tapDepth, bool carbon = false)
        {
            double[] t = T[d];
            double b0 = t[6] + t[5] + grip, emin = Math.Min(d, tapDepth - 0.5), emax = tapDepth - 0.5;
            foreach (int l in LEN)
                if (l >= b0 + emin - 0.01 && l <= b0 + emax + 0.01) return DefineSet(true, d, l, grip, carbon);
            throw new Exception(string.Format("M{0} 夹厚 {1} 孔深 {2}: 无合适标准长度", d, grip, tapDepth));
        }

        internal static string BuildSet(SetDefinition definition, Action<string> build)
        {
            string cached;
            if (Sets.TryGetValue(definition.Key, out cached)) return cached;
            string file = Path.Combine(Run.Models, D, "紧固件组", definition.FileName);
            if (!File.Exists(file) || !Cat.Reg.ContainsKey(definition.Key)) build(file);
            Cat.Reg[definition.Key] = file; Sets[definition.Key] = file;
            return file;
        }

        /// <summary>
        /// 螺栓-螺母组: 螺栓 + 平垫 | 夹紧厚度 grip | 平垫 + 弹垫 + 螺母。返回子装配路径。
        /// 局部: z=0 为螺栓头侧被夹紧面, 被夹紧件占 z∈[0, grip]。
        /// </summary>
        public static string BoltNut(int d, double grip, bool carbon = false)
        {
            double[] t = T[d];
            var definition = DefineBoltNut(d, grip, carbon);
            return BuildSet(definition, file =>
            {
                string pb = Bolt(d, definition.Length, carbon), pn = Nut(d, carbon), pw = Washer(d, carbon), ps = Spring(d, carbon);
                var a = new AsmBuild(file);
                a.Add(pw, Xf.T(0, 0, -t[5]));
                a.Add(pb, Xf.T(0, 0, -t[5]));
                a.Add(pw, Xf.T(0, 0, grip));
                a.Add(ps, Xf.T(0, 0, grip + t[5]));
                a.Add(pn, Xf.T(0, 0, grip + t[5] + t[6]));
                a.Build(false);
            });
        }

        /// <summary>
        /// 螺钉组 (拧入螺纹孔/拉铆螺母): 弹垫 + 平垫 | grip | 旋入 e。
        /// 长度取标准系列中满足 1.0d ≤ e ≤ 螺纹孔深-0.5 的最短者 (首轮干涉检查发现"取下一档长度"会顶穿孔底)。
        /// </summary>
        public static string BoltTap(int d, double grip, double tapDepth, bool carbon = false)
        {
            double[] t = T[d];
            var definition = DefineBoltTap(d, grip, tapDepth, carbon);
            return BuildSet(definition, file =>
            {
                string pb = Bolt(d, definition.Length, carbon), pw = Washer(d, carbon), ps = Spring(d, carbon);
                var a = new AsmBuild(file);
                a.Add(pw, Xf.T(0, 0, -t[5]));
                a.Add(ps, Xf.T(0, 0, -t[5] - t[6]));
                a.Add(pb, Xf.T(0, 0, -t[5] - t[6]));
                a.Build(false);
            });
        }

        public static double HeadK(int d) { return T[d][1]; }
        public static double WasherD(int d) { return T[d][4]; }
        public static double NutS(int d) { return T[d][0]; }
    }
}
