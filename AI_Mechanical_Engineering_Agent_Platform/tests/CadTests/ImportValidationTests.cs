using System;
using System.IO;
using CadKernel;
using CadProjects.Imported;
using Xunit;

namespace CadTests
{
    /// <summary>只用临时文件核对导入完整性与发布前的几何存在性, 不连接 SolidWorks。</summary>
    [Collection("Run")]
    public sealed class ImportValidationTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadimportvalidation_" + Guid.NewGuid().ToString("N"));

        public ImportValidationTests() { Directory.CreateDirectory(root); }

        public void Dispose()
        {
            Run.CloseLog();
            Directory.Delete(root, true);
        }

        static ImportModel Model()
        {
            var m = new ImportModel { Root = "A01" };
            m.Parts.Add(new ImportPart
            {
                Id = "P001", Code = "WG-001", Name = "管", Kind = "外购件", Material = "S304", Bodies = 1,
                Model = new ImportGeometry { Type = "tube", Axis = "Z", Center = new[] { 0.0, 0 }, Dout = 12, Din = 9, Length = 100 },
            });
            m.Assemblies.Add(new ImportAsm { Id = "A01", Name = "导入装配", Children = { new ImportChild { Ref = "P001", Xf = Xf.T(0, 0, 0).ToRow() } } });
            m.World["P001"] = new System.Collections.Generic.List<double[]> { Xf.T(0, 0, 0).ToRow() };
            return m;
        }

        string Save(ImportModel model)
        {
            string dir = Path.Combine(root, "task");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ImportModel.FileName), model.ToJson());
            File.WriteAllText(Path.Combine(dir, "design.json"), "{\"project\":\"imported\"}");
            return dir;
        }

        [Theory]
        [InlineData("零件实例数不符: 写入 2, SolidWorks 中 3")]
        [InlineData("原模型干涉中有 1 处无法对应到零件, 未登记 (运行时会按新增报出)")]
        [InlineData("跳过零部件 1 个 (压缩或未还原), 导入完整性未确认; 详情见导入日志")]
        public void PersistedCompletenessWarningsBlockValidation(string warning)
        {
            var model = Model();
            Assert.Empty(model.Validate(null));
            model.Warnings.Add(warning);
            string dir = Save(model);
            var loaded = ImportModel.Load(dir);
            Assert.Equal(new[] { warning }, loaded.Warnings);
            Assert.Contains(loaded.Validate(dir), p => p.Contains("导入完整性问题") && p.Contains(warning));
            var project = new ImportedAssembly();
            project.Bind(DesignInput.Load(dir));
            Assert.Contains(project.Validate(), p => p.Contains(warning));
        }

        [Fact]
        public void WarningsRejectRunBeforeOutputCreationOrCadConnection()
        {
            var model = Model();
            model.Warnings.Add("零件实例数不符: 写入 2, SolidWorks 中 3");
            string inputDir = Save(model);
            // 万一前置校验失效, Run.Begin 也会因上级是文件而失败, 不会进入 Sw.Connect。
            string blocker = Path.Combine(root, "not_a_directory");
            File.WriteAllText(blocker, "x");
            Run.Init(Path.Combine(blocker, "platform"));
            Assert.Equal(2, Pipeline.Execute(new ImportedAssembly(), DesignInput.Load(inputDir)));
            Assert.False(Directory.Exists(Run.OutputRoot));
            Assert.False(Directory.Exists(Run.StateDir));
        }

        [Fact]
        public void StagedGeometryIsCheckedInsteadOfTheOldPublishedGeometry()
        {
            var model = Model();
            model.Parts[0].Model = new ImportGeometry { Type = "geometry", File = "geometry/P001.x_t" };
            string dir = Save(model);
            string stage = Path.Combine(dir, ".geometry_importing");
            Directory.CreateDirectory(stage);
            string stagedFile = Path.Combine(stage, "P001.x_t");
            File.WriteAllText(stagedFile, "暂存几何");
            Assert.Empty(model.Validate(dir, stage));
            Assert.Contains(model.Validate(dir), p => p.Contains("几何文件不存在"));

            Directory.CreateDirectory(Path.Combine(dir, "geometry"));
            File.WriteAllText(Path.Combine(dir, "geometry", "P001.x_t"), "旧几何");
            File.Delete(stagedFile);
            Assert.Empty(model.Validate(dir));
            Assert.Contains(model.Validate(dir, stage), p => p.Contains("几何文件不存在"));
        }
    }
}
