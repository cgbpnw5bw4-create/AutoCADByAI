using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CadKernel;
using CadProjects.Imported;
using CadProjects.PlanetaryGear;
using Xunit;

namespace CadTests
{
    /// <summary>模型路径长度的前置检查 (PathLimit), 数据取自 docs/api_calibration.md 第 4 节第 10 条</summary>
    public class PathLimitTests
    {
        static readonly Encoding Gbk = CodePagesEncodingProvider.Instance.GetEncoding(936);
        const string A14 = @"models\装配\A14_S型热电偶_插深长度280mm冷端长度70mm总长350mm丝径0.4防水盒+M27螺纹+刚玉管_江苏新宇宁.SLDASM";

        [Fact]
        public void CalibratedIncidentIsOverAndMainDirectoryIsVerified()
        {
            // 主目录: 2026-10-16 基准运行全流程通过; worktree: 2026-10-17 出图挂起 4 次 4 次
            string main = @"D:\07_WorkSpace\Codex\AutoCADByAI\AI_Mechanical_Engineering_Agent_Platform\output\20261016_113139_microwave_furnace\" + A14;
            string deep = @"D:\07_WorkSpace\Codex\AutoCADByAI\.claude\worktrees\zealous-galileo-c68889\AI_Mechanical_Engineering_Agent_Platform\output\20261017_090000_microwave_furnace\" + A14;
            Assert.Equal(191, main.Length);
            Assert.Equal(222, PathLimit.Bytes(main, Gbk));
            Assert.Equal(232, deep.Length);
            Assert.Equal(263, PathLimit.Bytes(deep, Gbk));
            Assert.Equal(PathLimit.Level.Ok, PathLimit.Classify(PathLimit.Bytes(main, Gbk)));
            Assert.Equal(PathLimit.Level.Over, PathLimit.Classify(PathLimit.Bytes(deep, Gbk)));
        }

        [Fact]
        public void LevelsFollowMaxPathAndTheVerifiedLength()
        {
            Assert.Equal(PathLimit.Level.Ok, PathLimit.Classify(PathLimit.Verified));
            Assert.Equal(PathLimit.Level.Unverified, PathLimit.Classify(PathLimit.Verified + 1));
            Assert.Equal(PathLimit.Level.Unverified, PathLimit.Classify(259));          // 259 字节 + 结尾 NUL 正好填满 MAX_PATH
            Assert.Equal(PathLimit.Level.Over, PathLimit.Classify(260));
        }

