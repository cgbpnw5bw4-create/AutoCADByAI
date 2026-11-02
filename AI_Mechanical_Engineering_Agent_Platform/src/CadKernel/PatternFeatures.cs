using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 单切除特征的阵列/镜像。选择标记来自 REASW
    /// 06_runtime_experiments/20261019_expanded_attempt01/runner_snapshot/ExpandedProbe.cs 的 R12 三案例;
    /// 场景通过不代表任意种子/方向/几何均可用。本平台逐次核对选择、特征、单实体和解析体积。
    /// </summary>
    public static class PatternFeatures
    {
        internal static void CheckSelection(IReadOnlyList<(int Type, int Mark)> actual, IReadOnlyList<(int Type, int Mark)> expected)
        {
            if (actual.Count != expected.Count) throw new InvalidOperationException("选择数量不符: " + actual.Count + "/" + expected.Count);
            for (int i = 0; i < expected.Count; i++)
                if (actual[i] != expected[i]) throw new InvalidOperationException("选择类型或标记不符: 第 " + (i + 1) + " 项 " + actual[i] + ", 预期 " + expected[i]);
        }

        internal static void RequireSelection(ModelDoc2 model, IReadOnlyList<(int Type, int Mark)> expected)
        {
            var manager = (SelectionMgr)model.SelectionManager;
            int count = manager.GetSelectedObjectCount2(-1);
            var actual = new List<(int Type, int Mark)>();
            for (int i = 1; i <= count; i++) actual.Add((manager.GetSelectedObjectType3(i, -1), manager.GetSelectedObjectMark(i)));
            CheckSelection(actual, expected);
        }

        internal static void ValidatePattern(int count, double spacingMm = 1)
        {
            if (count < 2 || count > 1000) throw new ArgumentOutOfRangeException(nameof(count), "阵列数量须为 2~1000");
            Parameters.Positive(spacingMm, nameof(spacingMm));
        }

        static void SelectEntity(ModelDoc2 model, Entity entity, bool append, int mark)
        {
            var data = (SelectData)((SelectionMgr)model.SelectionManager).CreateSelectData();
            data.Mark = mark;
            if (entity == null || !entity.Select4(append, data)) throw new InvalidOperationException("实体选择失败, mark=" + mark);
        }

        static void Seed(Feature seed)
        {
            if (seed == null) throw new ArgumentNullException(nameof(seed), "仅支持一个拉伸切除种子特征");
            RequireCutSeedType(seed.GetTypeName2(), () => seed.GetTypeName());
        }

        internal static void RequireCutSeedType(string typeName2, Func<string> underlyingType)
        {
            // 官方 2023 IFeature.GetTypeName: Instant3D 的 GetTypeName2 为 ICE 时须读取底层类型。
            // https://help.solidworks.com/2023/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.ifeature~gettypename.html
            string legacy = typeName2 == "ICE" ? underlyingType() : null;
            string actual = typeName2 == "ICE" ? legacy : typeName2;
            if (actual != "Cut")
                throw new ArgumentException("仅支持一个拉伸切除种子特征: GetTypeName2=" + (typeName2 ?? "<null>") +
                    ", GetTypeName=" + (typeName2 == "ICE" ? legacy ?? "<null>" : "<未调用>"), "seed");
        }

        static void ClearAfterFailure(ModelDoc2 model)
        {
            // 成功路径由 Finish 严格清除; RPC 已断开时这里只尽力清理, 不覆盖原始特征调用异常。
            try { model.ClearSelection2(true); } catch { }
        }

        static Body2 Body(ModelDoc2 model)
        {
            var bodies = ((PartDoc)model).GetBodies2((int)swBodyType_e.swSolidBody, false) as object[];
            if (bodies == null || bodies.Length != 1) throw new InvalidOperationException("阵列/镜像仅支持单实体零件");
            return (Body2)bodies[0];
        }

        static Feature Finish(ModelDoc2 model, Feature feature, string name, double beforeMm3, double expectedMm3)
        {
            model.ClearSelection2(true);
            if (feature == null) throw new InvalidOperationException(name + " 返回 null");
            bool warning; int error = feature.GetErrorCode2(out warning);
            if (error != 0 || warning) throw new InvalidOperationException(name + " 特征错误/警告: " + error);
            feature.Name = name;
            Parameters.Rebuild(model);
            double after = Sw.Volume(model);
            if (!double.IsFinite(after) || after >= beforeMm3 - 1e-3) throw new InvalidOperationException(name + " 未新增切除材料");
            Parameters.AssertVolume(model, expectedMm3);
            return feature;
        }

        /// <summary>沿模型坐标轴的直边作单向等距切除阵列; count 包含种子。</summary>
        public static Feature LinearCut(ModelDoc2 model, Feature seed, int axis, int count, double spacingMm, double expectedMm3, string name = "LinearCutPattern")
        {
            ValidatePattern(count, spacingMm); Parameters.Positive(expectedMm3, nameof(expectedMm3)); Seed(seed);
            if (axis < 0 || axis > 2) throw new ArgumentOutOfRangeException(nameof(axis));
            Parameters.Activate(model);
            Edge direction = null; bool reverse = false;
            foreach (object item in (object[])Body(model).GetEdges())
            {
                var edge = (Edge)item;
                var curve = (Curve)edge.GetCurve();
                if (curve == null || !curve.IsLine()) continue;
                var start = edge.GetStartVertex() as Vertex; var end = edge.GetEndVertex() as Vertex;
                if (start == null || end == null) continue;
                var a = (double[])start.GetPoint(); var b = (double[])end.GetPoint();
                if (Math.Abs(b[axis] - a[axis]) <= 1e-6 || Enumerable.Range(0, 3).Any(i => i != axis && Math.Abs(b[i] - a[i]) > 1e-7)) continue;
                direction = edge; reverse = b[axis] < a[axis]; break;
            }
            if (direction == null) throw new InvalidOperationException("找不到指定方向直边");
            model.ClearSelection2(true);
            double before = Sw.Volume(model);
            try
            {
                SelectEntity(model, (Entity)direction, false, 1);
                if (!seed.Select2(true, 4)) throw new InvalidOperationException("线阵种子选择失败");
                RequireSelection(model, new[] { ((int)swSelectType_e.swSelEDGES, 1), ((int)swSelectType_e.swSelBODYFEATURES, 4) });
                Feature pattern = model.FeatureManager.FeatureLinearPattern5(count, spacingMm * Sw.MM, 1, 0.01,
                    reverse, false, "", "", false, false, false, false, false, false, false, false, false, false, 0, 0, false, false);
                return Finish(model, pattern, name, before, expectedMm3);
            }
            finally { ClearAfterFailure(model); }
        }

        /// <summary>以过模型原点、平行 Z 的外圆柱面为方向, 完整一周等角度切除阵列。</summary>
        public static Feature CircularCut(ModelDoc2 model, Feature seed, double outerRadiusMm, int count, double expectedMm3, string name = "CircularCutPattern")
        {
            ValidatePattern(count); Parameters.Positive(outerRadiusMm, nameof(outerRadiusMm)); Parameters.Positive(expectedMm3, nameof(expectedMm3)); Seed(seed);
            Parameters.Activate(model);
            var candidates = new List<Face2>();
            foreach (object item in (object[])Body(model).GetFaces())
            {
                var face = (Face2)item; var surface = (Surface)face.GetSurface();
                if (!surface.IsCylinder()) continue;
                var p = (double[])surface.CylinderParams;
                if (Math.Abs(p[6] / Sw.MM - outerRadiusMm) <= 1e-6 && Math.Abs(Math.Abs(p[5]) - 1) <= 1e-6 && Math.Abs(p[0]) <= 1e-9 && Math.Abs(p[1]) <= 1e-9)
                    candidates.Add(face);
            }
            if (candidates.Count != 1) throw new InvalidOperationException("外圆柱方向面不唯一: " + candidates.Count);
            model.ClearSelection2(true);
            double before = Sw.Volume(model);
            try
            {
                SelectEntity(model, (Entity)candidates[0], false, 1);
                if (!seed.Select2(true, 4)) throw new InvalidOperationException("圆阵种子选择失败");
                RequireSelection(model, new[] { ((int)swSelectType_e.swSelFACES, 1), ((int)swSelectType_e.swSelBODYFEATURES, 4) });
                Feature pattern = model.FeatureManager.FeatureCircularPattern4(count, 2 * Math.PI, false, "", false, true, false);
                return Finish(model, pattern, name, before, expectedMm3);
            }
            finally { ClearAfterFailure(model); }
        }

        /// <summary>围绕三个标准基准面之一镜像一个切除特征, 不复制实体。</summary>
        public static Feature MirrorCut(ModelDoc2 model, Feature seed, int plane, double expectedMm3, string name = "MirrorCutFeature")
        {
            Parameters.Positive(expectedMm3, nameof(expectedMm3)); Seed(seed);
            if (plane < 0 || plane > 2) throw new ArgumentOutOfRangeException(nameof(plane));
            Parameters.Activate(model); Body(model);
            model.ClearSelection2(true);
            double before = Sw.Volume(model);
            try
            {
                if (!seed.Select2(false, 1) || !Sw.Planes(model)[plane].Select2(true, 2)) throw new InvalidOperationException("镜像种子或基准面选择失败");
                RequireSelection(model, new[] { ((int)swSelectType_e.swSelBODYFEATURES, 1), ((int)swSelectType_e.swSelDATUMPLANES, 2) });
                Feature mirror = model.FeatureManager.InsertMirrorFeature2(false, false, true, false, 0);
                return Finish(model, mirror, name, before, expectedMm3);
            }
            finally { ClearAfterFailure(model); }
        }
    }
}
