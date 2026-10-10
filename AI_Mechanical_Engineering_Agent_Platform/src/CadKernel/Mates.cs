using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 带配合的装配体 (运动机构用): 逐个插入零部件, 以配合约束; 可添加齿轮配合并保留自由度。
    /// 固定布局的大装配体请用 AsmBuild (变换矩阵 + 全部固定, 无配合)。
    /// 来源: 行星齿轮减速器 02_build_assembly.py。实测要点 (见 docs/api_calibration.md):
    ///   * AddComponent5 前被引用零件须已在内存中打开;
    ///   * 齿轮配合是增量约束, 只有"新添加配合"会触发完整求解; 修改已有配合数值或 SetTransformAndSolve2 不能正确驱动;
    ///   * 齿轮配合方向 (Flip) 与实体排序有关, 须用临时角度配合驱动后核对并自动校正。
    /// </summary>
    public sealed class MateAsm
    {
        public readonly string File, Name;
        public readonly ModelDoc2 Doc;
        public readonly AssemblyDoc Asm;

        MateAsm(string file, ModelDoc2 doc)
        {
            File = file; Name = Path.GetFileNameWithoutExtension(file);
            Doc = doc; Asm = (AssemblyDoc)doc;
        }

        /// <summary>新建并先保存装配体 (零部件引用用相对路径); parts = 将要插入的零件 (先打开)</summary>
        public static MateAsm Create(string file, IEnumerable<string> parts)
        {
            foreach (string p in parts) Sw.Open(Cat.EnsureBuilt(p));
            ModelDoc2 md = Sw.NewAsm();
            Sw.Save(md, file);
            return new MateAsm(file, md);
        }

        public Component2 Add(string partPath, Xf xf)
        {
            Component2 c = (Component2)Asm.AddComponent5(partPath, 0, "", false, "", xf.Tx * Sw.MM, xf.Ty * Sw.MM, xf.Tz * Sw.MM);
            if (c == null) throw new Exception("插入零部件失败: " + partPath);
            c.Transform2 = Math(xf);
            return c;
        }

        public static MathTransform Math(Xf xf)
        {
            var a = new double[16];
            xf.Fill(a, 0);
            return (MathTransform)((MathUtility)Sw.App.GetMathUtility()).CreateTransform(a);
        }

        public void Fix(Component2 c)
        {
            Select(new[] { new Sel(c.Name2 + "@" + Name, "COMPONENT") });
            Asm.FixComponent();
            Doc.ClearSelection2(true);
        }

        /// <summary>选择项: 名称 (如 "GearAxis@SunGear-1@总装") + 类型 (AXIS / PLANE / COMPONENT …)</summary>
        public struct Sel
        {
            public string Id, Type;
            public Sel(string id, string type) { Id = id; Type = type; }
        }

        /// <summary>零部件内实体的选择名: 实体@零部件@装配体</summary>
        public Sel In(Component2 c, string entity, string type) { return new Sel(entity + "@" + c.Name2 + "@" + Name, type); }

        void Select(Sel[] items)
        {
            Doc.ClearSelection2(true);
            for (int i = 0; i < items.Length; i++)
                if (!Doc.Extension.SelectByID2(items[i].Id, items[i].Type, 0, 0, 0, i > 0, 1, null, 0))
                    throw new Exception("选择失败: " + items[i].Id + " [" + items[i].Type + "]");
        }

        /// <summary>添加配合并重命名; 错误码非 0 即抛异常</summary>
        public Feature Mate(swMateType_e type, Sel[] items, string name, int align = (int)swMateAlign_e.swMateAlignCLOSEST, bool flip = false,
                            double num = 1, double den = 1, double distMm = 0, double angleRad = 0)
        {
            Select(items);
            int err;
            Asm.AddMate5((int)type, align, flip, distMm * Sw.MM, distMm * Sw.MM, distMm * Sw.MM, num, den,
                         angleRad, angleRad, angleRad, false, false, 0, out err);
            Doc.ClearSelection2(true);
            if (err != (int)swAddMateError_e.swAddMateError_NoError) throw new Exception("配合失败 err=" + err + ": " + name);
            var all = Mates();
            if (all.Count == 0) throw new Exception("配合未生成: " + name);
            Feature last = all[all.Count - 1];
            if (name != null) last.Name = name;
            return last;
        }

        public List<Feature> Mates()
        {
            var r = new List<Feature>();
            Feature f = (Feature)Doc.FirstFeature();
            while (f != null)
            {
                if (f.GetTypeName2() == "MateGroup")
                {
                    Feature s = (Feature)f.GetFirstSubFeature();
                    while (s != null) { r.Add(s); s = (Feature)s.GetNextSubFeature(); }
                }
                f = (Feature)f.GetNextFeature();
            }
            return r;
        }

        public void DeleteMate(string name)
        {
            Feature m = Mates().Find(q => q.Name == name);
            if (m == null) throw new Exception("找不到配合: " + name);
            Doc.ClearSelection2(true);
            m.Select2(false, 0);
            Doc.Extension.DeleteSelection2(0);
            Doc.ClearSelection2(true);
            Doc.EditRebuild3();
        }

        /// <summary>零部件绕全局 Z 的转角 (°) 与原点 (mm)</summary>
        public static double AngleZ(Component2 c, out double[] origin)
        {
            double[] d = (double[])c.Transform2.ArrayData;
            origin = new[] { d[9] / Sw.MM, d[10] / Sw.MM, d[11] / Sw.MM };
            return System.Math.Atan2(d[1], d[0]) * 180 / System.Math.PI;
        }

        public static double AngleZ(Component2 c) { double[] o; return AngleZ(c, out o); }

        /// <summary>(-180, 180]</summary>
        public static double Wrap(double a) { return ((a + 180) % 360 + 360) % 360 - 180; }

        public void Save() { Sw.Save(Doc, File); }

        /// <summary>关闭装配体 (不保存) 及其打开的零件</summary>
        public void Close() { Sw.CloseAll(); }
    }
}
