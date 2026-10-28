using System;
using System.IO;
using System.Linq;
using CadKernel;
using CadProjects.PlanetaryGear;
using Xunit;

namespace CadTests
{
    /// <summary>只在临时目录模拟步骤与检查, 不连接 SolidWorks。</summary>
    [Collection("Run")]
    public class ResumeTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadresume_" + Guid.NewGuid().ToString("N"));

        public ResumeTests()
        {
            Run.Init(root);
            Run.Begin("demo", "demo", "续跑测试", null);
        }

        public void Dispose()
        {
            Run.CloseLog();
            Directory.Delete(root, true);
        }

        static void CompleteRun()
        {
            Pipeline.PrepareSteps(Pipeline.AllSteps, false);
            foreach (var s in Run.Manifest.Steps) s.Status = "passed";
            foreach (string name in new[] { "参数校验", "模型完整性", "项目专属校核", "工程图生成", "工程图参考", "STEP 身份与内容",
                                             "图纸版面审核", "外形尺寸读数与几何一致", "图纸合订", Baseline.CheckName })
                Run.Check(name, true, "旧结果");
            Run.Metric("物料/质量kg", 10);
            Run.Metric("导入/特征重建实体数", 2);
            Run.Metric("干涉/已登记接受", 3);
            Run.Metric("STEP/实体数", 4);
            Run.Finish(true);
            Assert.Equal("passed", Run.Manifest.Status);
        }

        [Theory]
        [InlineData("failed")]
        [InlineData("running")]
        [InlineData("pending")]
        public void FinishRejectsIncompleteStepsWithoutBaseline(string status)
        {
            Run.Manifest.Steps.Add(new StepRecord { Name = "零件", Status = status });
            Run.Check("模型完整性", true, "旧检查通过");
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
            Assert.Contains("零件", Run.Manifest.Error);
        }

        [Fact]
        public void FinishRejectsNoStepsAndStillRejectsFailedChecks()
        {
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
            Run.Step("零件", () => { });
            Run.Check("模型完整性", false, "检查失败");
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
            Run.Check("模型完整性", true, "重新检查通过");
            Run.Finish(true);
            Assert.Equal("passed", Run.Manifest.Status); // 显式部分运行仍可通过
        }

        [Fact]
        public void FailedRebuildCannotPassByResumingOnlyDrawingsAndLaterSteps()
        {
            CompleteRun();
            string dir = Run.Dir;
            Assert.False(File.Exists(Path.Combine(dir, "input", Baseline.FileName)));
            Run.CloseLog();
            Run.Resume(dir);
            Pipeline.PrepareSteps(new[] { "parts" }, true);
            Assert.Empty(Run.Manifest.Metrics);
            Assert.Equal(new[] { "参数校验" }, Run.Manifest.Checks.Select(c => c.Name));
            Assert.All(Run.Manifest.Steps, s => Assert.Equal("pending", s.Status));
            Assert.Throws<InvalidOperationException>(() => Run.Step("零件", () => throw new InvalidOperationException("重建失败")));
            Run.Finish(false, "重建失败");
            Run.CloseLog();

            Run.Resume(dir);
            Pipeline.PrepareSteps(new[] { "drawings", "export", "post" }, true);
            Run.Step("工程图", () => { });
            Run.Step("导出", () => { });
            Run.Step("后处理", () => { });
            Run.Check("工程图生成", true, "新结果");
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
            Assert.Contains("零件", Run.Manifest.Error);
            Assert.Contains("装配", Run.Manifest.Error);
            Assert.Contains("校核", Run.Manifest.Error);

            // 只有真正补完失效步骤后才能恢复成功。
            Pipeline.PrepareSteps(Pipeline.AllSteps, true);
            foreach (string name in Run.Manifest.Steps.Select(s => s.Name).ToList()) Run.Step(name, () => { });
            Run.Check("模型完整性", true, "重新检查通过");
            Run.Finish(true);
            Assert.Equal("passed", Run.Manifest.Status);
        }

