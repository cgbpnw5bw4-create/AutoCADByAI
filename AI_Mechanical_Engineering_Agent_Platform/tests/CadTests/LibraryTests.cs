using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CadKernel;
using CadLibrary;
using Xunit;

namespace CadTests
{
    /// <summary>公开外购件编号必须有实际定义; 按需模式只登记, 不建模、不连接 SolidWorks。</summary>
    [Collection("Run")]
    public class LibraryTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadlibrary_" + Guid.NewGuid().ToString("N"));

        public LibraryTests()
        {
            Cat.Reset();
            Run.Init(root);
            Run.Begin("test", "test", "零件库登记测试", null);
        }

        public void Dispose()
        {
            Run.CloseLog();
            Cat.Reset();
            Directory.Delete(root, true);
        }

        [Fact]
        public void EveryPublicPurchasedPartIdentifierHasARegisteredDefinition()
        {
            var fields = typeof(Buy).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).ToList();
            Assert.Equal(26, fields.Count);
            var ids = fields.Select(f => (string)f.GetRawConstantValue()).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Cat.Lazy = true;
            Buy.BuildAll();
            foreach (var field in fields)
                Assert.True(Cat.Has((string)field.GetRawConstantValue()), "公开编号缺少定义: " + field.Name);
            Assert.Empty(Cat.Reg); // 只有定义, 没有调用任何建模委托。
            Assert.Empty(Directory.EnumerateFileSystemEntries(Run.Models));
        }

        [Fact]
        public void UnimplementedSamplesDoNotAdvertiseCallableIdentifiers()
        {
            foreach (string name in new[] { "BRAKE", "GEARMOTOR", "BLOWER", "OVENFAN", "USGEN", "CABINET", "PUMP", "BAGFILTER" })
                Assert.Null(typeof(Buy).GetField(name, BindingFlags.Public | BindingFlags.Static));
        }
    }
}
