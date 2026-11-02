using System;
using System.Collections.Generic;
using System.IO;
using CadKernel;
using Xunit;

namespace CadTests
{
    public class ImportPublishTests
    {
        [Theory]
        [InlineData("零件实例数不符: 写入 1, SolidWorks 中 2")]
        [InlineData("导入跳过零部件 1 个")]
        public void IntegrityWarningsDoNotReplaceExistingInput(string warning)
        {
            using var f = new Fixture();
            var model = Model();
            model.Warnings.Add(warning);
            Assert.Contains(CadCli.Program.ValidateAndCommitImport(f.Task, f.Stage, model), p => p.Contains(warning));
            f.AssertOldInput();
        }

        [Fact]
        public void MissingStagedGeometryDoesNotUseOldFileOrReplaceInput()
        {
            using var f = new Fixture();
            var model = Model();
            model.Parts[0].Model.File = "geometry/old.x_t";
            Assert.NotEmpty(CadCli.Program.ValidateAndCommitImport(f.Task, f.Stage, model));
            f.AssertOldInput();
        }

        [Fact]
        public void ValidStagedModelIsCommittedByTheSameEntryPoint()
        {
            using var f = new Fixture();
            Assert.Empty(CadCli.Program.ValidateAndCommitImport(f.Task, f.Stage, Model()));
            Assert.Equal("new geometry", File.ReadAllText(Path.Combine(f.Task, "geometry", "new.x_t")));
            Assert.False(File.Exists(Path.Combine(f.Task, "geometry", "old.x_t")));
            Assert.Empty(ImportModel.Load(f.Task).Warnings);
        }

        static ImportModel Model()
        {
            return new ImportModel
            {
                Root = "A1",
                Parts = new List<ImportPart> { new ImportPart { Id = "P1", Kind = "外购件", Material = "UNKNOWN",
                    Model = new ImportGeometry { Type = "geometry", File = "geometry/new.x_t" } } },
                Assemblies = new List<ImportAsm> { new ImportAsm { Id = "A1", Name = "测试装配",
                    Children = new List<ImportChild> { new ImportChild { Ref = "P1", Xf = Xf.T(0, 0, 0).ToRow() } } } },
                World = new Dictionary<string, List<double[]>> { { "P1", new List<double[]> { Xf.T(0, 0, 0).ToRow() } } },
            };
        }

        sealed class Fixture : IDisposable
        {
            public readonly string Task = Path.Combine(Path.GetTempPath(), "cad_import_publish_" + Guid.NewGuid().ToString("N"));
            public string Stage { get { return Path.Combine(Task, ".geometry_importing"); } }

            public Fixture()
            {
                Directory.CreateDirectory(Stage);
                Directory.CreateDirectory(Path.Combine(Task, "geometry"));
                File.WriteAllText(Path.Combine(Stage, "new.x_t"), "new geometry");
                File.WriteAllText(Path.Combine(Task, "geometry", "old.x_t"), "old geometry");
                File.WriteAllText(Path.Combine(Task, "import.json"), "old import");
            }

            public void AssertOldInput()
            {
                Assert.Equal("old geometry", File.ReadAllText(Path.Combine(Task, "geometry", "old.x_t")));
                Assert.Equal("old import", File.ReadAllText(Path.Combine(Task, "import.json")));
                Assert.Equal("new geometry", File.ReadAllText(Path.Combine(Stage, "new.x_t")));
            }

            public void Dispose() { Directory.Delete(Task, true); }
        }
    }
}