        [Fact]
        public void LongestIsByAnsiBytesNotCharacters()
        {
            string run = @"C:\r";
            var e = PathLimit.Longest(run, new[] { @"外购件\" + new string('a', 30) + ".SLDPRT", @"装配\" + new string('长', 20) + ".SLDASM" }, "测试", Gbk);
            Assert.EndsWith(new string('长', 20) + ".SLDASM", e.FullPath);           // 字符少, 字节多
            Assert.Equal(PathLimit.Bytes(e.FullPath, Gbk), e.Bytes);
            Assert.Equal(e.FullPath.Length, e.Chars);
            Assert.Equal(2, e.Files);
            // 已是完整路径的 (续跑时运行目录里已有的文件) 原样计入; 没有文件时只算 models 目录
            Assert.Equal(@"C:\r\models\a.SLDPRT", PathLimit.Longest(run, new[] { @"C:\r\models\a.SLDPRT" }, "测试", Gbk).FullPath);
            var none = PathLimit.Longest(run, null, null, Gbk);
            Assert.Equal(@"C:\r\models", none.FullPath);
            Assert.Null(none.Source);
        }

        [Fact]
        public void AdviceSuggestsSubstOnlyWhenItHelps()
        {
            string root = @"D:\07_WorkSpace\Codex\AutoCADByAI\.claude\worktrees\zealous-galileo-c68889\AI_Mechanical_Engineering_Agent_Platform";
            var deep = PathLimit.Longest(root + @"\output\20261017_090000_microwave_furnace", new[] { A14.Substring(@"models\".Length) }, "输入任务", Gbk);
            Assert.Equal(263, deep.Bytes);
            var a = PathLimit.Advice(deep, root, false, Gbk);
            Assert.Contains(a, s => s.Contains("须缩短 4 字节以上"));
            Assert.Contains(a, s => s.Contains("subst W: \"" + root + "\"") && s.Contains("映射后最长约 150 字节"));
            Assert.DoesNotContain(a, s => s.Contains("续跑"));
            Assert.Contains(PathLimit.Advice(deep, root, true, Gbk), s => s.Contains("续跑须用新建运行时的同一盘符"));
            // 平台已在盘符根下仍超长: 映射无济于事, 只能缩短名称
            var shortRoot = PathLimit.Longest(@"W:\output\20261017_090000_t", new[] { new string('长', 125) + ".SLDASM" }, "输入任务", Gbk);
            Assert.Equal(PathLimit.Level.Over, shortRoot.Level);
            Assert.Contains(PathLimit.Advice(shortRoot, @"W:\", false, Gbk), s => s.Contains("须缩短零件 / 装配名称"));
            // 主目录: 不提示
            var main = PathLimit.Longest(@"D:\07_WorkSpace\Codex\AutoCADByAI\AI_Mechanical_Engineering_Agent_Platform\output\20261016_113139_microwave_furnace",
                                         new[] { A14.Substring(@"models\".Length) }, "输入任务", Gbk);
            Assert.Empty(PathLimit.Advice(main, @"D:\07_WorkSpace\Codex\AutoCADByAI\AI_Mechanical_Engineering_Agent_Platform", false, Gbk));
        }

        [Fact]
        public void ImportedDesignDeclaresTheSameFileNamesItBuilds()
        {
            var id = Xf.T(0, 0, 0).ToRow();
            var m = new ImportModel { Root = "A01" };
            ImportGeometry Tube() { return new ImportGeometry { Type = "tube", Axis = "Z", Center = new[] { 0.0, 0 }, Dout = 12, Din = 9, Length = 100 }; }
            for (int i = 1; i <= 3; i++)
                m.Parts.Add(new ImportPart { Id = "P00" + i, OriginalName = "RTWBL340A100" + i + " 管" + i, Kind = "自制件", Code = "RTWBL340A100" + i, Name = "管" + i, Material = "S304", Model = Tube() });
            m.Parts.Add(new ImportPart { Id = "P004", OriginalName = "卡套 M12", Kind = "外购件", Code = "WG-001", Name = "卡套", Material = "S304", Model = Tube() });
            m.Assemblies.Add(new ImportAsm { Id = "A01", Name = "微波炉 总装", Children = { new ImportChild { Ref = "A02", Xf = id }, new ImportChild { Ref = "A04", Xf = id } } });
            m.Assemblies.Add(new ImportAsm { Id = "A02", Name = "RTWBL340A1000_电源模块", Children = { new ImportChild { Ref = "A03", Xf = id }, new ImportChild { Ref = "P001", Xf = id } } });
            m.Assemblies.Add(new ImportAsm { Id = "A03", Name = "A14_S型热电偶 江苏", Children = { new ImportChild { Ref = "P004", Xf = id } } });
            m.Assemblies.Add(new ImportAsm { Id = "A04", Name = "RTWBL340A1000 电源模块", Children = { new ImportChild { Ref = "P002", Xf = id }, new ImportChild { Ref = "P003", Xf = id } } });
            string d = Path.Combine(Path.GetTempPath(), "cadtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, ImportModel.FileName), m.ToJson());
            File.WriteAllText(Path.Combine(d, "design.json"), "{ \"project\": \"imported\" }");
            var p = new ImportedAssembly();
            p.Bind(DesignInput.Load(d));
            var files = p.ModelFiles().ToList();         // 未调 Validate 也能估算 (cad doctor)
            var expected = new[]
            {
                @"自制件\RTWBL340A1001_管1.SLDPRT", @"自制件\RTWBL340A1002_管2.SLDPRT", @"自制件\RTWBL340A1003_管3.SLDPRT", @"外购件\WG-001_卡套.SLDPRT",
                @"微波炉_总装.SLDASM",                                   // 根装配 = 名称
                @"装配\RTWBL340A1000_电源模块.SLDASM",                   // 本设备图号开头: 图号_名称
                @"装配\A03_A14_S型热电偶_江苏.SLDASM",                   // 其余: 编号_名称
                @"装配\A04_RTWBL340A1000_电源模块.SLDASM",               // 与先建的 A02 重名 → 编号_名称 (子装配先于父装配, 按子项顺序)
            };
            Assert.Equal(expected.OrderBy(x => x, StringComparer.Ordinal), files.OrderBy(x => x, StringComparer.Ordinal));
            Assert.Null(new PlanetaryGearbox().ModelFiles());                 // 在建模代码中命名的设计: 运行前不知道
        }
    }

    /// <summary>与运行目录有关的部分 (Run 是全局状态, 与 RunTests 串行)</summary>
    [Collection("Run")]
    public class PathLimitRunTests
    {
        static string NewRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "cadrun_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        [Fact]
        public void NamesComeFromTheLastRunWhenTheDesignNamesThemInCode()
        {
            string root = NewRoot();
            Run.Init(root);
            var p = new PlanetaryGearbox();
            var none = PathLimit.ForRun(p, "demo");
            Assert.Null(none.Source);
            Assert.Equal(Path.Combine(Run.OutputRoot, Run.NewDirName("demo"), "models").Length, none.FullPath.Length);   // 运行目录名长度固定
            foreach (string run in new[] { "20260101_080000_demo", "20260102_080000_demo", "20260103_080000_demo2" })
            {
                string sub = Path.Combine(Run.OutputRoot, run, "models", "零件");
                Directory.CreateDirectory(sub);
                File.WriteAllText(Path.Combine(sub, run + "_齿轮.SLDPRT"), "x");
                File.WriteAllText(Path.Combine(sub, "~$" + run + "_齿轮加长加长.SLDPRT"), "x");    // SolidWorks 的临时文件不算
            }
            var last = PathLimit.ForRun(p, "demo");
            Assert.Equal("上次运行 20260102_080000_demo", last.Source);                // 同名任务中最近的一次; demo2 是别的任务
            Assert.Equal(Path.Combine(last.RunDir, "models", "零件", "20260102_080000_demo_齿轮.SLDPRT"), last.FullPath);
            Assert.NotEqual(Path.Combine(Run.OutputRoot, "20260102_080000_demo"), last.RunDir);   // 估算的是新运行目录
            // 续跑: 运行目录里已有的文件
            string resume = Path.Combine(Run.OutputRoot, "20260101_080000_demo");
            var r = PathLimit.ForRun(p, "demo", resume);
            Assert.Equal("运行目录现有文件", r.Source);
            Assert.Equal(Path.Combine(resume, "models", "零件", "20260101_080000_demo_齿轮.SLDPRT"), r.FullPath);
        }

        /// <summary>只含一个名称很长的根装配的导入设计 (直管零件, 不需要几何文件)</summary>
        static DesignInput LongNamedImport(string steps)
        {
            var id = Xf.T(0, 0, 0).ToRow();
            var m = new ImportModel { Root = "A01" };
            m.Parts.Add(new ImportPart { Id = "P001", Kind = "外购件", Code = "WG-001", Name = "管", Material = "S304",
                                         Model = new ImportGeometry { Type = "tube", Axis = "Z", Center = new[] { 0.0, 0 }, Dout = 12, Din = 9, Length = 100 } });
            m.Assemblies.Add(new ImportAsm { Id = "A01", Name = new string('长', 130), Children = { new ImportChild { Ref = "P001", Xf = id } } });
            string d = Path.Combine(Path.GetTempPath(), "cadtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, ImportModel.FileName), m.ToJson());
            File.WriteAllText(Path.Combine(d, "design.json"), "{ \"project\": \"imported\"" + (steps == null ? "" : ", \"steps\": " + steps) + " }");
            return DesignInput.Load(d);
        }

        [Fact]
        public void RunsThatDrawAreRefusedBeforeSolidWorksWhenPathsExceedMaxPath()
        {
            // 平台根放在一个文件"下面": 万一检查没拦住, Run.Begin 建目录即失败, 不会走到连接 SolidWorks
            string blocker = Path.Combine(NewRoot(), "not_a_dir");
            File.WriteAllText(blocker, "x");
            Run.Init(Path.Combine(blocker, "平台"));
            Assert.Equal(2, Pipeline.Execute(new ImportedAssembly(), LongNamedImport(null)));                 // 全部步骤 (含出图) → 不启动
            Assert.Equal(2, Pipeline.Execute(new ImportedAssembly(), LongNamedImport("[\"drawings\"]")));
            // 不出图的运行只提示, 照常往下走 (此处走到 Run.Begin 建目录失败, 说明没有被拦下)
            Assert.ThrowsAny<IOException>(() => Pipeline.Execute(new ImportedAssembly(), LongNamedImport("[\"parts\", \"assembly\"]")));
        }
    }
}
