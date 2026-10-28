using System;
using System.Globalization;
using System.IO;
using CadKernel;
using Xunit;

namespace CadTests
{
    /// <summary>紧固件组选长、完整夹厚标识与实际缓存路径; 不连接 SolidWorks。</summary>
    [Collection("Run")]
    public class FastenerTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadfastener_" + Guid.NewGuid().ToString("N"));

        public FastenerTests()
        {
            Cat.Reset(); Std.Reset();
            Run.Init(root);
            Run.Begin("test", "test", "紧固件缓存测试", null);
        }

        public void Dispose()
        {
            Run.CloseLog();
            Cat.Reset(); Std.Reset();
            Directory.Delete(root, true);
        }

        [Fact]
        public void NearbyGripValuesCanRequireDifferentBoltLengths()
        {
            var a = Std.DefineBoltNut(8, 10.40);
            var b = Std.DefineBoltNut(8, 10.42);
            Assert.Equal(25, a.Length);
            Assert.Equal(30, b.Length);
            Assert.NotEqual(a.Key, b.Key);
            Assert.NotEqual(a.FileName, b.FileName);
        }

        [Fact]
        public void TappedSetsAlsoKeepExactGripAcrossLengthBoundary()
        {
            var a = Std.DefineBoltTap(8, 13.30, 30);
            var b = Std.DefineBoltTap(8, 13.32, 30);
            Assert.Equal(25, a.Length);
            Assert.Equal(30, b.Length);
            Assert.NotEqual(a.Key, b.Key);
            Assert.NotEqual(a.FileName, b.FileName);
        }

        [Theory]
        [InlineData(false, 10.42, 10.44)]
        [InlineData(true, 10.40, 10.42)]
        public void SameBoltLengthStillNeedsDistinctGripPlacement(bool tap, double gripA, double gripB)
        {
            var a = tap ? Std.DefineBoltTap(8, gripA, 30) : Std.DefineBoltNut(8, gripA);
            var b = tap ? Std.DefineBoltTap(8, gripB, 30) : Std.DefineBoltNut(8, gripB);
            Assert.Equal(a.Length, b.Length);
            Assert.NotEqual(a.Key, b.Key);
            Assert.NotEqual(a.FileName, b.FileName);
        }

        [Theory]
        [InlineData(false, 10.40, 10.42)]
        [InlineData(false, 10.42, 10.44)]
        [InlineData(true, 13.30, 13.32)]
        [InlineData(true, 10.40, 10.42)]
        public void ProductionEntryPointsUseDistinctCachedFiles(bool tap, double gripA, double gripB)
        {
            var a = tap ? Std.DefineBoltTap(8, gripA, 30) : Std.DefineBoltNut(8, gripA);
            var b = tap ? Std.DefineBoltTap(8, gripB, 30) : Std.DefineBoltNut(8, gripB);
            int builds = 0;
            void Build(string file)
            {
                builds++;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, "模拟建组, 不调用 COM");
            }
            string pathA = Std.BuildSet(a, Build), pathB = Std.BuildSet(b, Build);
            Assert.Equal(2, builds);
            Assert.NotEqual(pathA, pathB);
            Assert.Equal(Path.Combine(Run.Models, "00_标准件", "紧固件组", a.FileName), pathA);
            Assert.Equal(pathA, Cat.Reg[a.Key]);
            Assert.Equal(pathB, Cat.Reg[b.Key]);
            // 公共生产入口须命中同一精确标识, 命中后不执行真实建模。
            Assert.Equal(pathA, tap ? Std.BoltTap(8, gripA, 30) : Std.BoltNut(8, gripA));
            Assert.Equal(pathB, tap ? Std.BoltTap(8, gripB, 30) : Std.BoltNut(8, gripB));
            Assert.Equal(pathA, Std.BuildSet(a, _ => throw new InvalidOperationException("重复建组")));
            Assert.Equal(2, builds);
        }

        [Theory]
        [InlineData("en-US")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        public void IdentifiersIgnoreCultureAndPreserveExistingIntegerFileNames(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Assert.Equal("FS_M8_G10.42", Std.DefineBoltNut(8, 10.42).Key);
                Assert.Equal("螺栓组_M8x30_夹厚10.42.SLDASM", Std.DefineBoltNut(8, 10.42).FileName);
                Assert.Equal("FS_M8_G10.0", Std.DefineBoltNut(8, 10).Key);
                Assert.Equal("螺栓组_M8x25_夹厚10.0.SLDASM", Std.DefineBoltNut(8, 10).FileName);
                Assert.Equal("FT_M8x25_G10.42", Std.DefineBoltTap(8, 10.42, 30).Key);
                Assert.Equal("螺钉组_M8x25_夹厚10.0_8.8.SLDASM", Std.DefineBoltTap(8, 10, 30, true).FileName);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Fact]
        public void GripIdentityRetainsMoreThanTwoDecimalPlaces()
        {
            var a = Std.DefineBoltNut(8, 10.421);
            var b = Std.DefineBoltNut(8, 10.422);
            Assert.Equal(a.Length, b.Length);
            Assert.NotEqual(a.Key, b.Key);
            Assert.NotEqual(a.FileName, b.FileName);
        }
    }
}
