using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>
    /// 具名长度尺寸、配置与单配置方程式。参考 REASW 20261015_parametric_v2、
    /// 20261019_expanded_attempt02/R06/R07 和 attempt04/R08 的冻结 runner_snapshot。
    /// 来源仅认证指定 SP05 窄案例; 本平台仍须独立运行验收, 不支持多配置 Add3。
    /// </summary>
    public static class Parameters
    {
        public const string Profile = "ParameterProfile", Boss = "ParameterBoss", BaseConfiguration = "Base";

        internal static void Positive(double value, string name)
        {
            if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name, "必须是有限正数");
        }

        internal static void Identifier(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Any(c => c == '@' || c == '"' || char.IsControl(c)))
                throw new ArgumentException("名称不能为空, 不能含 @、引号或控制字符", name);
        }

        public static string DimensionName(string name, string owner)
        {
            Identifier(name, nameof(name)); Identifier(owner, nameof(owner));
            return name + "@" + owner;
        }

        internal static string VariableEquation(string name, double millimeters)
        {
            Identifier(name, nameof(name)); Positive(millimeters, nameof(millimeters));
            return "\"" + name + "\" = " + millimeters.ToString("R", CultureInfo.InvariantCulture) + "mm";
        }

        internal static string DrivingEquation(string dimension, string variable)
        {
            ValidateDimensionName(dimension); Identifier(variable, nameof(variable));
            return "\"" + dimension + "\" = \"" + variable + "\"";
        }

        static void ValidateDimensionName(string name)
        {
            string[] fields = (name ?? "").Split('@');
            if (fields.Length != 2) throw new ArgumentException("尺寸名须为 Name@Feature", nameof(name));
            Identifier(fields[0], nameof(name)); Identifier(fields[1], nameof(name));
        }

        internal static void Activate(ModelDoc2 model)
        {
            Sw.Activate(model);
            var active = Sw.App.ActiveDoc as ModelDoc2;
            if (active == null || active.GetTitle() != model.GetTitle() || active.GetPathName() != model.GetPathName())
                throw new InvalidOperationException("活动文档与参数操作目标不一致");
        }

        static Dimension FindDimension(ModelDoc2 model, string name)
        {
            ValidateDimensionName(name);
            return model.Parameter(name) as Dimension ?? throw new InvalidOperationException("找不到具名尺寸 " + name);
        }

        static double ValueMm(Dimension dimension)
        {
            var values = dimension.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as Array;
            if (values == null || values.Length != 1) throw new InvalidOperationException("当前配置尺寸读数个数不是 1");
            double value = Convert.ToDouble(values.GetValue(0), CultureInfo.InvariantCulture) / Sw.MM;
            Positive(value, "尺寸读数");
            return value;
        }

        static void NameAndSet(Dimension dimension, string name, double valueMm)
        {
            if (dimension == null) throw new InvalidOperationException("驱动尺寸创建失败");
            Identifier(name, nameof(name)); Positive(valueMm, nameof(valueMm));
            dimension.Name = name;
            int result = dimension.SetSystemValue3(valueMm * Sw.MM, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
            if (result != (int)swSetValueReturnStatus_e.swSetValue_Successful || dimension.DrivenState != (int)swDimensionDrivenState_e.swDimensionDriving)
                throw new InvalidOperationException("尺寸不是可驱动状态或赋值失败: " + name + ", status=" + result);
            if (Math.Abs(ValueMm(dimension) - valueMm) > 1e-6) throw new InvalidOperationException("尺寸赋值读回不符: " + name);
        }

        static Feature ActiveProfile(ModelDoc2 model)
        {
            if (model.SketchManager.ActiveSketch == null) throw new InvalidOperationException("不在草图编辑状态");
            Feature found = null;
            for (Feature f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
                if (f.GetTypeName2() == "ProfileFeature") found = f;
            if (found == null) throw new InvalidOperationException("找不到活动草图特征");
            found.Name = Profile;
            return found;
        }

        /// <summary>在新零件中建立具名宽高深块; 拉伸仍由 Sk.Ext 执行并校验。</summary>
        public static Feature Block(ModelDoc2 model, double widthMm, double heightMm, double depthMm)
        {
            Positive(widthMm, nameof(widthMm)); Positive(heightMm, nameof(heightMm)); Positive(depthMm, nameof(depthMm));
            Activate(model);
            var sketch = Sk.XY(model);
            model.SketchManager.AddToDB = false; // 中心矩形需要其自动几何关系, 之后由驱动尺寸更新整体轮廓。
            ActiveProfile(model);
            var segments = model.SketchManager.CreateCenterRectangle(0, 0, 0, widthMm * Sw.MM / 2, heightMm * Sw.MM / 2, 0) as object[];
            if (segments == null) throw new InvalidOperationException("中心矩形创建失败");
            SketchSegment horizontal = null, vertical = null;
            foreach (var item in segments)
            {
                var segment = (SketchSegment)item;
                if (segment.ConstructionGeometry || segment.GetType() != (int)swSketchSegments_e.swSketchLINE) continue;
                var line = (SketchLine)item;
                var a = (SketchPoint)line.GetStartPoint2(); var b = (SketchPoint)line.GetEndPoint2();
                if (Math.Abs(a.Y - b.Y) < 1e-8 && Math.Abs(a.X - b.X) > 1e-5 && horizontal == null) horizontal = segment;
                if (Math.Abs(a.X - b.X) < 1e-8 && Math.Abs(a.Y - b.Y) > 1e-5 && vertical == null) vertical = segment;
            }
            if (horizontal == null || vertical == null) throw new InvalidOperationException("矩形轮廓缺少水平或竖直边");
            model.ClearSelection2(true);
            if (!horizontal.Select4(false, null)) throw new InvalidOperationException("宽度边选择失败");
            PatternFeatures.RequireSelection(model, new[] { ((int)swSelectType_e.swSelSKETCHSEGS, 0) });
            var dw = model.AddHorizontalDimension2(0, -(heightMm / 2 + 15) * Sw.MM, 0) as DisplayDimension;
            NameAndSet(dw?.GetDimension2(0) as Dimension, "Width", widthMm);
            model.ClearSelection2(true);
            if (!vertical.Select4(false, null)) throw new InvalidOperationException("高度边选择失败");
            PatternFeatures.RequireSelection(model, new[] { ((int)swSelectType_e.swSelSKETCHSEGS, 0) });
            var dh = model.AddVerticalDimension2(-(widthMm / 2 + 15) * Sw.MM, 0, 0) as DisplayDimension;
            NameAndSet(dh?.GetDimension2(0) as Dimension, "Height", heightMm);
            Feature boss = sketch.Ext(depthMm);
            boss.Name = Boss;
            NameAndSet(boss.Parameter("D1") as Dimension, "Depth", depthMm);
            Rebuild(model);
            AssertVolume(model, widthMm * heightMm * depthMm);
            return boss;
        }

        /// <summary>固定圆心 + 直径驱动的完全约束圆; 仅认证此类圆, 不宣称任意草图求解。</summary>
        public static Feature ConstrainedCylinder(ModelDoc2 model, double diameterMm, double depthMm, double xMm = 0, double yMm = 0)
        {
            Positive(diameterMm, nameof(diameterMm)); Positive(depthMm, nameof(depthMm));
            if (!double.IsFinite(xMm) || !double.IsFinite(yMm)) throw new ArgumentException("圆心必须有限");
            Activate(model);
            var context = Sk.XY(model);
            model.SketchManager.AddToDB = false;
            ActiveProfile(model);
            var circle = model.SketchManager.CreateCircleByRadius(xMm * Sw.MM, yMm * Sw.MM, 0, diameterMm * Sw.MM / 2) as SketchSegment;
            if (circle == null) throw new InvalidOperationException("圆创建失败");
            var center = ((SketchArc)circle).GetCenterPoint2() as SketchPoint;
            model.ClearSelection2(true);
            if (center == null || !center.Select4(false, null)) throw new InvalidOperationException("圆心选择失败");
            PatternFeatures.RequireSelection(model, new[] { ((int)swSelectType_e.swSelSKETCHPOINTS, 0) });
            model.SketchAddConstraints("sgFIXED");
            model.ClearSelection2(true);
            if (!circle.Select4(false, null)) throw new InvalidOperationException("直径圆选择失败");
            PatternFeatures.RequireSelection(model, new[] { ((int)swSelectType_e.swSelSKETCHSEGS, 0) });
            var display = model.AddDiameterDimension2((xMm + diameterMm) * Sw.MM, (yMm + diameterMm) * Sw.MM, 0) as DisplayDimension;
            NameAndSet(display?.GetDimension2(0) as Dimension, "Diameter", diameterMm);
            int state = ((Sketch)model.SketchManager.ActiveSketch).GetConstrainedStatus();
            if (state != (int)swConstrainedStatus_e.swFullyConstrained) throw new InvalidOperationException("圆草图未完全约束: " + state);
            var readCenter = (SketchPoint)((SketchArc)circle).GetCenterPoint2();
            if (circle.ConstructionGeometry || Math.Abs(readCenter.X / Sw.MM - xMm) > 1e-6 || Math.Abs(readCenter.Y / Sw.MM - yMm) > 1e-6 ||
                Math.Abs(((SketchArc)circle).GetRadius() / Sw.MM - diameterMm / 2) > 1e-6)
                throw new InvalidOperationException("完全约束圆的圆心或半径读回不符");
            Feature boss = context.Ext(depthMm);
            boss.Name = Boss;
            NameAndSet(boss.Parameter("D1") as Dimension, "Depth", depthMm);
            Rebuild(model);
            AssertVolume(model, Math.PI * diameterMm * diameterMm / 4 * depthMm);
            return boss;
        }

        public static double ReadMm(ModelDoc2 model, string dimension)
        {
            Activate(model);
            return ValueMm(FindDimension(model, dimension)); // 每次读取重新取得, 不跨配置缓存 Dimension。
        }

        public static void SetMm(ModelDoc2 model, string dimension, double millimeters)
        {
            Positive(millimeters, nameof(millimeters)); Activate(model);
            var value = FindDimension(model, dimension);
            int status = value.SetSystemValue3(millimeters * Sw.MM, (int)swSetValueInConfiguration_e.swSetValue_InThisConfiguration, null);
            if (status != (int)swSetValueReturnStatus_e.swSetValue_Successful) throw new InvalidOperationException("驱动尺寸修改失败: " + dimension + ", status=" + status);
            Rebuild(model);
            if (Math.Abs(ReadMm(model, dimension) - millimeters) > 1e-6) throw new InvalidOperationException("重建后具名尺寸不符: " + dimension);
        }

        public static void RenameSingleConfiguration(ModelDoc2 model, string name = BaseConfiguration)
        {
            Identifier(name, nameof(name)); Activate(model);
            if (model.GetConfigurationCount() != 1) throw new InvalidOperationException("只允许重命名新单配置零件");
            model.ConfigurationManager.ActiveConfiguration.Name = name;
            if (model.ConfigurationManager.ActiveConfiguration.Name != name) throw new InvalidOperationException("配置重命名失败");
        }

        public static void AddConfiguration(ModelDoc2 model, string name)
        {
            Identifier(name, nameof(name)); Activate(model);
            string original = model.ConfigurationManager.ActiveConfiguration.Name;
            try
            {
                if (model.GetConfigurationByName(name) != null) throw new InvalidOperationException("配置已存在: " + name);
                if (model.ConfigurationManager.AddConfiguration2(name, "参数化回归样例", "", 0, "", "", true) == null)
                    throw new InvalidOperationException("配置创建失败: " + name);
            }
            finally { SelectConfiguration(model, original); }
        }

        static void SelectConfiguration(ModelDoc2 model, string name)
        {
            if (model.ConfigurationManager.ActiveConfiguration.Name != name && !model.ShowConfiguration2(name))
                throw new InvalidOperationException("配置切换失败: " + name);
            if (model.ConfigurationManager.ActiveConfiguration.Name != name) throw new InvalidOperationException("活动配置身份不符: " + name);
            Rebuild(model);
        }

        internal static T InConfiguration<T>(Func<string> current, Action<string> select, string name, Func<T> body)
        {
            Identifier(name, nameof(name));
            string original = current();
            try
            {
                select(name);
                if (current() != name) throw new InvalidOperationException("活动配置身份不符: " + name);
                return body();
            }
            finally
            {
                select(original);
                if (current() != original) throw new InvalidOperationException("原配置恢复失败: " + original);
            }
        }

        public static T InConfiguration<T>(ModelDoc2 model, string name, Func<T> body)
        {
            Activate(model);
            return InConfiguration(() => model.ConfigurationManager.ActiveConfiguration.Name, cfg => SelectConfiguration(model, cfg), name, body);
        }

        /// <summary>单配置 Add2 路线, 显式检查初值和更新值; 不回退到尚未证实的 Add3。</summary>
        public static void DriveSingleConfiguration(ModelDoc2 model, string dimension, string variable, double initialMm, double finalMm)
        {
            string initial = VariableEquation(variable, initialMm), final = VariableEquation(variable, finalMm);
            string driving = DrivingEquation(dimension, variable);
            Activate(model);
            RequireSingleConfiguration(model.GetConfigurationCount());
            var equations = (EquationMgr)model.GetEquationMgr();
            equations.AutomaticRebuild = true; equations.AutomaticSolveOrder = true;
            int index = equations.Add2(-1, initial, false);
            if (index < 0 || equations.Add2(-1, driving, false) < 0) throw new InvalidOperationException("Add2 方程式创建失败");
            equations.EvaluateAll(); Rebuild(model);
            if (Math.Abs(ReadMm(model, dimension) - initialMm) > 1e-6) throw new InvalidOperationException("单配置方程式初值未驱动几何");
            equations = (EquationMgr)model.GetEquationMgr();
            equations.set_Equation(index, final);
            equations.EvaluateAll(); Rebuild(model);
            if (Math.Abs(ReadMm(model, dimension) - finalMm) > 1e-6) throw new InvalidOperationException("单配置方程式更新未驱动几何");
        }

        internal static void RequireSingleConfiguration(int count)
        {
            if (count != 1) throw new InvalidOperationException("Add2 方程式仅支持单配置; 多配置 Add3 尚未验证");
        }

        public static void Rebuild(ModelDoc2 model)
        {
            model.ClearSelection2(true);
            if (model.SketchManager.ActiveSketch != null) throw new InvalidOperationException("仍在草图编辑状态");
            if (!model.EditRebuild3()) throw new InvalidOperationException("参数化重建失败");
            if (Sw.BodyCount(model) != 1) throw new InvalidOperationException("参数化零件不是单实体");
            for (Feature f = model.FirstFeature() as Feature; f != null; f = f.GetNextFeature() as Feature)
            {
                bool warning; int error = f.GetErrorCode2(out warning);
                if (error != 0 || warning) throw new InvalidOperationException("特征重建错误/警告: " + f.Name + ", error=" + error);
            }
        }

        public static void AssertVolume(ModelDoc2 model, double expectedMm3, double toleranceMm3 = 1e-3)
        {
            Positive(expectedMm3, nameof(expectedMm3)); Positive(toleranceMm3, nameof(toleranceMm3));
            if (toleranceMm3 >= expectedMm3) throw new ArgumentOutOfRangeException(nameof(toleranceMm3));
            model.ClearSelection2(true);
            double actual = Sw.Volume(model);
            if (!double.IsFinite(actual) || Math.Abs(actual - expectedMm3) > toleranceMm3)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "解析体积不符: 实际 {0:R}, 预期 {1:R} ±{2:R} mm³", actual, expectedMm3, toleranceMm3));
        }

        /// <summary>生产链中重开零件核对指定配置; 最终独立只读验收由 Verification 另行执行。</summary>
        public static void CheckSavedPart(string path, string configuration, double expectedMm3, IDictionary<string, double> dimensionsMm)
        {
            Sw.CloseAll();
            ModelDoc2 model = Sw.Open(path);
            try
            {
                InConfiguration(model, configuration, () =>
                {
                    AssertVolume(model, expectedMm3);
                    foreach (var dimension in dimensionsMm)
                        if (Math.Abs(ReadMm(model, dimension.Key) - dimension.Value) > 1e-6)
                            throw new InvalidOperationException("保存后具名尺寸不符: " + configuration + "/" + dimension.Key);
                    return true;
                });
            }
            finally { Sw.Close(model); }
        }
    }
}
