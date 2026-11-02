using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadKernel;
using SolidWorks.Interop.sldworks;

namespace CadProjects.Reasw
{
    /// <summary>
    /// 首批 REASW 公开 API 配方的本平台六案例: 完全约束圆、配置隔离、单配置 Add2、三孔线阵、四孔圆阵与切除镜像。
    /// 参考 20261019_expanded_attempt02/R06/R07、attempt04/R08、attempt01/R12 的冻结 runner_snapshot。
    /// 从本平台新建全部几何, 不打开 REASW 工件、不运行其工具。解析期望只来自输入和公式, 不取本次测量值。
    /// 2026-11-01 用户确认恢复补图版本并建立回归基准; 范围限于本样例, 不代替孔位独立读回。
    /// CAD-07 另以输入解析孔集合与保存副本的实体几何匹配, 覆盖当前七配置的九个 Z 向直圆柱孔。
    /// </summary>
    public sealed class ParametricPatternsProject : CadProject
    {
        internal const string AssemblyName = "RS-000_参数化阵列样例.SLDASM";
        static readonly string[] Names = { "完全约束圆", "配置隔离块", "单配置方程块", "线性孔阵列", "圆周孔阵列", "镜像切除" };
        static readonly string[] Keys = { "diameter_mm", "updated_diameter_mm", "block_width_mm", "block_height_mm", "depth_mm",
            "wide_width_mm", "equation_initial_width_mm", "equation_final_width_mm", "hole_radius_mm", "linear_count",
            "linear_pitch_mm", "disk_radius_mm", "bolt_circle_radius_mm", "circular_count" };
        internal SamplePlan Plan { get; private set; } = new SamplePlan();

        public override string Id { get { return "reasw_parametric_patterns"; } }
        public override string Description { get { return "具名参数、配置、方程与阵列六案例"; } }
        public override IEnumerable<string> KnownParams { get { return Keys; } }

        internal static string PartName(int index) { return "RS-00" + (index + 1) + "_" + Names[index] + ".SLDPRT"; }

        public override List<string> Validate()
        {
            Plan = new SamplePlan
            {
                Diameter = Input.Num("diameter_mm", 20), UpdatedDiameter = Input.Num("updated_diameter_mm", 30),
                Width = Input.Num("block_width_mm", 80), Height = Input.Num("block_height_mm", 50), Depth = Input.Num("depth_mm", 20),
                WideWidth = Input.Num("wide_width_mm", 100), EquationInitialWidth = Input.Num("equation_initial_width_mm", 90),
                EquationFinalWidth = Input.Num("equation_final_width_mm", 120), HoleRadius = Input.Num("hole_radius_mm", 2),
                LinearCount = Input.Int("linear_count", 3), LinearPitch = Input.Num("linear_pitch_mm", 15),
                DiskRadius = Input.Num("disk_radius_mm", 30), BoltCircleRadius = Input.Num("bolt_circle_radius_mm", 20),
                CircularCount = Input.Int("circular_count", 4),
            };
            return Plan.Validate();
        }

        public override IEnumerable<string> ModelFiles()
        {
            return Enumerable.Range(0, 6).Select(PartName).Concat(new[] { AssemblyName });
        }

        public override void Prepare()
        {
            Cat.ProjectName = Title; Cat.Version = "V1.0";
        }

        public override void BuildParts()
        {
            for (int i = 0; i < 6; i++)
            {
                int sample = i;
                Cat.Make(new Part { Id = "RS" + (i + 1), Code = "RS-00" + (i + 1), Name = Names[i], Mat = Mat.S304,
                    DrwType = "plate",
                    Notes = sample == 4 ? FormattableString.Invariant($"尺寸单位 mm。|{Plan.CircularCount} 孔沿 Φ{2 * Plan.BoltCircleRadius:0.###} 节圆均布，等分角 {360.0 / Plan.CircularCount:0.###}°。") : "尺寸单位 mm。",
                    Table = sample == 1 ? FormattableString.Invariant($"图示配置|Base;Base 宽度|{Plan.Width:0.###} mm;Wide 宽度|{Plan.WideWidth:0.###} mm") : ""
                }, model => BuildSample(model, sample));
            }
        }

