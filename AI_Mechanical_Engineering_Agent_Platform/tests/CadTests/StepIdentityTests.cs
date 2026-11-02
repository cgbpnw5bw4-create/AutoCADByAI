using System;
using System.Linq;
using CadKernel;
using Xunit;

namespace CadTests
{
    /// <summary>只核对 STEP 文本与明确的零件/配置身份, 不连接 SolidWorks。</summary>
    public class StepIdentityTests
    {
        static string Step(params string[] products)
        {
            return "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n"
                + string.Join("\n", products.Select((p, i) => "#" + (i + 1) + "=PRODUCT('" + p.Replace("'", "''") + "','','',());"))
                + "\n#100=MANIFOLD_SOLID_BREP('body',#101);\nENDSEC;\nEND-ISO-10303-21;";
        }

        [Theory]
        [InlineData("part", true)]
        [InlineData("part_Base", true)]
        [InlineData("part_Wide", false)]
        [InlineData("part_Base_extra", false)]
        [InlineData("part_Base2", false)]
        [InlineData("other_part_Base", false)]
        public void SingleReferencedConfigurationAcceptsOnlyExactPlainOrConfiguredName(string product, bool expected)
        {
            bool actual = StepCheck.VerifyConfiguredText(Step("root", product), "root", new[] { ("part", "Base") }, out string summary);
            Assert.Equal(expected, actual);
            if (!expected) Assert.Contains("缺少零件/配置", summary);
        }

        [Fact]
        public void MultipleReferencedConfigurationsMustEachHaveTheirOwnExactProduct()
        {
            var references = new[] { ("part", "Base"), ("part", "Wide"), ("part", "Base") };
            Assert.True(StepCheck.VerifyConfiguredText(Step("root", "part_Base", "part_Wide"), "root", references, out string summary), summary);
            Assert.Contains("2 项", summary); // 同配置的重复实例只需要同一产品定义。
        }

        [Theory]
        [InlineData("part", "part_Base")]
        [InlineData("part", "part_Wide")]
        [InlineData("part_Base", "part_Other")]
        [InlineData("part_Base_extra", "part_Wide")]
        public void PlainNameOrWrongConfigurationCannotHideAnotherMissingConfiguration(string first, string second)
        {
            Assert.False(StepCheck.VerifyConfiguredText(Step("root", first, second), "root",
                new[] { ("part", "Base"), ("part", "Wide") }, out string summary));
            Assert.Contains("缺少零件/配置", summary);
        }

        [Fact]
        public void ConfiguredPartsDoNotRelaxRootIdentityOrStepStructure()
        {
            var references = new[] { ("part", "Base") };
            Assert.False(StepCheck.VerifyConfiguredText(Step("root_Base", "part_Base"), "root", references, out string wrongRoot));
            Assert.Contains("根产品名不是 root", wrongRoot);
            Assert.False(StepCheck.VerifyConfiguredText("not STEP", "root", references, out string invalid));
            Assert.Contains("文件头不是", invalid);
            Assert.Contains("没有实体", invalid);
        }

        [Fact]
        public void AllSixBaseProductsAreMatchedWithoutAcceptingOtherConfigurations()
        {
            var parts = Enumerable.Range(1, 6).Select(i => ("RS-00" + i + "_样例", "Base")).ToArray();
            string[] products = new[] { "RS-000_总装" }.Concat(parts.Select(p => p.Item1 + "_Base")).ToArray();
            Assert.True(StepCheck.VerifyConfiguredText(Step(products), products[0], parts, out string summary), summary);
            products[4] = parts[3].Item1 + "_Wide";
            Assert.False(StepCheck.VerifyConfiguredText(Step(products), products[0], parts, out _));
        }

        [Fact]
        public void ExistingVerifyTextStillRequiresOriginalExactNames()
        {
            Assert.True(StepCheck.VerifyText(Step("root", "part"), "root", new[] { "part" }, out _));
            Assert.False(StepCheck.VerifyText(Step("root", "part_Base"), "root", new[] { "part" }, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public void UnknownReferencedConfigurationDoesNotBecomeAWildcard(string configuration)
        {
            Assert.False(StepCheck.VerifyConfiguredText(Step("root", "part"), "root",
                new[] { ("part", configuration) }, out string summary));
            Assert.Contains("被引用配置缺失", summary);
        }
    }
}
