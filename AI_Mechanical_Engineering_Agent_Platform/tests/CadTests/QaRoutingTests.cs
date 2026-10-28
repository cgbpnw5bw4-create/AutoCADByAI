using System;
using System.IO;
using CadKernel;
using CadProjects.PlanetaryGear;
using Xunit;

namespace CadTests
{
    /// <summary>只在临时目录验证 output / qa 路由、归档边界与清单, 不连接 SolidWorks。</summary>
    [Collection("Run")]
    public sealed class QaRoutingTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadqarouting_" + Guid.NewGuid().ToString("N"));

        public QaRoutingTests()
        {
            Directory.CreateDirectory(root);
            Run.Init(root);
        }

        public void Dispose()
        {
            Run.CloseLog();
            Directory.Delete(root, true);
        }

        [Fact]
        public void ModelingKeepsOutputWhileRegressionUsesQa()
        {
            string normal = Run.Begin("demo", "demo", "普通运行", null);
            Assert.Equal(Run.OutputRoot, Path.GetDirectoryName(normal));
            Assert.Matches(@"^\d{8}_\d{6}_demo$", Path.GetFileName(normal));
            Assert.Equal("modeling", Run.Manifest.Purpose);
            Assert.False(Directory.Exists(Run.QaRoot));
            Run.CloseLog();

            string regression = Run.Begin("demo", "demo", "回归运行", null, RunPurpose.Regression);
            Assert.Equal(Run.QaRoot, Path.GetDirectoryName(regression));
            Assert.Matches(@"^\d{8}_\d{6}_reg_demo$", Path.GetFileName(regression));
            Assert.Equal("regression", Run.Manifest.Purpose);
            Assert.NotEqual(normal, regression);
        }

