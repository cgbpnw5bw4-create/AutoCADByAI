using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CadKernel;
using CadProjects.Reasw;
using Xunit;

namespace CadTests
{
    /// <summary>参数合同、配置恢复、选择前置条件和解析几何预期; 不连接 SolidWorks。</summary>
    public class ParametricPatternTests
    {
        [Fact]
        public void StableDimensionNamesRejectExecutableEquationText()
        {
            Assert.Equal("Diameter@ParameterProfile", Parameters.DimensionName("Diameter", Parameters.Profile));
            Assert.Throws<ArgumentException>(() => Parameters.DimensionName("Width@Other", Parameters.Profile));
            Assert.Throws<ArgumentException>(() => Parameters.DimensionName("Width\" = 100mm", Parameters.Profile));
            Assert.Throws<ArgumentException>(() => Parameters.DrivingEquation("Width@Profile@Document", "DrivenWidth"));
        }

        [Fact]
        public void EquationLengthsKeepExplicitMillimetersAcrossCultures()
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                Assert.Equal("\"DrivenWidth\" = 120.25mm", Parameters.VariableEquation("DrivenWidth", 120.25));
                Assert.Equal("\"Width@ParameterProfile\" = \"DrivenWidth\"", Parameters.DrivingEquation("Width@ParameterProfile", "DrivenWidth"));
                Assert.Throws<ArgumentOutOfRangeException>(() => Parameters.VariableEquation("Width", double.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(() => Parameters.VariableEquation("Width", 0));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [Fact]
        public void ProductionEquationGateRejectsMultipleConfigurations()
        {
            Parameters.RequireSingleConfiguration(1);
            Assert.Throws<InvalidOperationException>(() => Parameters.RequireSingleConfiguration(0));
            Assert.Throws<InvalidOperationException>(() => Parameters.RequireSingleConfiguration(2));
            Assert.Throws<InvalidOperationException>(() => Parameters.RequireSingleConfiguration(3));
        }

        [Fact]
        public void ConfigurationBodyReadsCurrentStateAndRestoresOriginal()
        {
            string current = "Base";
            var widths = new Dictionary<string, double> { { "Base", 80 }, { "Wide", 100 } };
            var switches = new List<string>();
            double result = Parameters.InConfiguration(() => current, name => { current = name; switches.Add(name); }, "Wide", () => widths[current]);
            Assert.Equal(100, result);
            Assert.Equal("Base", current);
            Assert.Equal(new[] { "Wide", "Base" }, switches);
            Assert.Equal(80, widths[current]);
        }

        [Fact]
        public void ConfigurationFailureStillRestoresOriginalAndNeverReportsSuccess()
        {
            string current = "Base";
            Assert.Throws<InvalidOperationException>(() => Parameters.InConfiguration<int>(() => current, name => current = name,
                "Wide", () => throw new InvalidOperationException("读回失败")));
            Assert.Equal("Base", current);
            Assert.Throws<InvalidOperationException>(() => Parameters.InConfiguration(() => current, _ => { }, "Wide", () => 1));
            Assert.Equal("Base", current);
        }

        [Fact]
        public void SelectionContractsRejectCountTypeMarkAndOrderChanges()
        {
            var expected = new[] { (1, 1), (22, 4) }; // 直边方向 mark1、种子特征 mark4。
            PatternFeatures.CheckSelection(expected, expected);
            Assert.Throws<InvalidOperationException>(() => PatternFeatures.CheckSelection(new[] { (1, 1) }, expected));
            Assert.Throws<InvalidOperationException>(() => PatternFeatures.CheckSelection(new[] { (2, 1), (22, 4) }, expected));
            Assert.Throws<InvalidOperationException>(() => PatternFeatures.CheckSelection(new[] { (1, 0), (22, 4) }, expected));
            Assert.Throws<InvalidOperationException>(() => PatternFeatures.CheckSelection(new[] { (22, 4), (1, 1) }, expected));
        }

        [Fact]
        public void Instant3DCutSeedRequiresExactUnderlyingCutType()
        {
            int reads = 0;
            PatternFeatures.RequireCutSeedType("ICE", () => { reads++; return "Cut"; });
            Assert.Equal(1, reads);
            var error = Assert.Throws<ArgumentException>(() => PatternFeatures.RequireCutSeedType("ICE", () => "Boss"));
            Assert.Contains("GetTypeName2=ICE", error.Message);
            Assert.Contains("GetTypeName=Boss", error.Message);
            Assert.Throws<ArgumentException>(() => PatternFeatures.RequireCutSeedType("ICE", () => null));
        }

        [Fact]
        public void NormalFeatureTypesNeverUseInstant3DFallback()
        {
            string UnexpectedRead() { throw new InvalidOperationException("普通特征不应读取旧类型"); }
            PatternFeatures.RequireCutSeedType("Cut", UnexpectedRead);
            var error = Assert.Throws<ArgumentException>(() => PatternFeatures.RequireCutSeedType("Boss", UnexpectedRead));
            Assert.Contains("GetTypeName2=Boss", error.Message);
            Assert.Contains("GetTypeName=<未调用>", error.Message);
        }

        [Theory]
        [InlineData(1, 15)]
        [InlineData(1001, 15)]
        [InlineData(3, 0)]
        [InlineData(3, -1)]
        [InlineData(3, double.PositiveInfinity)]
        public void InvalidPatternDefinitionsAreRejectedBeforeCom(int count, double pitch)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PatternFeatures.ValidatePattern(count, pitch));
        }

