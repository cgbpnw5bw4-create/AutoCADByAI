using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadKernel;
using CadProjects.PlanetaryGear;
using Xunit;

namespace CadTests
{
    public class KernelTests
    {
        static string TempDir()
        {
            string d = Path.Combine(Path.GetTempPath(), "cadtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            return d;
        }

        static DesignInput Input(string json)
        {
            string d = TempDir();
            File.WriteAllText(Path.Combine(d, "design.json"), json);
            return DesignInput.Load(d);
        }

        // ---------------- 设计输入 ----------------
        [Fact]
        public void DesignInputParsesCommentsAndParams()
        {
            var i = Input("{ // 注释\n \"project\": \"planetary_gear\", \"title\": \"t\", \"steps\": [\"parts\"], \"params\": { \"module\": 2.5, \"z_sun\": 20 } }");
            Assert.Equal("planetary_gear", i.Project);
            Assert.Equal(new[] { "parts" }, i.Steps);
            Assert.Equal(2.5, i.Num("module", 0));
            Assert.Equal(20, i.Int("z_sun", 0));
            Assert.Equal(7, i.Int("absent", 7));
            Assert.Throws<Exception>(() => i.Int("module", 0));                 // 2.5 不是整数
            Assert.Equal(new[] { "z_sun" }, i.Unknown(new[] { "module" }));
        }

        [Fact]
        public void DesignInputRequiresProject()
        {
            Assert.ThrowsAny<Exception>(() => Input("{ \"params\": {} }"));
        }

        // ---------------- 设计项目参数校验 (不连 SolidWorks) ----------------
        [Fact]
        public void PlanetaryDefaultsAreValidAndBadRatioIsRejected()
        {
            var p = new PlanetaryGearbox();
            p.Bind(Input("{ \"project\": \"planetary_gear\" }"));
            Assert.Empty(p.Validate());
            p.Bind(Input("{ \"project\": \"planetary_gear\", \"params\": { \"z_ring\": 70 } }"));
            Assert.Contains(p.Validate(), s => s.Contains("同心"));
        }

        /// <summary>input/ 下每个回归样例的参数都能通过校验 (不连 SolidWorks), 基准文件 (若有) 能解析</summary>
        [Fact]
        public void AllRegressionInputsValidate()
        {
            string root = Env.FindPlatformRoot(AppContext.BaseDirectory);
            var projects = new Dictionary<string, Func<CadProject>>
            {
                { "planetary_gear", () => new PlanetaryGearbox() },
                { "cfc650_module", () => new CadProjects.Cfc650.Cfc650Module() },
            };
            int n = 0;
            foreach (string d in Directory.GetDirectories(Path.Combine(root, "input")))
            {
                if (!File.Exists(Path.Combine(d, "design.json"))) continue;
                var i = DesignInput.Load(d);
                var p = projects[i.Project]();
                p.Bind(i);
                Assert.Empty(p.Validate());
                Assert.Empty(i.Unknown(p.KnownParams));
                string e = Path.Combine(d, Baseline.FileName);
                if (File.Exists(e)) Assert.NotEmpty(Baseline.Parse(File.ReadAllText(e)).Drawings);
                n++;
            }
            Assert.True(n >= 3, "回归样例 " + n);
        }

        // ---------------- 专用实例识别: PID 会被系统复用, 必须 PID + 启动时间都一致 ----------------
        [Fact]
        public void DedicatedInstanceRequiresPidAndStartTime()
        {
            var t = new DateTime(2026, 10, 10, 2, 12, 0, DateTimeKind.Utc);
            string rec = Sw.Stamp(36092, t);
            Assert.True(Sw.Matches(rec, 36092, t));
            Assert.False(Sw.Matches(rec, 36092, t.AddSeconds(5)));      // 同一 PID 被后来启动的 SolidWorks 复用
            Assert.False(Sw.Matches(rec, 36093, t));
            Assert.False(Sw.Matches("36092", 36092, t));                // 旧格式只有 PID: 无法确认身份
            Assert.False(Sw.Matches("", 36092, t));
            Assert.False(Sw.Matches(null, 36092, t));
        }

        [Fact]
        public void RotNameMatchesWholePid()
        {
            Assert.Equal(1234, Sw.RotPid("!SolidWorks_PID_1234"));
            Assert.Equal(1234, Sw.RotPid("SolidWorks_PID_1234"));
            Assert.NotEqual(123, Sw.RotPid("!SolidWorks_PID_1234"));     // 旧代码用 Contains: PID 123 会连到 1234
            Assert.Equal(-1, Sw.RotPid("!{12345678-ABCD}"));
            Assert.Equal(-1, Sw.RotPid(null));
        }

        // ---------------- 回归基准 ----------------
        static RunManifest Manifest()
        {
            var m = new RunManifest();
            m.Checks.Add(new CheckRecord { Name = "干涉检查", Passed = true, Summary = "0 处" });
            m.Checks.Add(new CheckRecord { Name = "回归基准", Passed = false, Summary = "旧" });
            m.Metrics["装配/A/零件数"] = new MetricRecord { Value = 6 };
            m.Metrics["装配/A/长mm"] = new MetricRecord { Value = 180, Tol = 1 };
            m.Metrics["物料/质量kg"] = new MetricRecord { Value = 3.6, TolPct = 0.5 };
            return m;
        }

        const string Index = "[ { \"code\": \"PG-001\", \"file\": \"PG-001_太阳轮.SLDDRW\", \"size\": \"A3\", \"scale\": \"1:1\", \"remark\": \"\", \"dims\": 4, \"holes\": 0, \"balloons\": null }," +
                             "  { \"code\": \"PG-000\", \"file\": \"装配图/PG-000.SLDDRW\", \"size\": \"A2\", \"scale\": \"1:1.5\", \"remark\": \"\", \"dims\": 3, \"holes\": null, \"balloons\": 4 } ]";

        [Fact]
        public void BaselineOfARunMatchesItself()
        {
            var m = Manifest();
            var e = Baseline.Parse(Baseline.Generate(m, Index, "run1"));
            Assert.DoesNotContain("回归基准", e.Checks);                  // 不把自己写进基准
            Assert.Equal(2, e.Drawings.Count);
            Assert.Empty(Baseline.Compare(e, m, Index));
        }

        [Fact]
        public void BaselineCatchesMetricOutsideTolerance()
        {
            var e = Baseline.Parse(Baseline.Generate(Manifest(), Index, "run1"));
            var m = Manifest();
            m.Metrics["装配/A/长mm"].Value = 180.8;                       // 允差 ±1 内
            m.Metrics["物料/质量kg"].Value = 3.61;                        // ±0.5% = ±0.018 内
            Assert.Empty(Baseline.Compare(e, m, Index));
            m.Metrics["装配/A/零件数"].Value = 5;                          // 计数必须相等
            m.Metrics["物料/质量kg"].Value = 3.7;
            m.Metrics.Remove("装配/A/长mm");
            var d = Baseline.Compare(e, m, Index);
            Assert.Equal(3, d.Count);
            Assert.Contains(d, s => s.Contains("零件数") && s.Contains("基准 6"));
            Assert.Contains(d, s => s.StartsWith("指标缺失"));
        }

        [Fact]
        public void BaselineCatchesDrawingRegressions()
        {
            var m = Manifest();
            var e = Baseline.Parse(Baseline.Generate(m, Index, "run1"));
            string changed = Index.Replace("\"scale\": \"1:1\"", "\"scale\": \"1:2\"").Replace("\"dims\": 4", "\"dims\": 6");
            var d = Baseline.Compare(e, m, changed);
            Assert.Contains(d, s => s.Contains("1:2") && s.Contains("基准 A3 1:1"));
            Assert.Contains(d, s => s.Contains("尺寸 6 处, 基准 4 处"));   // 多出伪直径也是退化
            string missing = "[ { \"file\": \"PG-001_太阳轮.SLDDRW\", \"size\": \"A3\", \"scale\": \"1:1\", \"dims\": 4, \"holes\": 0 }," +
                             "  { \"file\": \"PG-009_新图.SLDDRW\", \"size\": \"A3\", \"scale\": \"1:1\", \"dims\": 1, \"holes\": 0 } ]";
            d = Baseline.Compare(e, m, missing);
            Assert.Contains("图纸缺失: 装配图/PG-000.SLDDRW", d);
            Assert.Contains("基准中没有的图纸: PG-009_新图.SLDDRW", d);
            Assert.Contains("图纸缺失: PG-001_太阳轮.SLDDRW", Baseline.Compare(e, m, null).First());
        }

        [Fact]
        public void BaselineCatchesMissingOrFailedChecks()
        {
            var e = Baseline.Parse(Baseline.Generate(Manifest(), Index, "run1"));
            var m = Manifest();
            m.Checks[0].Passed = false;
            Assert.Contains(Baseline.Compare(e, m, Index), s => s.StartsWith("校核未通过: 干涉检查"));
            m.Checks.RemoveAt(0);
            Assert.Contains("校核缺失: 干涉检查", Baseline.Compare(e, m, Index));
        }

        // ---------------- 行星轮系运动学残差: 太阳轮整圈歧义 ----------------
        [Fact]
        public void KinematicResidualResolvesWholeSunTurnsButNotWrongStates()
        {
            // m=1.5, 21/27/75 真实运行 (20261010_140631): 归零配合使太阳轮从 -180° 继续转到 -360°, 读数为 0°;
            // 行星架 -78.75° = -360/4.571, 行星轮自转 140° —— 与"多转一圈"的理论值完全一致
            var p = new PlanetaryGearbox();
            p.Bind(Input("{ \"project\": \"planetary_gear\", \"params\": { \"module\": 1.5, \"z_sun\": 21, \"z_planet\": 27, \"z_ring\": 75, \"face_width\": 15 } }"));
            Assert.Empty(p.Validate());
            var orb = new[] { -78.75, -78.75, -78.75 };
            int k; double ec, ep;
            Assert.True(p.Residual(0, 0, -78.75, new[] { 140.0, 140, 140 }, orb, out k, out ec, out ep) < 1e-9);
            Assert.Equal(-1, k);
            // 行星架对得上某一圈、行星轮自转对不上 → 仍不通过
            Assert.True(p.Residual(0, 0, -78.75, new[] { 0.0, 140, 140 }, orb, out k, out ec, out ep) > 1);
            // 行星架不在任何整圈对应的位置 → 不通过
            Assert.True(p.Residual(0, 0, -40, new[] { 140.0, 140, 140 }, new[] { -40.0, -40, -40 }, out k, out ec, out ep) > 1);
            // 常规小角度驱动: k = 0
            double c10 = -10 / (1 + 75.0 / 21), sp = c10 - 21.0 / 27 * (-10 - c10);
            Assert.True(p.Residual(-10, -10, c10, new[] { sp, sp, sp }, new[] { c10, c10, c10 }, out k, out ec, out ep) < 1e-9);
            Assert.Equal(0, k);
        }

        // ---------------- 变换矩阵约定 (已标定: 行向量, ArrayData[0..2] = 局部 X 轴在父坐标中的方向) ----------------
        [Fact]
        public void XfFillUsesCalibratedRowConventionAndMetres()
        {
            var a = new double[16];
            Xf.RotZ(90, 10, 20, 30).Fill(a, 0);
            Assert.Equal(0, a[0], 12); Assert.Equal(1, a[1], 12);        // 局部 X -> 全局 +Y
            Assert.Equal(-1, a[3], 12); Assert.Equal(0, a[4], 12);       // 局部 Y -> 全局 -X
            Assert.Equal(0.010, a[9], 12); Assert.Equal(0.030, a[11], 12);
            Assert.Equal(1, a[12]);
        }

        [Fact]
        public void XfComposition()
        {
            var child = Xf.T(100, 0, 0);
            var parent = Xf.RotZ(90, 0, 0, 0);
            var g = child.In(parent);
            Assert.Equal(0, g.Tx, 9); Assert.Equal(100, g.Ty, 9);
            var d = Xf.Dir("+X", 0, 0, 0);                                // 局部 Z 指向 +X
            Assert.Equal(new double[] { 1, 0, 0 }, d.Z);
        }

        // ---------------- 枚举陷阱 (docs/api_calibration.md 1-20) ----------------
        [Fact]
        public void AddMateNoErrorIsOneNotZero()
        {
            Assert.Equal(1, (int)SolidWorks.Interop.swconst.swAddMateError_e.swAddMateError_NoError);
        }

        // ---------------- 孔/凸台判定: 外法向指向轴线 = 孔 ----------------
        [Fact]
        public void ConcaveTestDistinguishesHoleFromBoss()
        {
            var axisZ = new double[] { 0.01, 0.02, 0, 0, 0, 1, 0.005 };   // 轴线过 (10,20) mm, 沿 Z, R5
            var p = new double[] { 0.015, 0.02, 0.003 };                   // 面上一点 (轴线 +X 侧)
            Assert.True(Drw.Concave(p, new double[] { -1, 0, 0 }, axisZ));  // 孔: 法向指向轴线
            Assert.False(Drw.Concave(p, new double[] { 1, 0, 0 }, axisZ));  // 凸台: 法向背离轴线
        }

        // ---------------- GB 紧固件长度选取 ----------------
        [Fact]
        public void StandardBoltLengthPicksNextSeriesValue()
        {
            Assert.Equal(40, Std.StdLen(38.2));
            Assert.Equal(40, Std.StdLen(40.0));
            Assert.Equal(10, Std.StdLen(3));
            Assert.Throws<Exception>(() => Std.StdLen(500));
        }

        // ---------------- 工程图参数表 ----------------
        [Fact]
        public void ParamTableParsing()
        {
            var r = Drw.ParseTable("模数 m|2; 齿数 z|18 ;坏行;压力角|20°");
            Assert.Equal(3, r.Count);
            Assert.Equal(new[] { "齿数 z", "18" }, r[1]);
            Assert.Empty(Drw.ParseTable(""));
            Assert.Equal(new[] { 16.0, 31.5 }, Drw.ParseDias("16; 31.5;x"));
            Assert.Empty(Drw.ParseDias(""));
        }

        // ---------------- STEP 身份核对 ----------------
        const string Step = "ISO-10303-21;\nHEADER;\nENDSEC;\nDATA;\n" +
            "#1 = PRODUCT ( 'PG-000_\\X2\\884c661f\\X0\\', 'x', '', ( #9 ) ) ;\n" +
            "#2 = PRODUCT ( 'PG-001_\\X2\\592a9633\\X0\\', 'x', '', ( #9 ) ) ;\n" +
            "#3 = MANIFOLD_SOLID_BREP ( 'a', #5 ) ;\nENDSEC;\nEND-ISO-10303-21;\n";

        [Fact]
        public void StepDecodeAndProducts()
        {
            Assert.Equal("PG-001_太阳", StepCheck.Decode("PG-001_\\X2\\592a9633\\X0\\"));
            var p = StepCheck.Products(Step);
            Assert.Contains("PG-000_行星", p);
        }

        [Fact]
        public void StepCheckCatchesWrongDocument()
        {
            string s;
            Assert.True(StepCheck.VerifyText(Step, "PG-000_行星", new[] { "PG-001_太阳" }, out s), s);
            Assert.False(StepCheck.VerifyText(Step, "PG-999_别的装配", new[] { "PG-001_太阳" }, out s));
            Assert.False(StepCheck.VerifyText(Step, "PG-000_行星", new[] { "PG-001_太阳", "PG-002_行星轮" }, out s));
            Assert.Contains("缺少零件", s);
            Assert.False(StepCheck.VerifyText("not a step", "PG-000_行星", new string[0], out s));
        }
    }