        [Fact]
        public void VerificationGetsANewQaDirectoryAndOnlyReferencesItsSource()
        {
            string source = Path.Combine(root, "output", "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "run.json"), "源运行不改写");
            byte[] before = File.ReadAllBytes(Path.Combine(source, "run.json"));

            string verification = Run.Begin("demo", "verification", "独立复验", null, RunPurpose.Verification, source);
            Assert.Equal(Run.QaRoot, Path.GetDirectoryName(verification));
            Assert.Matches(@"^\d{8}_\d{6}_verify_demo$", Path.GetFileName(verification));
            Assert.Equal("verification", Run.Manifest.Purpose);
            Assert.Equal(Path.GetFullPath(source), Run.Manifest.SourceRun);
            Assert.Empty(Directory.GetFiles(Run.InputSnapshot, "*", SearchOption.AllDirectories));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(source, "run.json")));
        }

        [Fact]
        public void ArchivedRunCannotBeResumedOrHaveItsLogCreated()
        {
            string live = Run.Begin("demo", "demo", "回归运行", null, RunPurpose.Regression);
            Run.CloseLog();
            string archive = Path.Combine(root, "qa", "archive", "20260101_080000_reg_demo");
            Directory.CreateDirectory(archive);
            string manifest = Path.Combine(archive, "run.json");
            File.Copy(Path.Combine(live, "run.json"), manifest);
            byte[] before = File.ReadAllBytes(manifest);

            Assert.Throws<IOException>(() => Run.Resume(archive));
            Assert.Equal(live, Run.Dir);
            Assert.Equal(before, File.ReadAllBytes(manifest));
            Assert.False(File.Exists(Path.Combine(archive, "run.log")));
            Assert.True(Run.IsArchivedRun(Path.Combine(archive, "models", "part.SLDPRT")));
        }

        [Fact]
        public void LiveQaRunCanResumeWithItsPurposeAndMetrics()
        {
            string live = Run.Begin("demo", "demo", "回归运行", null, RunPurpose.Regression);
            Run.Step("零件", () => { });
            Run.Metric("物料/质量kg", 4.2);
            Run.Finish(true);
            Run.CloseLog();
            Run.Resume(live);
            Assert.Equal("running", Run.Manifest.Status);
            Assert.Equal("regression", Run.Manifest.Purpose);
            Assert.Equal(4.2, Run.Manifest.Metrics["物料/质量kg"].Value);
            Assert.Equal(live, Run.Dir);
            Assert.False(Directory.Exists(Run.OutputRoot));
        }

        [Fact]
        public void OwnedPathsRequireRealDirectoryBoundariesAndExcludeArchive()
        {
            foreach (string path in new[] { "output/run/models/part.SLDPRT", "qa/run/models/part.SLDPRT", "qa/archive_like/part.SLDPRT" })
                Assert.True(Run.IsOwnedOutputPath(Path.Combine(root, path)), path);
            foreach (string path in new[] { "output_neighbour/part.SLDPRT", "qa_neighbour/part.SLDPRT", "qa/archive/run/models/part.SLDPRT", "input/part.SLDPRT" })
                Assert.False(Run.IsOwnedOutputPath(Path.Combine(root, path)), path);
            Assert.False(Run.IsOwnedOutputPath(null));
            Assert.False(Run.IsOwnedOutputPath(Path.Combine(root, "output", "..", "input", "part.SLDPRT")));
        }

        string AddRun(string relative, string title)
        {
            string dir = Path.GetFullPath(Path.Combine(root, relative));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "run.json"), "{\"status\":\"passed\",\"title\":\"" + title + "\"}");
            return dir;
        }

        [Fact]
        public void CliFindsQaAndArchiveAndListsThemInSeparateGroups()
        {
            string normal = AddRun("output/20260101_080000_demo", "交付示例");
            string live = AddRun("qa/20260101_080000_reg_demo", "验证示例");
            string archive = AddRun("qa/archive/20250101_080000_reg_demo", "归档示例");
            Assert.Equal(normal, CadCli.Program.RunDir(root, Path.GetFileName(normal)));
            Assert.Equal(live, CadCli.Program.RunDir(root, Path.GetFileName(live)));
            Assert.Equal(archive, CadCli.Program.RunDir(root, Path.GetFileName(archive)));
            Assert.Equal(archive, CadCli.Program.RunDir(root, "qa/archive/" + Path.GetFileName(archive)));

            var previous = Console.Out;
            using var text = new StringWriter();
            try { Console.SetOut(text); Assert.Equal(0, CadCli.Program.List(root)); }
            finally { Console.SetOut(previous); }
            string output = text.ToString();
            int delivery = output.IndexOf("交付运行 (output/)", StringComparison.Ordinal);
            int qa = output.IndexOf("活动验证 (qa/)", StringComparison.Ordinal);
            int history = output.IndexOf("历史验证 (qa/archive/", StringComparison.Ordinal);
            Assert.True(delivery >= 0 && delivery < qa && qa < history);
            Assert.InRange(output.IndexOf("交付示例", StringComparison.Ordinal), delivery, qa - 1);
            Assert.InRange(output.IndexOf("验证示例", StringComparison.Ordinal), qa, history - 1);
            Assert.True(output.IndexOf("归档示例", StringComparison.Ordinal) > history);
        }

        [Fact]
        public void ListDoesNotCreateMissingOutputOrQaDirectories()
        {
            var previous = Console.Out;
            using var text = new StringWriter();
            try { Console.SetOut(text); Assert.Equal(0, CadCli.Program.List(root)); }
            finally { Console.SetOut(previous); }
            Assert.False(Directory.Exists(Run.OutputRoot));
            Assert.False(Directory.Exists(Run.QaRoot));
        }

        [Fact]
        public void PathEstimateUsesActualRegressionRouteAndIgnoresVerifyAndArchive()
        {
            foreach (string relative in new[] { "output/20260101_080000_demo", "qa/20260102_080000_reg_demo", "qa/20260103_080000_verify_demo", "qa/archive/20260104_080000_reg_demo" })
            {
                string models = Path.Combine(root, relative, "models");
                Directory.CreateDirectory(models);
                File.WriteAllText(Path.Combine(models, Path.GetFileName(Path.GetDirectoryName(models)) + ".SLDPRT"), "x");
            }
            var project = new PlanetaryGearbox();
            var normal = PathLimit.ForRun(project, "demo");
            var regression = PathLimit.ForRun(project, "demo", regression: true);
            Assert.Equal("上次运行 20260101_080000_demo", normal.Source);
            Assert.Equal("上次运行 20260102_080000_reg_demo", regression.Source);
            Assert.Equal(Run.OutputRoot, Path.GetDirectoryName(normal.RunDir));
            Assert.Equal(Run.QaRoot, Path.GetDirectoryName(regression.RunDir));
            Assert.Contains("_reg_demo", regression.RunDir);
            Assert.Contains("20260102_080000_reg_demo.SLDPRT", regression.FullPath);
            Assert.Equal(PathLimit.Bytes(regression.FullPath), regression.Bytes);
            Assert.NotEqual(normal.FullPath, regression.FullPath);
        }

        [Fact]
        public void FirstRegressionCanReuseDeliveryNamesWithoutUsingItsOutputDirectory()
        {
            string previous = Path.Combine(Run.OutputRoot, "20260101_080000_demo", "models");
            Directory.CreateDirectory(previous);
            File.WriteAllText(Path.Combine(previous, "零件.SLDPRT"), "x");
            var estimate = PathLimit.ForRun(new PlanetaryGearbox(), "demo", regression: true);
            Assert.Equal("上次运行 20260101_080000_demo", estimate.Source);
            Assert.Equal(Run.QaRoot, Path.GetDirectoryName(estimate.RunDir));
            Assert.Equal(Path.Combine(estimate.RunDir, "models", "零件.SLDPRT"), estimate.FullPath);
        }
    }
}