        void BuildSample(ModelDoc2 model, int sample)
        {
            Parameters.RenameSingleConfiguration(model);
            switch (sample)
            {
                case 0:
                    Parameters.ConstrainedCylinder(model, Plan.Diameter, Plan.Depth, 5, 5);
                    Parameters.SetMm(model, Parameters.DimensionName("Diameter", Parameters.Profile), Plan.UpdatedDiameter);
                    Parameters.AssertVolume(model, Plan.NamedVolume);
                    break;
                case 1:
                    Parameters.Block(model, Plan.Width, Plan.Height, Plan.Depth);
                    Parameters.AddConfiguration(model, "Wide");
                    Parameters.InConfiguration(model, "Wide", () =>
                    {
                        Parameters.SetMm(model, Parameters.DimensionName("Width", Parameters.Profile), Plan.WideWidth);
                        Parameters.AssertVolume(model, Plan.WideVolume);
                        return true;
                    });
                    // 此刻已恢复 Base, 重新获取尺寸检查隔离, 不沿用 Wide 中的 Dimension 对象。
                    if (Math.Abs(Parameters.ReadMm(model, Parameters.DimensionName("Width", Parameters.Profile)) - Plan.Width) > 1e-6)
                        throw new InvalidOperationException("Wide 修改影响了 Base 配置");
                    Parameters.AssertVolume(model, Plan.BlockVolume);
                    break;
                case 2:
                    Parameters.Block(model, Plan.Width, Plan.Height, Plan.Depth);
                    Parameters.DriveSingleConfiguration(model, Parameters.DimensionName("Width", Parameters.Profile), "DrivenWidth",
                        Plan.EquationInitialWidth, Plan.EquationFinalWidth);
                    Parameters.AssertVolume(model, Plan.EquationVolume);
                    break;
                case 3:
                    Parameters.Block(model, Plan.Width, Plan.Height, Plan.Depth);
                    Feature linearSeed = Sk.XY(model, Plan.Depth).Circ(-25, 0, 2 * Plan.HoleRadius).CutThru();
                    PatternFeatures.LinearCut(model, linearSeed, 0, Plan.LinearCount, Plan.LinearPitch, Plan.LinearVolume);
                    break;
                case 4:
                    Sk.XY(model).Circ(0, 0, 2 * Plan.DiskRadius).Ext(Plan.Depth);
                    Feature circularSeed = Sk.XY(model, Plan.Depth).Circ(Plan.BoltCircleRadius, 0, 2 * Plan.HoleRadius).CutThru();
                    // 本平台孔中心半径默认 20 mm; REASW 原窄案例为 15 mm, 属于待本次新验证的参数范围。
                    PatternFeatures.CircularCut(model, circularSeed, Plan.DiskRadius, Plan.CircularCount, Plan.CircularVolume);
                    break;
                case 5:
                    Parameters.Block(model, Plan.Width, Plan.Height, Plan.Depth);
                    Feature mirrorSeed = Sk.XY(model, Plan.Depth).Circ(15, 5, 2 * Plan.HoleRadius).CutThru();
                    PatternFeatures.MirrorCut(model, mirrorSeed, 2, Plan.MirrorVolume);
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(sample));
            }
            Parameters.Rebuild(model);
        }

        public override List<AsmTarget> BuildAssemblies()
        {
            var assembly = new AsmBuild(Path.Combine(Run.Models, AssemblyName));
            double pitchX = Math.Max(Math.Max(Plan.WideWidth, Plan.EquationFinalWidth), Math.Max(Plan.Width, 2 * Plan.DiskRadius)) + 60;
            double pitchY = Math.Max(Plan.Height, Math.Max(Plan.UpdatedDiameter, 2 * Plan.DiskRadius)) + 60;
            for (int i = 0; i < 6; i++) assembly.Add(Cat.P("RS" + (i + 1)), (i % 3) * pitchX, (i / 3) * pitchY, 0);
            string path = assembly.Build();
            Cat.Reg["RS_ASM"] = path;
            return new List<AsmTarget> { new AsmTarget { Path = path, Main = true } };
        }

