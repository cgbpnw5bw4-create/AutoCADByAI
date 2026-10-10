using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 刚体变换。SolidWorks MathTransform.ArrayData 约定(已标定, 见 docs/04):
    ///   [0..2]=局部 X 轴在父坐标系中的方向, [3..5]=局部 Y 轴, [6..8]=局部 Z 轴, [9..11]=平移(m), [12]=缩放
    /// </summary>
    public class Xf
    {
        public double[] X = { 1, 0, 0 }, Y = { 0, 1, 0 }, Z = { 0, 0, 1 };
        public double Tx, Ty, Tz;

        public static Xf T(double x, double y, double z) { return new Xf { Tx = x, Ty = y, Tz = z }; }

        /// <summary>局部 Z 轴指向 dir (单位向量, 仅支持坐标轴方向 ±X/±Y/±Z), 原点在 (x,y,z)</summary>
        public static Xf Dir(string dir, double x, double y, double z)
        {
            var f = T(x, y, z);
            switch (dir)
            {
                case "+Z": break;
                case "-Z": f.X = new double[] { 1, 0, 0 }; f.Y = new double[] { 0, -1, 0 }; f.Z = new double[] { 0, 0, -1 }; break;
                case "+X": f.X = new double[] { 0, 0, -1 }; f.Y = new double[] { 0, 1, 0 }; f.Z = new double[] { 1, 0, 0 }; break;
                case "-X": f.X = new double[] { 0, 0, 1 }; f.Y = new double[] { 0, 1, 0 }; f.Z = new double[] { -1, 0, 0 }; break;
                case "+Y": f.X = new double[] { 1, 0, 0 }; f.Y = new double[] { 0, 0, -1 }; f.Z = new double[] { 0, 1, 0 }; break;
                case "-Y": f.X = new double[] { 1, 0, 0 }; f.Y = new double[] { 0, 0, 1 }; f.Z = new double[] { 0, -1, 0 }; break;
                default: throw new Exception("不支持的方向 " + dir);
            }
            return f;
        }

        static double[] V(string d)
        {
            switch (d)
            {
                case "+X": return new double[] { 1, 0, 0 }; case "-X": return new double[] { -1, 0, 0 };
                case "+Y": return new double[] { 0, 1, 0 }; case "-Y": return new double[] { 0, -1, 0 };
                case "+Z": return new double[] { 0, 0, 1 }; case "-Z": return new double[] { 0, 0, -1 };
            }
            throw new Exception("方向 " + d);
        }

        /// <summary>任意坐标轴组合: 局部 Z -> zDir, 局部 Y -> yDir, 局部 X = Y×Z</summary>
        public static Xf Frame(string zDir, string yDir, double x, double y, double z)
        {
            double[] Zv = V(zDir), Yv = V(yDir);
            var f = T(x, y, z);
            f.Z = Zv; f.Y = Yv;
            f.X = new double[] { Yv[1] * Zv[2] - Yv[2] * Zv[1], Yv[2] * Zv[0] - Yv[0] * Zv[2], Yv[0] * Zv[1] - Yv[1] * Zv[0] };
            return f;
        }

        /// <summary>绕 Y 轴旋转 180° (Z->-Z, X->-X)</summary>
        public static Xf FlipY(double x, double y, double z)
        {
            var f = T(x, y, z); f.X = new double[] { -1, 0, 0 }; f.Z = new double[] { 0, 0, -1 }; return f;
        }

        /// <summary>绕 X 轴旋转 180° (Y->-Y, Z->-Z)</summary>
        public static Xf FlipX(double x, double y, double z)
        {
            var f = T(x, y, z); f.Y = new double[] { 0, -1, 0 }; f.Z = new double[] { 0, 0, -1 }; return f;
        }

        /// <summary>绕 Z 轴旋转任意角(度)</summary>
        public static Xf RotZ(double deg, double x, double y, double z)
        {
            double a = deg * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a);
            var f = T(x, y, z); f.X = new double[] { c, s, 0 }; f.Y = new double[] { -s, c, 0 }; return f;
        }

        /// <summary>复合: 先本变换, 再父变换 (返回全局)</summary>
        public Xf In(Xf p)
        {
            var r = new Xf();
            r.X = p.Vec(X); r.Y = p.Vec(Y); r.Z = p.Vec(Z);
            double[] t = p.Pt(new double[] { Tx, Ty, Tz });
            r.Tx = t[0]; r.Ty = t[1]; r.Tz = t[2];
            return r;
        }

        public double[] Vec(double[] v)
        {
            return new double[] { v[0] * X[0] + v[1] * Y[0] + v[2] * Z[0], v[0] * X[1] + v[1] * Y[1] + v[2] * Z[1], v[0] * X[2] + v[1] * Y[2] + v[2] * Z[2] };
        }

        public double[] Pt(double[] p)
        {
            double[] v = Vec(p); return new double[] { v[0] + Tx, v[1] + Ty, v[2] + Tz };
        }

        public void Fill(double[] a, int o)
        {
            for (int i = 0; i < 3; i++) { a[o + i] = X[i]; a[o + 3 + i] = Y[i]; a[o + 6 + i] = Z[i]; }
            a[o + 9] = Tx * Sw.MM; a[o + 10] = Ty * Sw.MM; a[o + 11] = Tz * Sw.MM; a[o + 12] = 1;
        }
    }

    /// <summary>装配体构建器: 先收集 (路径, 变换), 最后一次性 AddComponents3 批量插入 —— 无需预先打开被引用文档</summary>
    public class AsmBuild
    {
        public readonly string File;
        readonly List<string> paths = new List<string>();
        readonly List<Xf> xfs = new List<Xf>();
        public int Count { get { return paths.Count; } }

        public AsmBuild(string file) { File = file; }

        public void Add(string path, Xf xf) { paths.Add(path); xfs.Add(xf); }
        public void Add(string path, double x, double y, double z) { Add(path, Xf.T(x, y, z)); }

        /// <summary>螺栓-螺母组: p=头侧被夹紧面上的点, dir=指向被连接件内部</summary>
        public void BoltNut(int d, double grip, string dir, double x, double y, double z, bool carbon = false)
        { Add(Std.BoltNut(d, grip, carbon), Xf.Dir(dir, x, y, z)); }

        /// <summary>螺钉拧入螺纹孔: tapDepth = 可用螺纹深度 (含允许的穿出量)</summary>
        public void BoltTap(int d, double grip, double tapDepth, string dir, double x, double y, double z, bool carbon = false)
        { Add(Std.BoltTap(d, grip, tapDepth, carbon), Xf.Dir(dir, x, y, z)); }

        public string Build(bool log = true)
        {
            foreach (string p in paths) Cat.EnsureBuilt(p);   // 按需模式: 先把用到的零件建出来, AddComponents3 要求文件已存在
            ModelDoc2 md = Sw.NewAsm();
            Sw.Save(md, File);                 // 先存盘, 零部件引用使用相对路径
            AssemblyDoc a = (AssemblyDoc)md;
            const int BATCH = 400;
            for (int s = 0; s < paths.Count; s += BATCH)
            {
                int n = Math.Min(BATCH, paths.Count - s);
                var names = new string[n]; var cfg = new string[n]; var arr = new double[16 * n];
                for (int i = 0; i < n; i++) { names[i] = paths[s + i]; cfg[i] = ""; xfs[s + i].Fill(arr, 16 * i); }
                object[] cs = (object[])a.AddComponents3(names, arr, cfg);
                int got = cs == null ? 0 : cs.Length;
                if (got != n) throw new Exception("AddComponents3 插入数量不符 " + got + "/" + n + " : " + Path.GetFileName(File));
            }
            // 全部固定 (程序化布局用变换矩阵定位, 不使用配合, 无过定义)
            md.ClearSelection2(true);
            object[] comps = (object[])a.GetComponents(true);
            if (comps != null)
            {
                foreach (object o in comps) ((Component2)o).Select4(true, null, false);
                a.FixComponent();
            }
            md.ClearSelection2(true);
            // 只重建顶层 (true): 整体重建 (false) 会把被引用零件在内存中一并重建, 刷新其重建戳而零件文件未存,
            // 装配体再打开时报 NeedsRegen (CFC-650 末次完整性检查 9 条均为此; 平台回归 M03 复现 8 条)。
            // 零件在 Cat.Make 中已重建并保存, 固定布局装配无配合需要求解。
            md.ForceRebuild3(true);
            md.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
            md.ViewZoomtofit2();
            Sw.Save(md, File);
            if (log) Sw.Log(string.Format("{0,-44} 顶层零部件 {1,4}", Path.GetFileName(File), paths.Count));
            Sw.Close(md);
            return File;
        }
    }
}