    /// <summary>运行目录与 run.json (Run 是全局状态, 单独一个串行测试)</summary>
    [Collection("Run")]
    public class RunTests
    {
        [Fact]
        public void RunCreatesStandardLayoutAndManifest()
        {
            string root = Path.Combine(Path.GetTempPath(), "cadrun_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            string input = Path.Combine(root, "in"); Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "design.json"), "{}");
            Run.Init(root);
            string dir = Run.Begin("任务 A", "demo", "演示", input);
            foreach (string sub in new[] { "models", "drawings", "exports", "bom", "checks", "images", "input" })
                Assert.True(Directory.Exists(Path.Combine(dir, sub)), sub);
            Assert.True(File.Exists(Path.Combine(dir, "input", "design.json")));
            Assert.EndsWith("_任务_A", dir);
            Run.Step("零件", () => File.WriteAllText(Path.Combine(Run.Models, "a.SLDPRT"), "x"));
            Assert.Throws<InvalidOperationException>(() => Run.Step("装配", () => throw new InvalidOperationException("boom")));
            Run.Check("干涉检查", true, "0 处");
            Run.Metric("物料/质量kg", 3.6, tolPct: 0.5);
            Run.Finish(false, "boom");
            Run.CloseLog();
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "run.json")));
            var r = doc.RootElement;
            Assert.Equal("failed", r.GetProperty("status").GetString());
            var steps = r.GetProperty("steps").EnumerateArray().ToList();
            Assert.Equal("passed", steps[0].GetProperty("status").GetString());
            Assert.Equal("failed", steps[1].GetProperty("status").GetString());
            Assert.Equal("boom", steps[1].GetProperty("message").GetString());
            Assert.Equal(1, r.GetProperty("artifacts").GetProperty("parts").GetInt32());
            Assert.Equal(0.5, r.GetProperty("metrics").GetProperty("物料/质量kg").GetProperty("tol_pct").GetDouble());
            File.Delete(Path.Combine(dir, "run.log"));                     // 日志文件已释放
            // 续跑读回 run.json: 指标保留 (续跑的基准比对依赖它)
            Run.Resume(dir);
            Run.CloseLog();
            Assert.Equal(3.6, Run.Manifest.Metrics["物料/质量kg"].Value);
        }