        public override void ExtraChecks(List<AsmTarget> targets)
        {
            var expected = MakeExpectations();
            var failures = new List<string>();
            foreach (var part in expected.Parts)
                foreach (var configuration in part.Configurations)
                {
                    try
                    {
                        Parameters.CheckSavedPart(Path.Combine(Run.Dir, part.File), configuration.Name, configuration.VolumeM3.Value * 1e9,
                            configuration.Dimensions.ToDictionary(d => d.FullName, d => d.ValueM.Value / Sw.MM));
                    }
                    catch (Exception ex) { failures.Add(Path.GetFileName(part.File) + "/" + configuration.Name + ": " + ex.Message); }
                }
            Run.Check("参数化与阵列解析校核", failures.Count == 0,
                failures.Count == 0 ? "6 个案例 / 7 个配置与解析体积、具名尺寸一致" : string.Join("; ", failures));
        }

        public override void WriteVerificationExpectations()
        {
            Verification.SaveExpectations(Run.Dir, MakeExpectations());
        }

        internal VerifyExpectations MakeExpectations()
        {
            var result = new VerifyExpectations();
            VerifyDimension Dimension(string name, double mm, string owner = Parameters.Profile)
            {
                return new VerifyDimension { FullName = Parameters.DimensionName(name, owner), ValueM = mm * Sw.MM, ToleranceM = 1e-9 };
            }
            VerifyConfiguration Configuration(string name, double volumeMm3, string oracle, params VerifyDimension[] dimensions)
            {
                return new VerifyConfiguration { Name = name, BodyCount = 1, VolumeM3 = volumeMm3 * 1e-9, VolumeToleranceM3 = 1e-12,
                    Oracle = oracle, Dimensions = dimensions.ToList(), Holes = new List<VerifyHole>(), HoleToleranceM = 1e-7 };
            }
            VerifyConfiguration Block(string name, double width, double volume, string oracle)
            {
                return Configuration(name, volume, oracle, Dimension("Width", width), Dimension("Height", Plan.Height), Dimension("Depth", Plan.Depth, Parameters.Boss));
            }
            void Part(int index, params VerifyConfiguration[] configurations)
            {
                result.Parts.Add(new VerifyPart { File = "models/" + PartName(index), Configurations = configurations.ToList() });
            }
            VerifyHole Hole(double x, double y)
            {
                return new VerifyHole { StartM = new[] { x * Sw.MM, y * Sw.MM, 0.0 },
                    EndM = new[] { x * Sw.MM, y * Sw.MM, Plan.Depth * Sw.MM }, RadiusM = Plan.HoleRadius * Sw.MM };
            }
            Part(0, Configuration("Base", Plan.NamedVolume, "π × updated_diameter_mm² / 4 × depth_mm (mm³); 单圆柱无孔",
                Dimension("Diameter", Plan.UpdatedDiameter), Dimension("Depth", Plan.Depth, Parameters.Boss)));
            Part(1, Block("Base", Plan.Width, Plan.BlockVolume, "block_width_mm × block_height_mm × depth_mm (mm³); 方块无孔"),
                Block("Wide", Plan.WideWidth, Plan.WideVolume, "wide_width_mm × block_height_mm × depth_mm (mm³); 方块无孔"));
            Part(2, Block("Base", Plan.EquationFinalWidth, Plan.EquationVolume, "equation_final_width_mm × block_height_mm × depth_mm (mm³); 方块无孔"));
            var linear = Block("Base", Plan.Width, Plan.LinearVolume,
                "block_volume - linear_count × π × hole_radius_mm² × depth_mm (mm³); 孔心(-25+i×linear_pitch_mm,0), i=0..linear_count-1; Z=0..depth_mm, r=hole_radius_mm");
            linear.Holes = Enumerable.Range(0, Plan.LinearCount).Select(i => Hole(-25 + i * Plan.LinearPitch, 0)).ToList();
            Part(3, linear);
            var circular = Configuration("Base", Plan.CircularVolume,
                "π × disk_radius_mm² × depth_mm - circular_count × π × hole_radius_mm² × depth_mm (mm³); 孔心R×(cos(2πi/n),sin(2πi/n)), R=bolt_circle_radius_mm, n=circular_count; Z=0..depth_mm, r=hole_radius_mm");
            circular.Holes = Enumerable.Range(0, Plan.CircularCount).Select(i => Hole(
                Plan.BoltCircleRadius * Math.Cos(2 * Math.PI * i / Plan.CircularCount),
                Plan.BoltCircleRadius * Math.Sin(2 * Math.PI * i / Plan.CircularCount))).ToList();
            Part(4, circular);
            var mirror = Block("Base", Plan.Width, Plan.MirrorVolume,
                "block_volume - 2 × π × hole_radius_mm² × depth_mm (mm³); 种子孔心(15,5)沿X=0镜像为(-15,5); Z=0..depth_mm, r=hole_radius_mm");
            mirror.Holes.Add(Hole(15, 5)); mirror.Holes.Add(Hole(-15, 5));
            Part(5, mirror);
            result.Steps.Add(new VerifyStep { File = "exports/" + Path.ChangeExtension(AssemblyName, ".step"), BodyCount = 6,
                VolumeM3 = Plan.AssemblyVolume * 1e-9, VolumeToleranceM3 = 1e-12,
                Oracle = "各零件保存的 Base 配置解析体积相加; 6 个互不重叠单实体, 不使用本次测量值" });
            return result;
        }
    }

    /// <summary>六案例的纯参数与解析期望, 不依赖 CAD 测量。</summary>
    internal sealed class SamplePlan
    {
        public double Diameter = 20, UpdatedDiameter = 30, Width = 80, Height = 50, Depth = 20,
            WideWidth = 100, EquationInitialWidth = 90, EquationFinalWidth = 120, HoleRadius = 2,
            LinearPitch = 15, DiskRadius = 30, BoltCircleRadius = 20;
        public int LinearCount = 3, CircularCount = 4;
        public double NamedVolume { get { return Math.PI * UpdatedDiameter * UpdatedDiameter / 4 * Depth; } }
        public double BlockVolume { get { return Width * Height * Depth; } }
        public double WideVolume { get { return WideWidth * Height * Depth; } }
        public double EquationVolume { get { return EquationFinalWidth * Height * Depth; } }
        public double HoleVolume { get { return Math.PI * HoleRadius * HoleRadius * Depth; } }
        public double LinearVolume { get { return BlockVolume - LinearCount * HoleVolume; } }
        public double CircularVolume { get { return Math.PI * DiskRadius * DiskRadius * Depth - CircularCount * HoleVolume; } }
        public double MirrorVolume { get { return BlockVolume - 2 * HoleVolume; } }
        public double AssemblyVolume { get { return NamedVolume + BlockVolume + EquationVolume + LinearVolume + CircularVolume + MirrorVolume; } }

        public List<string> Validate()
        {
            var issues = new List<string>();
            foreach (double value in new[] { Diameter, UpdatedDiameter, Width, Height, Depth, WideWidth, EquationInitialWidth, EquationFinalWidth,
                HoleRadius, LinearPitch, DiskRadius, BoltCircleRadius })
                if (!double.IsFinite(value) || value <= 0) issues.Add("所有尺寸须为有限正数");
            if (issues.Count > 0) return issues;
            if (LinearCount < 2 || LinearCount > 12 || CircularCount < 2 || CircularCount > 12) issues.Add("样例阵列数量须为 2~12");
            if (Diameter == UpdatedDiameter) issues.Add("圆直径初值与终值须不同");
            if (WideWidth == Width) issues.Add("Base 与 Wide 宽度须不同");
            if (EquationInitialWidth == EquationFinalWidth) issues.Add("方程式初值与终值须不同");
            if (LinearPitch <= 2 * HoleRadius || -25 - HoleRadius <= -Width / 2 || -25 + (LinearCount - 1) * LinearPitch + HoleRadius >= Width / 2)
                issues.Add("线阵孔须互不相交且完整落在块内");
            if (15 + HoleRadius >= Width / 2 || 5 + HoleRadius >= Height / 2 || HoleRadius >= 15) issues.Add("镜像两孔须互不相交且完整落在块内");
            if (BoltCircleRadius + HoleRadius >= DiskRadius || HoleRadius >= BoltCircleRadius * Math.Sin(Math.PI / Math.Max(2, CircularCount)))
                issues.Add("圆阵孔须互不相交且完整落在盘内");
            if (2 * HoleRadius >= Height) issues.Add("线阵孔径须小于块高");
            if (new[] { NamedVolume, BlockVolume, WideVolume, EquationVolume, LinearVolume, CircularVolume, MirrorVolume, AssemblyVolume }
                .Any(v => !double.IsFinite(v) || v <= 1e-3)) issues.Add("解析体积须有限且大于校核绝对允差 0.001 mm³");
            return issues;
        }
    }
}
