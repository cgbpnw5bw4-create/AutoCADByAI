using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 基准轴与圆角等补充特征。来源: 行星齿轮减速器 sw_common.py (实测可用), 改为早期绑定。
    /// 与 Sk 相同的原则: 特征 API 失败时不抛异常, 因此每个特征后读回类型或体积变化。
    /// </summary>
    public static class Features
    {
        const double MM = Sw.MM;

        /// <summary>两基准面相交生成基准轴 (按特征树顺序 0=前视 1=上视 2=右视, 与模板语言无关)</summary>
        public static Feature AxisFromPlanes(ModelDoc2 m, int p1, int p2, string name)
        {
            var pl = Sw.Planes(m);
            m.ClearSelection2(true);
            pl[p1].Select2(false, 0);
            pl[p2].Select2(true, 0);
            bool ok = m.InsertAxis2(true);
            return Named(m, ok, name);
        }

        /// <summary>选取过点 (mm) 的圆柱面生成基准轴。取点应避开圆柱面的 0° 接缝</summary>
        public static Feature AxisFromFace(ModelDoc2 m, double x, double y, double z, string name)
        {
            m.ClearSelection2(true);
            bool sel = m.Extension.SelectByID2("", "FACE", x * MM, y * MM, z * MM, false, 0, null, 0);
            bool ok = sel && m.InsertAxis2(true);
            return Named(m, ok, name);
        }

        /// <summary>
        /// 由圆柱面生成基准轴: 按圆柱参数精确查找 (轴线平行于全局 axis 'X'/'Y'/'Z', 轴线过 (u,v), 半径 r, mm), 不依赖视图与拾取点。
        /// 按坐标点选 (AxisFromFace) 的结果受当前视图方向影响, 点恰在圆柱面上时可能选到端面或什么都不选。
        /// </summary>
        public static Feature AxisFromCylinder(ModelDoc2 m, char axis, double u, double v, double r, string name, double tol = 0.01)
        {
            int a = axis == 'X' ? 0 : axis == 'Y' ? 1 : 2, iu = (a + 1) % 3, iv = (a + 2) % 3;
            Face2 hit = null;
            foreach (object bo in (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true))
                foreach (object fo in (object[])((Body2)bo).GetFaces())
                {
                    Surface s = (Surface)((Face2)fo).GetSurface();
                    if (!s.IsCylinder()) continue;
                    double[] p = (double[])s.CylinderParams;   // 原点 xyz, 轴向 xyz, 半径 (m)
                    if (Math.Abs(Math.Abs(p[3 + a]) - 1) > 1e-6) continue;
                    double[] uv = axis == 'X' ? new[] { p[1], p[2] } : axis == 'Y' ? new[] { p[2], p[0] } : new[] { p[0], p[1] };
                    if (Math.Abs(uv[0] / MM - u) < tol && Math.Abs(uv[1] / MM - v) < tol && Math.Abs(p[6] / MM - r) < tol) { hit = (Face2)fo; break; }
                }
            if (hit == null) throw new Exception(string.Format("找不到圆柱面 (轴 {0}, 过 ({1:F2},{2:F2}), R{3}): {4}", axis, u, v, r, name));
            m.ClearSelection2(true);
            bool ok = ((Entity)hit).Select4(false, null) && m.InsertAxis2(true);
            return Named(m, ok, name);
        }

        static Feature Named(ModelDoc2 m, bool ok, string name)
        {
            Feature f = Sw.LastFeature(m);
            m.ClearSelection2(true);
            if (!ok || f == null || f.GetTypeName2() != "RefAxis") throw new Exception("基准轴创建失败: " + name);
            f.Name = name;
            return f;
        }

        /// <summary>按 (cx, cy, cz, r) [mm] 查找零件中的圆形边线</summary>
        public static List<Edge> CircularEdges(ModelDoc2 m, IList<double[]> specs, double tol = 0.05)
        {
            var found = new List<Edge>();
            foreach (object bo in (object[])((PartDoc)m).GetBodies2((int)swBodyType_e.swSolidBody, true))
                foreach (object eo in (object[])((Body2)bo).GetEdges())
                {
                    Edge e = (Edge)eo;
                    Curve c = (Curve)e.GetCurve();
                    if (!c.IsCircle()) continue;
                    double[] p = (double[])c.CircleParams;     // cx cy cz ax ay az r (m)
                    foreach (double[] s in specs)
                        if (Math.Abs(p[0] / MM - s[0]) < tol && Math.Abs(p[1] / MM - s[1]) < tol &&
                            Math.Abs(p[2] / MM - s[2]) < tol && Math.Abs(p[6] / MM - s[3]) < tol)
                            found.Add(e);
                }
            return found;
        }

        /// <summary>给按 (cx, cy, cz, r) 指定的圆形边线加等半径圆角; 边数必须与规格数一致, 体积必须变化</summary>
        public static Feature FilletCircles(ModelDoc2 m, IList<double[]> specs, double r, string name = null)
        {
            var edges = CircularEdges(m, specs);
            if (edges.Count != specs.Count) throw new Exception(string.Format("圆角边线: 找到 {0} 条, 期望 {1}", edges.Count, specs.Count));
            double v0 = Sw.Volume(m);
            m.ClearSelection2(true);
            SelectionMgr sm = (SelectionMgr)m.SelectionManager;
            SelectData sd = (SelectData)sm.CreateSelectData();
            sd.Mark = 1;
            for (int i = 0; i < edges.Count; i++) ((Entity)edges[i]).Select4(i > 0, sd);
            int opts = (int)(swFeatureFilletOptions_e.swFeatureFilletPropagate | swFeatureFilletOptions_e.swFeatureFilletUniformRadius |
                             swFeatureFilletOptions_e.swFeatureFilletAttachEdges | swFeatureFilletOptions_e.swFeatureFilletKeepFeatures);
            Feature f = (Feature)m.FeatureManager.FeatureFillet3(opts, r * MM, 0, 0, 0, 0, 0, null, null, null, null, null, null, null);
            m.ClearSelection2(true);
            if (f == null) throw new Exception("圆角创建失败 R" + r);
            if (Math.Abs(Sw.Volume(m) - v0) < 1e-3) throw new Exception("圆角未改变几何 R" + r);
            if (name != null) f.Name = name;
            return f;
        }
    }
}