        [Fact]
        public void ResetClearsLazyRegistrationsOfThePreviousRun()
        {
            string root = Path.Combine(Path.GetTempPath(), "cadrun_" + Guid.NewGuid().ToString("N"));
            Run.Init(root);
            Run.Begin("t", "demo", "演示", null);
            Run.CloseLog();
            Cat.Lazy = true;
            string p = Cat.Make(new Part { Id = "X", Code = "T-001", Name = "零件" }, m => { });   // 按需模式只登记, 不连接 SolidWorks
            Assert.StartsWith(Run.Models, p);
            Assert.True(Cat.Has("X"));
            Cat.Version = "V9";
            Cat.Reset();
            Assert.False(Cat.Has("X"));
            Assert.False(Cat.Lazy);
            Assert.Equal("V1.0", Cat.Version);
        }

        [Fact]
        public void ParameterTypeErrorIsAValidationProblemNotACrash()
        {
            // 退出码 2 = 参数校验未通过 (未连接 SolidWorks); 以前类型错误直接抛异常, 退出码 1
            string d = Path.Combine(Path.GetTempPath(), "cadtests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "design.json"), "{ \"project\": \"planetary_gear\", \"params\": { \"module\": \"abc\" } }");
            Assert.Equal(2, Pipeline.Execute(new PlanetaryGearbox(), DesignInput.Load(d)));
        }

        [Fact]
        public void InitDoesNotCreateDirectories()
        {
            string root = Path.Combine(Path.GetTempPath(), "cadrun_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Run.Init(root);
            Assert.False(Directory.Exists(Path.Combine(root, "output")));
            Assert.False(Directory.Exists(Path.Combine(root, ".cad")));
        }
    }
}