        [Fact]
        public void DrawingsResumePreservesUpstreamResultsAndInvalidatesDownstream()
        {
            CompleteRun();
            Pipeline.PrepareSteps(new[] { "drawings", "export", "post" }, true);
            Assert.All(Run.Manifest.Steps.Take(3), s => Assert.Equal("passed", s.Status));
            Assert.All(Run.Manifest.Steps.Skip(3), s => Assert.Equal("pending", s.Status));
            Assert.Contains(Run.Manifest.Checks, c => c.Name == "模型完整性");
            Assert.Contains(Run.Manifest.Checks, c => c.Name == "项目专属校核");
            Assert.Equal(10, Run.Manifest.Metrics["物料/质量kg"].Value);
            Assert.DoesNotContain("STEP/实体数", Run.Manifest.Metrics.Keys);
            Assert.DoesNotContain(Run.Manifest.Checks, c => c.Name == Baseline.CheckName || c.Name == "工程图生成" || c.Name == "STEP 身份与内容");
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
            foreach (string name in new[] { "工程图", "导出", "后处理" }) Run.Step(name, () => { });
            Run.Finish(true);
            Assert.Equal("passed", Run.Manifest.Status);
        }

        [Fact]
        public void ChecksResumeRemovesConditionalAndProjectResults()
        {
            CompleteRun();
            Pipeline.PrepareSteps(new[] { "checks" }, true);
            Assert.Empty(Run.Manifest.Metrics);
            Assert.Equal(new[] { "参数校验" }, Run.Manifest.Checks.Select(c => c.Name));
            Run.Step("校核", () => Run.Check("模型完整性", true, "新结果"));
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status); // 被省略的旧导出、工程图等仍失效
        }

        [Fact]
        public void ExportResumePreservesModelAndDrawingChecks()
        {
            CompleteRun();
            Pipeline.PrepareSteps(new[] { "export", "post" }, true);
            Assert.Contains(Run.Manifest.Checks, c => c.Name == "工程图生成");
            Assert.Contains(Run.Manifest.Checks, c => c.Name == "模型完整性");
            Assert.Equal(10, Run.Manifest.Metrics["物料/质量kg"].Value);
            Assert.DoesNotContain("STEP/实体数", Run.Manifest.Metrics.Keys);
            Assert.DoesNotContain(Run.Manifest.Checks, c => c.Name == "图纸合订" || c.Name == Baseline.CheckName);
        }

        [Fact]
        public void PostResumePreservesStepMetricAndDoesNotReuseOldBaselineFailure()
        {
            CompleteRun();
            Run.Check(Baseline.CheckName, false, "旧基准不符");
            Pipeline.PrepareSteps(new[] { "post" }, true);
            Assert.Equal(4, Run.Manifest.Metrics["STEP/实体数"].Value);
            Assert.DoesNotContain(Run.Manifest.Checks, c => c.Name == Baseline.CheckName);
            Run.Step("后处理", () => { });
            Run.Check(Baseline.CheckName, false, "重新比对仍不符");
            Run.Finish(true);
            Assert.Equal("failed", Run.Manifest.Status);
        }

        [Fact]
        public void EmptyFilteredPlanIsRejectedBeforeChangingRunOrConnectingCad()
        {
            string inputDir = Path.Combine(root, "task");
            Directory.CreateDirectory(inputDir);
            File.WriteAllText(Path.Combine(inputDir, "design.json"), "{\"project\":\"planetary_gear\",\"steps\":[\"parts\"]}");
            byte[] before = File.ReadAllBytes(Path.Combine(Run.Dir, "run.json"));
            var input = DesignInput.Load(inputDir);
            Assert.Equal(2, Pipeline.Execute(new PlanetaryGearbox(), input, Run.Dir, "drawings"));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(Run.Dir, "run.json")));
            Assert.Equal(new[] { "parts" }, input.Steps);
            Assert.False(Directory.Exists(Path.Combine(root, ".cad")));
        }
    }
}