        [Fact]
        public void DefaultVolumesHaveIndependentAnalyticValues()
        {
            var plan = new SamplePlan();
            Assert.Empty(plan.Validate());
            Assert.Equal(20, plan.BoltCircleRadius); // 本平台批准的新验证半径, 不冒充 REASW 原 15 mm 范围。
            Assert.Equal(4500 * Math.PI, plan.NamedVolume, 7);          // Ø30 ×20。
            Assert.Equal(80000, plan.BlockVolume);
            Assert.Equal(100000, plan.WideVolume);
            Assert.Equal(120000, plan.EquationVolume);
            Assert.Equal(80000 - 240 * Math.PI, plan.LinearVolume, 7); // 3 × R2 ×20 孔。
            Assert.Equal(17680 * Math.PI, plan.CircularVolume, 7);     // R30盘 -4孔。
            Assert.Equal(80000 - 160 * Math.PI, plan.MirrorVolume, 7); // 原孔及镜像孔。
            Assert.Equal(360000 + 21780 * Math.PI, plan.AssemblyVolume, 7); // 总装采用 Base 而非 Wide。
        }

        [Fact]
        public void GeometryPreconditionsRejectOverlappingOrOutOfBoundsHoles()
        {
            Assert.NotEmpty(new SamplePlan { LinearPitch = 4 }.Validate());
            Assert.NotEmpty(new SamplePlan { LinearCount = 12 }.Validate());
            Assert.NotEmpty(new SamplePlan { DiskRadius = 16 }.Validate());
            Assert.NotEmpty(new SamplePlan { BoltCircleRadius = 2 }.Validate());
            Assert.NotEmpty(new SamplePlan { Height = 10 }.Validate());
            Assert.NotEmpty(new SamplePlan { UpdatedDiameter = 20 }.Validate());
            Assert.NotEmpty(new SamplePlan { WideWidth = 80 }.Validate());
            Assert.NotEmpty(new SamplePlan { EquationFinalWidth = 90 }.Validate());
            Assert.NotEmpty(new SamplePlan { Depth = double.PositiveInfinity }.Validate());
        }

        [Fact]
        public void NewInputAndVerificationExpectationsCoverAllSixCases()
        {
            string root = Env.FindPlatformRoot(AppContext.BaseDirectory);
            var input = DesignInput.Load(Path.Combine(root, "input", "reasw_parametric_patterns"));
            var project = new ParametricPatternsProject();
            project.Bind(input);
            Assert.Empty(project.Validate());
            Assert.Empty(input.Unknown(project.KnownParams));
            Assert.Equal(7, project.ModelFiles().Count());
            var expected = project.MakeExpectations();
            Assert.Equal(6, expected.Parts.Count);
            Assert.Equal(7, expected.Parts.Sum(p => p.Configurations.Count));
            Assert.Equal(new[] { "Base", "Wide" }, expected.Parts[1].Configurations.Select(c => c.Name));
            Assert.Equal(0.03, expected.Parts[0].Configurations[0].Dimensions.Single(d => d.FullName.StartsWith("Diameter@")).ValueM.Value, 12);
            Assert.Equal(0.0001, expected.Parts[1].Configurations[1].VolumeM3.Value, 12);
            Assert.Equal(0.00012, expected.Parts[2].Configurations[0].VolumeM3.Value, 12);
            var step = Assert.Single(expected.Steps);
            Assert.Equal(6, step.BodyCount);
            Assert.Equal((360000 + 21780 * Math.PI) * 1e-9, step.VolumeM3.Value, 12);
            Assert.EndsWith("RS-000_参数化阵列样例.step", step.File);
            Assert.All(expected.Parts, p => Assert.All(p.Configurations, c => Assert.False(string.IsNullOrWhiteSpace(c.Oracle))));
        }
    }
}
