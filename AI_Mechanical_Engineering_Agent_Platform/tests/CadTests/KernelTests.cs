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
                if (i.Project == "imported")
                {
                    // 导入的设计: 原几何 (geometry/) 不入库, 只校验 import.json 本身
                    Assert.Empty(ImportModel.Load(d).Validate(null));
                    continue;
                }
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

        // ---------------- STEP 导入: 名称、分类、材料 ----------------
        [Fact]
        public void ImportNamesDecodeAndClassify()
        {
            // SolidWorks 2023 导入 STEP 后的文档名 (\X2\ 中的反斜杠变成下划线, 配置名的尖括号变成下划线)
            Assert.Equal("不锈钢无缝管 304φ12x135mm长 淘宝", ImportNames.Clean("_X2_4e0d950894a265e07f1d7ba1_X0_ 304_X2_03c6_X0_12x135mm_X2_957f_X0_ _X2_6dd85b9d_X0_.sldprt"));
            Assert.Equal("RTWBL340A1002B", ImportNames.Clean("RTWBL340A1002B__X2_9ed88ba4_X0___X2_630952a05de5_X0__.sldprt"));
            Assert.Equal("RTWBL340A1001B机架", ImportNames.Clean("RTWBL340A1001B机架_默认<按加工>"));
            Assert.Equal("S8VK_C24024_BASE", ImportNames.Clean("S8VK_C24024_BASE_Default.sldprt"));
            Assert.Equal("微波实验炉", ImportNames.Decode("\\X2\\5fae6ce25b9e9a8c7089\\X0\\"));

            var names = new[] { "RTWBL340A1001机架封板1", "RTWBL340A1003B", "RTWBL340A1005B", "S8VK_C24024_BASE", "PLC 6ES7 214-2BD23-OXB8 西门子", "SAK 4N" };
            string pre = ImportNames.ProjectPrefix(names);
            Assert.Equal("RTWBL340", pre);
            string kind, code, nm, spec, brand;
            ImportNames.Classify("RTWBL340A1001机架封板4", pre, out kind, out code, out nm, out spec, out brand);
            Assert.Equal(("自制件", "RTWBL340A1001", "机架封板4"), (kind, code, nm));
            ImportNames.Classify("RTWBL340A1003B", pre, out kind, out code, out nm, out spec, out brand);
            Assert.Equal(("自制件", "RTWBL340A1003B", "RTWBL340A1003B"), (kind, code, nm));
            ImportNames.Classify("不锈钢无缝管 304φ12x135mm长 淘宝", pre, out kind, out code, out nm, out spec, out brand);
            Assert.Equal(("外购件", "不锈钢无缝管", "304φ12x135mm长", "淘宝"), (kind, nm, spec, brand));
            ImportNames.Classify("S8VK_C24024_BASE", pre, out kind, out code, out nm, out spec, out brand);
            Assert.Equal(("外购件", "S8VK_C24024_BASE"), (kind, nm));
            Assert.Null(ImportNames.ProjectPrefix(new[] { "PLC 6ES7", "SAK 4N" }));

            Assert.Equal("S304", ImportNames.Material("不锈钢卡套三通 304"));
            Assert.Equal("AL", ImportNames.Material("铝合金导轨"));
            Assert.Equal("PVC", ImportNames.Material("PVC线槽 高60X宽40"));
            Assert.Equal("UNKNOWN", ImportNames.Material("耐高温300°密封垫"));     // 300 不是 304
            Assert.Equal("UNKNOWN", ImportNames.Material("RTWBL340A1003B"));
        }

        // ---------------- STEP 导入: 直管识别 ----------------
        static List<FaceInfo> TubeFaces(double od, double id, double y0, double len, double x, double z)
        {
            // 沿 Y 轴的管: 内外圆柱面各分两半 + 两个端面
            var f = new List<FaceInfo>();
            foreach (double r in new[] { od / 2, od / 2, id / 2, id / 2 })
                f.Add(new FaceInfo { Kind = 'C', P = new[] { x, 123.0, z }, D = new[] { 0.0, -1, 0 }, R = r });
            f.Add(new FaceInfo { Kind = 'P', P = new[] { 5.0, y0, 7.0 }, D = new[] { 0.0, -1, 0 } });
            f.Add(new FaceInfo { Kind = 'P', P = new[] { -3.0, y0 + len, 1.0 }, D = new[] { 0.0, 1, 0 } });
            return f;
        }

        [Fact]
        public void TubeRecognitionIsStrict()
        {
            double v = Math.PI / 4 * (12 * 12 - 9 * 9) * 135;
            var g = TubeFit.Recognize(TubeFaces(12, 9, -20, 135, 40, -15), v);
            Assert.NotNull(g);
            Assert.Equal(("tube", "Y", 12.0, 9.0, 135.0, -20.0), (g.Type, g.Axis, g.Dout, g.Din, g.Length, g.Start));
            Assert.Equal(new[] { 40.0, -15.0 }, g.Center);                                   // XZ 面: (u,v) = (X,Z)
            Assert.Null(TubeFit.Recognize(TubeFaces(12, 9, -20, 135, 40, -15), v * 1.01));  // 体积对不上 (有倒角/开槽) 不认
            var withOther = TubeFaces(12, 9, -20, 135, 40, -15); withOther.Add(new FaceInfo { Kind = 'O' });
            Assert.Null(TubeFit.Recognize(withOther, v));
            var tilted = TubeFaces(12, 9, -20, 135, 40, -15);
            foreach (var f in tilted) f.D = new[] { 0.6, 0.8, 0 };                           // 轴线不平行于坐标轴
            Assert.Null(TubeFit.Recognize(tilted, v));
            var offAxis = TubeFaces(12, 9, -20, 135, 40, -15); offAxis[3].P = new[] { 41.0, 0, -15 };   // 内外圆不同轴
            Assert.Null(TubeFit.Recognize(offAxis, v));
        }

        // ---------------- STEP 导入: 相对变换 ----------------
        [Fact]
        public void RelativeTransformRoundTrip()
        {
            var parent = Xf.Frame("+X", "+Z", 100, 200, 300);
            var child = Xf.RotZ(30, 10, -5, 7).In(parent);
            var rel = Xf.Relative(child, parent);
            double dt, dr;
            Xf.Diff(rel.In(parent), child, out dt, out dr);
            Assert.True(dt < 1e-9 && dr < 1e-12);
            Xf.Diff(Xf.FromRow(child.ToRow()), child, out dt, out dr);
            Assert.True(dt < 1e-5 && dr < 1e-8);
            Xf.Diff(parent.In(parent.Inverse()), Xf.T(0, 0, 0), out dt, out dr);
            Assert.True(dt < 1e-9 && dr < 1e-12);
        }

        [Fact]
        public void ImportModelValidationCatchesEditingMistakes()
        {
            var id = Xf.T(0, 0, 0).ToRow();
            var m = new ImportModel { Root = "A01" };
            m.Parts.Add(new ImportPart { Id = "P001", Kind = "外购件", Material = "S304", Model = new ImportGeometry { Type = "tube", Axis = "Z", Center = new[] { 0.0, 0 }, Dout = 12, Din = 9, Length = 100 } });
            m.Assemblies.Add(new ImportAsm { Id = "A01", Children = { new ImportChild { Ref = "P001", Xf = id }, new ImportChild { Ref = "A02", Xf = id } } });
            m.Assemblies.Add(new ImportAsm { Id = "A02", Children = { new ImportChild { Ref = "P001", Xf = Xf.RotZ(45, 1, 2, 3).ToRow() } } });
            Assert.Empty(m.Validate(null));
            var round = ImportModel.Load(SaveTo(m));
            Assert.Empty(round.Validate(null));
            Assert.Equal(m.Assemblies[1].Children[0].Xf, round.Assemblies[1].Children[0].Xf);

            m.Assemblies[1].Children.Add(new ImportChild { Ref = "P999", Xf = id });                         // 引用不存在
            var scaled = (double[])id.Clone(); scaled[0] = 2;
            m.Assemblies[1].Children.Add(new ImportChild { Ref = "P001", Xf = scaled });                     // 不是刚体
            m.Assemblies[1].Children.Add(new ImportChild { Ref = "A01", Xf = id });                          // 循环
            m.Parts[0].Material = "钛合金";
            var r = m.Validate(null);
            Assert.Contains(r, s => s.Contains("P999"));
            Assert.Contains(r, s => s.Contains("单位向量"));
            Assert.Contains(r, s => s.Contains("循环"));
            Assert.Contains(r, s => s.Contains("未知材料"));
            var mirror = Xf.T(0, 0, 0).ToRow(); mirror[8] = -1;
            Assert.Equal("不是右手系 (镜像)", ImportModel.RigidError(mirror));
        }

        [Fact]
        public void KnownInterferenceMatchingIsStrict()
        {
            // 密封垫 (P016) 嵌入法兰板 (P025): 法兰在密封垫坐标系中的位置, 干涉 22606.7 mm³
            var rel = Xf.RotZ(90, 12.5, -3, 1.5);
            var m = new ImportModel();
            m.AcceptedInterferences.Add(new KnownInterference { PartA = "P016", PartB = "P025", Xf = rel.ToRow(), VolumeMm3 = 22606.7 });
            Assert.True(m.Accepts("P016", "P025", rel, 22606.7));
            Assert.True(m.Accepts("P016", "P025", rel, 22650));                    // 体积 ±1% 内 (数值误差)
            Assert.True(m.Accepts("P025", "P016", rel.Inverse(), 22606.7));         // A/B 顺序反过来
            Assert.False(m.Accepts("P016", "P025", Xf.RotZ(90, 12.55, -3, 1.5), 22606.7));   // 移动 0.05 mm → 新增
            Assert.False(m.Accepts("P016", "P025", rel, 23800));                   // 体积变了 5% (如改了密封垫厚度) → 新增
            Assert.False(m.Accepts("P016", "P099", rel, 22606.7));                 // 换了零件 → 新增
            Assert.False(m.Accepts("P016", "P025", null, 22606.7));                // 不是两零件之间的干涉
            Assert.Contains(m.Validate(null), s => s.Contains("已知干涉引用了不存在的零件"));   // 零件表里没有 P016/P025
        }

        /// <summary>圆角矩形环 (中心在原点, 宽 w 高 h 圆角 r); ccw = 逆时针走向</summary>
        static Loop2D RoundRect(double w, double h, double r, bool ccw, bool outer)
        {
            double a = w / 2, b = h / 2;
            var segs = new List<Seg2>
            {
                new Seg2 { T = "L", P = new[] { -a + r, -b, a - r, -b } },
                new Seg2 { T = "A", C = new[] { a - r, -b + r }, P = new[] { a - r, -b, a, -b + r }, Dir = 1 },
                new Seg2 { T = "L", P = new[] { a, -b + r, a, b - r } },
                new Seg2 { T = "A", C = new[] { a - r, b - r }, P = new[] { a, b - r, a - r, b }, Dir = 1 },
                new Seg2 { T = "L", P = new[] { a - r, b, -a + r, b } },
                new Seg2 { T = "A", C = new[] { -a + r, b - r }, P = new[] { -a + r, b, -a, b - r }, Dir = 1 },
                new Seg2 { T = "L", P = new[] { -a, b - r, -a, -b + r } },
                new Seg2 { T = "A", C = new[] { -a + r, -b + r }, P = new[] { -a, -b + r, -a + r, -b }, Dir = 1 },
            };
            if (!ccw)
            {
                segs.Reverse();
                foreach (var s in segs) { s.P = new[] { s.P[2], s.P[3], s.P[0], s.P[1] }; s.Dir = -s.Dir; }
            }
            return new Loop2D { Outer = outer, Segs = segs };
        }

        [Fact]
        public void PrismProfileAreaMatchesClosedForm()
        {
            // 方管 40×40 外圆角 R4, 内腔 36×36 内圆角 R2 (内孔按顺时针走向, 与 SolidWorks 内环一致)
            var outer = RoundRect(40, 40, 4, true, true);
            var inner = RoundRect(36, 36, 2, false, false);
            double expect = (1600 - (4 - Math.PI) * 16) - (1296 - (4 - Math.PI) * 4);
            Assert.Equal(1600 - (4 - Math.PI) * 16, Prism.SignedArea(outer), 9);
            Assert.Equal(-(1296 - (4 - Math.PI) * 4), Prism.SignedArea(inner), 9);
            Assert.Equal(expect, Prism.Area(new[] { outer, inner }), 9);
            // 环的走向反过来, 面积不变 (外环减孔)
            Assert.Equal(expect, Prism.Area(new[] { RoundRect(40, 40, 4, false, true), RoundRect(36, 36, 2, true, false) }), 9);
            // 带一个整圆孔 Ø10
            var hole = new Loop2D { Outer = false, Segs = { new Seg2 { T = "O", C = new[] { 0.0, 0 }, R = 5 } } };
            Assert.Equal(1600 - (4 - Math.PI) * 16 - Math.PI * 25, Prism.Area(new[] { outer, hole }), 9);

            var p = new PrismDef { Axis = "Y", From = 0, To = 1750, Loops = { outer, inner } };
            Assert.Contains("box", Prism.Check(p));                                   // 运行时靠它找回原实体
            p.Box = new double[] { -20, 0, -20, 20, 1750, 20 };
            Assert.Null(Prism.Check(p));
            p.To = -1; Assert.Contains("to", Prism.Check(p));
            p.To = 1750; p.Loops[0].Outer = false; Assert.Contains("外轮廓", Prism.Check(p));
            Assert.Equal(new[] { 3.0, 2.0 }, Prism.UV(0, new[] { 1.0, 2, 3 }));      // 轴 X → (Z, Y)
            Assert.Equal(new[] { 1.0, 3.0 }, Prism.UV(1, new[] { 1.0, 2, 3 }));      // 轴 Y → (X, Z)
            // 段的顺序与走向打乱 (实测 SolidWorks 读出的环就是这样): 首尾相接重排后面积正确
            var mixed = RoundRect(40, 40, 4, true, true).Segs;
            var shuffled = new List<Seg2> { mixed[0], mixed[5], mixed[2], mixed[7], mixed[4], mixed[1], mixed[6], mixed[3] };
            foreach (int i in new[] { 1, 2, 5 })
            {
                var g = shuffled[i];
                shuffled[i] = new Seg2 { T = g.T, C = g.C, P = new[] { g.P[2], g.P[3], g.P[0], g.P[1] }, Dir = -g.Dir };
            }
            var chained = new Loop2D { Outer = true, Segs = Prism.Chain(shuffled) };
            Assert.Equal(8, chained.Segs.Count);
            Assert.Equal(1600 - (4 - Math.PI) * 16, Math.Abs(Prism.SignedArea(chained)), 9);
            Assert.Null(Prism.Chain(shuffled.Take(7).ToList()));                     // 缺一段: 不闭合
            // 样条形式存储的圆弧: 过三点求圆心
            var c = Prism.Center(new[] { 10.0, 0 }, new[] { 0.0, 10 }, new[] { -10.0, 0 });
            Assert.Equal(0, c[0], 9); Assert.Equal(0, c[1], 9);
            Assert.Null(Prism.Center(new[] { 0.0, 0 }, new[] { 1.0, 1 }, new[] { 2.0, 2 }));   // 共线
        }

        [Fact]
        public void MiterCutRegionAndFreeSection()
        {
            // 40×40 方管沿 Z, z∈[-510,510], 上端 45° 斜口 (平面平行于 Y 轴, 过 x=510,z=470, 外法向朝 -x+z)
            double s = Math.Sqrt(0.5);
            var cut = new PlaneCut { N = new[] { -s, 0, s }, P = new[] { 510.0, 357.5, 470 } };
            double[] box = { 510, 337.5, -510, 550, 377.5, 510 };
            int k;
            var poly = Prism.CutRegion(cut, box, 5, out k);
            Assert.Equal(1, k);                                                       // 在 XZ 面 (法向 Y) 上画切除区
            Assert.True(poly.Count >= 3);
            foreach (var q in poly) Assert.True(-s * (q[0] - 510) + s * (q[1] - 470) >= -1e-9);   // 全在切口外侧
            // 切除区包含方管上端的楔形 (x=510 内侧角, z 从 470 到 510 处), 不含下部
            Assert.True(Contains(poly, 512, 505));
            Assert.False(Contains(poly, 545, 0));
            Assert.Empty(Prism.CutRegion(new PlaneCut { N = new[] { 0.5774, 0.5774, 0.5774 }, P = new double[3] }, box, 5, out k));   // 复合斜角: 不支持
            Assert.Equal(-1, k);
            // 半平面裁剪: 单位正方形保留 x ≥ 0.5 的一半
            var half = Prism.ClipHalfPlane(new List<double[]> { new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 0.0, 1 } }, new[] { 1.0, 0 }, 0.5);
            Assert.Equal(4, half.Count);
            Assert.Equal(0.5, half.Min(q => q[0]), 12);
            Assert.Empty(Prism.ClipHalfPlane(new List<double[]> { new[] { 0.0, 0 }, new[] { 1.0, 0 }, new[] { 1.0, 1 } }, new[] { 1.0, 0 }, 2));
            Assert.Equal(1, Prism.ZeroAxis(new[] { s, 0, s }));
            Assert.Equal(-1, Prism.ZeroAxis(new[] { 1.0, 0, 0 }));
            Assert.Equal(2, Prism.AlongAxis(new[] { 0, 0, -1.0 }));
            // 截面取样: 避开斜口 [-510,-470]、[470,510] 与横孔 [-5,5], 取最长的空段中部
            double z0, dz;
            Assert.True(Prism.FreeLevel(-510, 510, new List<double[]> { new[] { 470.0, 510 }, new[] { -510.0, -470 }, new[] { -5.0, 5 } }, out z0, out dz));
            Assert.Equal(1, dz, 12);
            Assert.True(z0 > 5 && z0 + dz < 470 || z0 < -5 && z0 > -470);
            Assert.False(Prism.FreeLevel(0, 10, new List<double[]> { new[] { 0.0, 5 }, new[] { 4.9, 10 } }, out z0, out dz));   // 处处被占
            // 数据校验
            var p = new PrismDef { Axis = "Z", From = -510, To = 510, Box = box, Loops = { RoundRect(40, 40, 4, true, true) }, Cuts = new List<PlaneCut> { cut } };
            Assert.Null(Prism.Check(p));
            p.Cuts[0] = new PlaneCut { N = new[] { 0.6, 0.0, 0.6 }, P = cut.P }; Assert.Contains("单位", Prism.Check(p));
            p.Cuts = null;
            p.Holes = new List<HoleCut> { new HoleCut { Axis = "Z", C = new[] { 0.0, 0 }, D = 10, From = 0, To = 3 } }; Assert.Contains("拉伸轴", Prism.Check(p));
            p.Holes[0].Axis = "Y"; Assert.Null(Prism.Check(p));
            p.Holes[0].To = 0; Assert.Contains("to", Prism.Check(p));
        }

        [Fact]
        public void SheetDefinitionCheck()
        {
            // 封板 2: 1272×497 基板, 板厚 1.5, R1, 四边向下 16.5 并回折 10, 基板上一个 Ø7 孔
            var outer = new Loop2D { Outer = true, Segs =
            {
                new Seg2 { T = "L", P = new[] { -636.0, -248.5, 636, -248.5 } }, new Seg2 { T = "L", P = new[] { 636.0, -248.5, 636, 248.5 } },
                new Seg2 { T = "L", P = new[] { 636.0, 248.5, -636, 248.5 } }, new Seg2 { T = "L", P = new[] { -636.0, 248.5, -636, -248.5 } },
            } };
            var d = new SheetDef
            {
                Axis = "Z", Top = 0, T = 1.5, R = 1, Gap = 0.05, Box = new double[] { -636, -248.5, -16.5, 636, 248.5, 0 },
                Loops = { outer, new Loop2D { Outer = false, Segs = { new Seg2 { T = "O", C = new[] { -622.5, -235.0 }, R = 3.5 } } } },
                Flanges = { new FlangeDef { Edge = new[] { -636.0, -248.5, -636, 248.5 }, Dir = -1, Length = 16.5, Return = 10 } },
            };
            Assert.Null(SheetMetal.Check(d));
            Assert.Equal(1272 * 497 - Math.PI * 3.5 * 3.5, Prism.Area(d.Loops), 6);            // 基体法兰草图 = 外环减孔
            d.Flanges[0].Return = 1; Assert.Contains("回折", SheetMetal.Check(d));               // 回折边不能窄于板厚
            d.Flanges[0].Return = 10; d.Flanges[0].Dir = 0; Assert.Contains("dir", SheetMetal.Check(d));
            d.Flanges[0].Dir = -1; d.Loops[0].Outer = false; Assert.Contains("外环", SheetMetal.Check(d));
            d.Loops[0].Outer = true; d.T = 0; Assert.Contains("t > 0", SheetMetal.Check(d));
            d.T = 1.5; d.Box = null; Assert.Contains("box", SheetMetal.Check(d));
        }

        [Fact]
        public void FlangeNotchFromOuterLoop()
        {
            // 法兰面 100×20, 从 v=0 的边切进一个 15×8 的缺口 (u 40..55)
            Func<double, double, double, double, Seg2> L = (a, b, c, e) => new Seg2 { T = "L", P = new[] { a, b, c, e } };
            var face = new Loop2D { Outer = true, Segs =
            {
                L(0, 0, 40, 0), L(40, 0, 40, 8), L(40, 8, 55, 8), L(55, 8, 55, 0), L(55, 0, 100, 0),
                L(100, 0, 100, 20), L(100, 20, 0, 20), L(0, 20, 0, 0),
            } };
            var notches = SheetMetal.Notches(face);
            Assert.Single(notches);
            Assert.Equal(4, notches[0].Segs.Count);                                   // 三段缺口边 + 沿矩形边封口
            Assert.Equal(120, Math.Abs(Prism.SignedArea(notches[0])), 9);
            // 没有缺口的矩形
            Assert.Empty(SheetMetal.Notches(new Loop2D { Outer = true, Segs = { L(0, 0, 100, 0), L(100, 0, 100, 20), L(100, 20, 0, 20), L(0, 20, 0, 0) } }));
        }

        /// <summary>点是否在凸多边形内 (顶点逆时针或顺时针均可)</summary>
        static bool Contains(List<double[]> poly, double u, double v)
        {
            int sign = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                double[] a = poly[i], b = poly[(i + 1) % poly.Count];
                double c = (b[0] - a[0]) * (v - a[1]) - (b[1] - a[1]) * (u - a[0]);
                int sg = Math.Sign(c);
                if (sg == 0) continue;
                if (sign == 0) sign = sg; else if (sg != sign) return false;
            }
            return true;
        }

        static string SaveTo(ImportModel m)
        {
            string d = TempDir();
            File.WriteAllText(Path.Combine(d, ImportModel.FileName), m.ToJson());
            return d;
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

        [Fact]
        public void AssemblyCodeAndNameComeFromFileName()
        {
            string no, nm;
            AsmBuild.CodeName(@"C:\m\装配\RTWBL340A1000_微波炉磁控管电源模块.SLDASM", out no, out nm);
            Assert.Equal(("RTWBL340A1000", "微波炉磁控管电源模块"), (no, nm));
            AsmBuild.CodeName(@"C:\m\微波实验炉.SLDASM", out no, out nm);
            Assert.Equal(("微波实验炉", "微波实验炉"), (no, nm));
            AsmBuild.CodeName(@"C:\m\CFC650-M03-000_碱洗模块装配.SLDASM", out no, out nm);
            Assert.Equal(("CFC650-M03-000", "碱洗模块装配"), (no, nm));
        }

        // ---------------- 工程图折线数据解析 (GetPolylines7) ----------------
        static double[] Line(double x1, double y1, double x2, double y2) { return new double[] { 0, 0, 0, 0, -1, 1, -1, 0, 2, x1, y1, 0, x2, y2, 0 }; }

        [Fact]
        public void PolylineParserSurvivesRecordsWithoutGeometryData()
        {
            var arc = new double[] { 1, 12, 0, 0, 0, 0.01, 0, 0, 0.01, 0, 0, 0, 0, 1, 0, 0, -1, 1, -1, 0, 3, 0.01, 0, 0, 0, 0.01, 0, -0.01, 0, 0 };
            // 实测 (微波实验炉装配图): 样条转成的折线, 第二个数是 17, 后面直接是线属性与点, 没有几何数据
            var spline = new double[] { 0, 17, 0, 0, -1, 1, -1, 0, 3, 0.1, 0.2, 0, 0.11, 0.21, 0, 0.12, 0.215, 0 };
            var pl = Line(0, 0, 1, 0).Concat(arc).Concat(spline).Concat(Line(0, 0, 0, 1)).ToArray();
            string err;
            var r = Drw.ParsePolylines(pl, 4, out err);
            Assert.Null(err);
            Assert.Equal(4, r.Count);
            Assert.Equal((1, 12, 3), (r[1].Type, r[1].GeomLen, r[1].Np));
            Assert.Equal((0, 0, 3), (r[2].Type, r[2].GeomLen, r[2].Np));                  // 按"无几何数据"解析
            Assert.Equal((0, 2), (r[3].Type, r[3].Np));
            Assert.Equal(1.0, pl[r[3].PtsAt + 4]);                                       // 最后一条线的终点 y = 1, 没有错位
            // 无法解析的数据: 返回出错位置, 不抛异常 (以前数组长度为负 → 溢出异常 → 整张图外形尺寸漏标)
            r = Drw.ParsePolylines(Line(0, 0, 1, 0).Concat(new double[] { 0.37, -5, 2.5 }).ToArray(), 2, out err);
            Assert.Single(r);
            Assert.Contains("第 1 条", err);
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
            Directory.CreateDirectory(Path.Combine(input, "geometry"));
            File.WriteAllText(Path.Combine(input, "geometry", "P001.x_t"), "x");
            Run.Init(root);
            string dir = Run.Begin("任务 A", "demo", "演示", input);
            foreach (string sub in new[] { "models", "drawings", "exports", "bom", "checks", "images", "input" })
                Assert.True(Directory.Exists(Path.Combine(dir, sub)), sub);
            Assert.True(File.Exists(Path.Combine(dir, "input", "design.json")));
            Assert.True(File.Exists(Path.Combine(dir, "input", "geometry", "P001.x_t")));      // 续跑读快照: 子目录也要在
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
