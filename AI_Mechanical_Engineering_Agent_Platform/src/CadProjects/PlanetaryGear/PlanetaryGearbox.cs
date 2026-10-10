using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using CadKernel;
using CadLibrary;

namespace CadProjects.PlanetaryGear
{
    /// <summary>
    /// 2K-H 行星齿轮减速器 (齿圈固定, 太阳轮输入, 行星架输出), 带齿轮配合可拖动的运动装配。
    /// 回归样例 + 参数化设计: 模数、齿数、行星轮数、齿宽、侧隙可由 design.json 指定; 其余尺寸按模数缩放,
    /// 默认值 (m=2, 18/27/72, 3 行星轮, b=20) 与 2026-09-24 实测项目完全一致; 第二组参数 (m=1.5, 21/27/75, b=15,
    /// input/planetary_gear_m15) 于 2026-10-10 真机跑通并逐张看图核对 —— 它暴露了运动学校核的整圈歧义与剖视图重合边漏标, 均已修正。
    /// 来源: D:\07_WorkSpace\Claude\0924\PlanetaryGear (Python 脚本 01/02/02b, 已在 SOLIDWORKS 2023 SP5 实测)。
    /// 零件坐标: 齿轮轴线 = Z 轴 (前视基准面法向), 齿宽 z ∈ [0, b]。
    /// </summary>
    public sealed class PlanetaryGearbox : CadProject
    {
        public override string Id { get { return "planetary_gear"; } }
        public override string Description { get { return "2K-H 行星齿轮减速器"; } }
        public override IEnumerable<string> KnownParams
        {
            get { return new[] { "module", "z_sun", "z_planet", "z_ring", "planets", "face_width", "backlash" }; }
        }

        // ---- 设计参数 ----
        double M, B, J;          // 模数, 齿宽, 分度圆圆周侧隙
        int ZS, ZP, ZR, N;
        double A { get { return M * (ZS + ZP) / 2; } }       // 中心距
        double Ratio { get { return 1.0 + (double)ZR / ZS; } }

        // ---- 派生尺寸 (以模数为单位, 默认 m=2 时即原项目数值) ----
        double SunShaftD { get { return 8 * M; } }            // Ø16
        double SunShaftL { get { return 2 * B; } }            // 伸出 40
        double PinR { get { return 3 * M; } }                 // 销轴 Ø12 = 行星轮孔
        double Gap { get { return M; } }                      // 行星架端面与齿轮端面间隙 2
        double PlateT { get { return 5 * M; } }               // 行星架板厚 10
        double CarrierR { get { return A + 6.5 * M; } }       // 58
        double OutShaftD { get { return 12.5 * M; } }         // Ø25
        double OutShaftL { get { return 25 * M; } }           // 50
        double RingRootR { get { return M * ZR / 2 + 1.25 * M; } }
        double RingOutR { get { return RingRootR + 7.75 * M; } }   // 90
        double RingBoltPcdR { get { return Math.Round((RingRootR + RingOutR) / 2); } }   // 82
        double RingBoltD { get { return 4 * M; } }            // Ø8

        string sunP, planetP, ringP, carrierP, asmPath;

        public override List<string> Validate()
        {
            M = Input.Num("module", 2.0); B = Input.Num("face_width", 20.0); J = Input.Num("backlash", 0.10);
            ZS = Input.Int("z_sun", 18); ZP = Input.Int("z_planet", 27); ZR = Input.Int("z_ring", 72); N = Input.Int("planets", 3);
            var r = Gears.PlanetaryConditions(ZS, ZP, ZR, N);
            if (M <= 0 || M > 10) r.Add("模数应在 (0, 10] mm");
            if (B < 4 * M || B > 20 * M) r.Add("齿宽建议 4m ~ 20m");
            if (J < 0 || J > 0.1 * M) r.Add("侧隙应在 [0, 0.1m]");
            // 减重孔 (r = 0.8a, Ø9m) 不得碰销轴、太阳轮轴
            double lr = 0.8 * A, ld = 9 * M;
            double pinDist = Math.Sqrt(lr * lr + A * A - 2 * lr * A * Math.Cos(Math.PI / N));   // 减重孔心 (相位 π/N) 到销轴心
            if (pinDist < ld / 2 + PinR + M) r.Add("减重孔与销轴过近, 请调整参数");
            if (lr - ld / 2 < SunShaftD / 2 + M) r.Add("减重孔与中心过近, 请调整参数");
            return r;
        }

        public override string DefaultTitle
        {
            get { return "行星齿轮减速器"; }
        }

        // ================================================================== 零件
        string GearTable(string kind, int z, GearInfo g)
        {
            var rows = new List<string>
            {
                "模数 m|" + M, "齿数 z|" + z, "压力角 α|20°", "齿顶高系数 ha*|1", "顶隙系数 c*|0.25",
                "分度圆直径 d|" + (2 * g.R).ToString("0.###"),
                (kind == "内齿" ? "齿顶圆直径 da (内)|" : "齿顶圆直径 da|") + (2 * g.Ra).ToString("0.###"),
                (kind == "内齿" ? "齿根圆直径 df (外)|" : "齿根圆直径 df|") + (2 * g.Rf).ToString("0.###"),
                "圆周侧隙 jt|" + J.ToString("0.00") + " (两轮各分担一半)",
                "精度等级|8 (GB/T 10095.1)",
                "配对齿轮|" + (kind == "太阳轮" ? "行星轮 z=" + ZP : kind == "行星轮" ? "太阳轮 z=" + ZS + " / 内齿圈 z=" + ZR : "行星轮 z=" + ZP),
            };
            return string.Join(";", rows);
        }

        const string NOTE_GEAR = "齿根过渡为半径 0.38m 圆弧近似 (非展成曲线), 齿根应力仅作量级评估|未注倒角 C0.5|未注尺寸公差 GB/T 1804-m";

        public override void Prepare()
        {
            Cat.ProjectName = Title;
            Cat.Version = "V1.0";
        }

        public override void BuildParts()
        {
            GearInfo gi;

            // ---- 太阳轮: 齿 + 输入轴, 轴肩 R1.5 (FEA 显示尖角处应力集中) ----
            var sunSegs = Gears.External(ZS, M, out gi, backlash: J);
            sunP = Cat.Make(new Part { Id = "SUN", Code = "PG-001", Name = "太阳轮", Mat = Mat.S4340, Color = Col.Orange, DrwType = "turn",
                Spec = string.Format("z={0} m={1} α=20° b={2}, 输入轴 Ø{3}", ZS, M, B, SunShaftD), Process = "锻-粗车-正火-精车-滚齿-去毛刺",
                Notes = NOTE_GEAR, Table = GearTable("太阳轮", ZS, gi), Dias = SunShaftD.ToString("0.###") }, m =>
            {
                Gears.Draw(Sk.XY(m, 0), sunSegs).Ext(B, 1).Name = "太阳轮齿";
                Sk.XY(m, 0).Circ(0, 0, SunShaftD).Ext(B + SunShaftL, 1).Name = "输入轴";
                Features.FilletCircles(m, new[] { new double[] { 0, 0, B, SunShaftD / 2 } }, 0.75 * M, "轴肩圆角");
                Features.AxisFromPlanes(m, 1, 2, "GearAxis");
            });

            // ---- 行星轮: 齿 + 销孔 ----
            var plSegs = Gears.External(ZP, M, out gi, backlash: J);
            planetP = Cat.Make(new Part { Id = "PLANET", Code = "PG-002", Name = "行星轮", Mat = Mat.S4340, Color = Col.Blue, DrwType = "turn",
                Spec = string.Format("z={0} m={1} α=20° b={2}, 孔 Ø{3}", ZP, M, B, 2 * PinR), Process = "锻-粗车-正火-精车-滚齿-去毛刺",
                Notes = NOTE_GEAR + "|销孔与齿轮分度圆同轴度 φ0.02", Table = GearTable("行星轮", ZP, gi), Dias = (2 * PinR).ToString("0.###") }, m =>
            {
                Gears.Draw(Sk.XY(m, 0), plSegs).Ext(B, 1).Name = "行星轮齿";
                Sk.XY(m, 0).Circ(0, 0, 2 * PinR).CutThru(0).Name = "销孔";
                Features.AxisFromPlanes(m, 1, 2, "GearAxis");
            });

            // ---- 内齿圈: 外圆 + 内齿廓同一草图拉伸, 安装孔 ----
            var rgSegs = Gears.Internal(ZR, M, out gi, backlash: J);
            ringP = Cat.Make(new Part { Id = "RING", Code = "PG-003", Name = "内齿圈", Mat = Mat.ALLOY, Color = Col.Grey, DrwType = "plate",
                Spec = string.Format("z={0} m={1} α=20° b={2}, 外径 Ø{3:0}", ZR, M, B, 2 * RingOutR), Process = "锻-粗车-调质-精车-插齿-钻孔",
                Notes = NOTE_GEAR.Replace("齿根过渡为半径 0.38m 圆弧近似 (非展成曲线), 齿根应力仅作量级评估|", "") + "|安装孔对齿圈轴线位置度 φ0.1",
                Table = GearTable("内齿", ZR, gi) }, m =>
            {
                Gears.Draw(Sk.XY(m, 0).Circ(0, 0, 2 * RingOutR), rgSegs).Ext(B, 1).Name = "内齿圈";
                var s = Sk.XY(m, 0);
                for (int k = 0; k < 6; k++)
                {
                    double a = (30 + 60 * k) * Math.PI / 180;
                    s.Circ(RingBoltPcdR * Math.Cos(a), RingBoltPcdR * Math.Sin(a), RingBoltD);
                }
                s.CutThru(0).Name = "安装孔";
                Features.AxisFromPlanes(m, 1, 2, "GearAxis");
            });

            // ---- 行星架: 板 z∈[-Gap-PlateT, -Gap] + 销轴 z∈[-5, b] + 输出轴 + 减重孔 ----
            carrierP = Cat.Make(new Part { Id = "CARRIER", Code = "PG-004", Name = "行星架", Mat = Mat.C45, Color = Col.Green, DrwType = "plate",
                Spec = string.Format("{0}×Ø{1} 销轴, 输出轴 Ø{2}", N, 2 * PinR, OutShaftD), Process = "锻-粗车-调质-精车-钻镗销孔",
                Notes = "销轴根部 R" + (0.5 * M).ToString("0.#") + "、输出轴根部 R" + M.ToString("0.#") + " 圆角不得省略|销轴分布圆位置度 φ0.03|未注尺寸公差 GB/T 1804-m" }, m =>
            {
                Sk.XY(m, -Gap).Circ(0, 0, 2 * CarrierR).Ext(PlateT, -1).Name = "行星架板";
                var pins = Sk.XY(m, -2.5 * M);                           // 销轴 z∈[-2.5m, b], 根部埋入板内
                foreach (var c in PinCenters()) pins.Circ(c[0], c[1], 2 * PinR);
                pins.Ext(B + 2.5 * M, 1).Name = "销轴";
                Sk.XY(m, -Gap).Circ(0, 0, OutShaftD).Ext(OutShaftL, -1).Name = "输出轴";
                var lh = Sk.XY(m, 0);
                for (int i = 0; i < N; i++)
                {
                    double a = 2 * Math.PI * (i + 0.5) / N;
                    lh.Circ(0.8 * A * Math.Cos(a), 0.8 * A * Math.Sin(a), 9 * M);
                }
                lh.CutThru(0).Name = "减重孔";
                // 销轴基准轴: 按圆柱参数精确找销轴圆柱面 (原脚本按坐标点选, 受视图方向影响)
                var pc = PinCenters();
                for (int i = 0; i < N; i++) Features.AxisFromCylinder(m, 'Z', pc[i][0], pc[i][1], PinR, "PinAxis" + (i + 1));
                Features.FilletCircles(m, pc.Select(c => new[] { c[0], c[1], -Gap, PinR }).ToList(), 0.5 * M, "销轴根部圆角");
                Features.FilletCircles(m, new[] { new double[] { 0, 0, -Gap - PlateT, OutShaftD / 2 } }, M, "输出轴根部圆角");
                Features.AxisFromPlanes(m, 1, 2, "GearAxis");
            });
        }

        List<double[]> PinCenters()
        {
            var r = new List<double[]>();
            for (int i = 0; i < N; i++) r.Add(new[] { A * Math.Cos(2 * Math.PI * i / N), A * Math.Sin(2 * Math.PI * i / N) });
            return r;
        }

        // ================================================================== 装配
        const double TEST_DEG = 30.0;
        const string TEST_MATE = "校验角度(临时)";
        string front, right;   // 模板中的基准面名称 (与语言相关, 从零件读取)

        public override List<AsmTarget> BuildAssemblies()
        {
            asmPath = Path.Combine(Run.Models, "PG-000_行星齿轮减速器.SLDASM");
            sunP = Cat.P("SUN"); planetP = Cat.P("PLANET"); ringP = Cat.P("RING"); carrierP = Cat.P("CARRIER");
            Sw.CloseAll();
            ModelDoc2 probe = Sw.Open(sunP);
            var pl = Sw.Planes(probe);
            front = pl[0].Name; right = pl[2].Name;
            Sw.CloseAll();

            // 齿轮配合方向与 SolidWorks 内部实体排序有关: 先驱动校验, 不对就翻转后重建 (最多 4 次)
            var flips = Enumerable.Range(0, N).Select(i => new[] { false, true }).ToArray();
            MateAsm asm = null; Component2 sun = null, carrier = null; List<Component2> planets = null;
            for (int attempt = 1; ; attempt++)
            {
                asm = Build(flips, out sun, out carrier, out planets);
                double s = MateAsm.AngleZ(sun), c = MateAsm.AngleZ(carrier);
                var errs = planets.Select(p => Math.Abs(MateAsm.Wrap(MateAsm.AngleZ(p) - PlanetSpin(s)))).ToList();
                bool carrierOk = Math.Abs(MateAsm.Wrap(c - s / Ratio)) < 0.01;
                Sw.Log(string.Format("[齿轮配合校验 {0}] 太阳轮 {1:F3}° 行星架 {2:F3}° (理论 {3:F3}°) 行星轮误差 [{4}]", attempt, s, c, s / Ratio,
                    string.Join(", ", errs.Select(e => e.ToString("F3")))));
                if (carrierOk && errs.All(e => e < 0.01)) break;
                if (attempt >= 4) throw new Exception("齿轮配合方向无法自动校正");
                if (!carrierOk) flips[0][1] = !flips[0][1];                       // 行星架由行星轮 1 的两个配合决定: 只翻齿圈配合
                else
                {
                    if (errs[0] >= 0.01) { flips[0][0] = !flips[0][0]; flips[0][1] = !flips[0][1]; }
                    for (int i = 1; i < N; i++) if (errs[i] >= 0.01) flips[i][0] = !flips[i][0];
                }
                asm.Close();
            }
            // 删除临时角度配合, 用临时重合配合把太阳轮带回 0° 后删除, 保留 1 个自由度供拖动/运动算例
            asm.DeleteMate(TEST_MATE);
            asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(sun, right, "PLANE"), asm.In(ringComp, right, "PLANE") }, "归零(临时)",
                     (int)swMateAlign_e.swMateAlignALIGNED);
            asm.Doc.EditRebuild3();
            asm.DeleteMate("归零(临时)");
            asm.Doc.ShowNamedView2("", (int)swStandardViews_e.swIsometricView);
            asm.Doc.ViewZoomtofit2();
            asm.Save();
            Sw.Log("装配体已保存, 齿轮配合方向: " + string.Join(" ", flips.Select(f => "(" + f[0] + "," + f[1] + ")")));
            asm.Close();
            Cat.Reg["ASM_TOP"] = asmPath;
            return new List<AsmTarget> { new AsmTarget { Path = asmPath, Main = true } };
        }

        Component2 ringComp;

        /// <summary>行星轮绝对转角 (太阳轮转 s 时)</summary>
        double PlanetSpin(double s) { double c = s / Ratio; return c - (double)ZS / ZP * (s - c); }

        MateAsm Build(bool[][] flips, out Component2 sun, out Component2 carrier, out List<Component2> planets)
        {
            var asm = MateAsm.Create(asmPath, new[] { ringP, sunP, carrierP, planetP });
            ringComp = asm.Add(ringP, Xf.T(0, 0, 0));
            sun = asm.Add(sunP, Xf.T(0, 0, 0));
            carrier = asm.Add(carrierP, Xf.T(0, 0, 0));
            planets = new List<Component2>();
            foreach (var c in PinCenters()) planets.Add(asm.Add(planetP, Xf.T(c[0], c[1], 0)));
            asm.Doc.EditRebuild3();
            var R = ringComp; var S = sun; var K = carrier;
            asm.Fix(R);
            int AL = (int)swMateAlign_e.swMateAlignALIGNED;
            asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(S, "GearAxis", "AXIS"), asm.In(R, "GearAxis", "AXIS") }, "同轴-太阳轮");
            asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(S, front, "PLANE"), asm.In(R, front, "PLANE") }, "端面-太阳轮", AL);
            asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(K, "GearAxis", "AXIS"), asm.In(R, "GearAxis", "AXIS") }, "同轴-行星架");
            asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(K, front, "PLANE"), asm.In(R, front, "PLANE") }, "端面-行星架", AL);
            for (int i = 0; i < N; i++)
            {
                asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(planets[i], "GearAxis", "AXIS"), asm.In(K, "PinAxis" + (i + 1), "AXIS") }, "同轴-行星轮" + (i + 1));
                asm.Mate(swMateType_e.swMateCOINCIDENT, new[] { asm.In(planets[i], front, "PLANE"), asm.In(K, front, "PLANE") }, "端面-行星轮" + (i + 1), AL);
            }
            asm.Doc.EditRebuild3();
            // 齿轮配合: 比例按齿数。只用最小无冗余集合 (N+1 个配合 → 恰好 1 个自由度):
            // 冗余齿轮配合会让拖动求解分摊误差、破坏啮合相位
            for (int i = 0; i < N; i++)
            {
                asm.Mate(swMateType_e.swMateGEAR, new[] { asm.In(S, "GearAxis", "AXIS"), asm.In(planets[i], "GearAxis", "AXIS") },
                         "齿轮-太阳轮-行星轮" + (i + 1), flip: flips[i][0], num: ZS, den: ZP);
                if (i == 0)
                    asm.Mate(swMateType_e.swMateGEAR, new[] { asm.In(planets[i], "GearAxis", "AXIS"), asm.In(R, "GearAxis", "AXIS") },
                             "齿轮-行星轮1-齿圈", flip: flips[i][1], num: ZP, den: ZR);
            }
            asm.Doc.EditRebuild3();
            // 临时角度配合驱动太阳轮转 TEST_DEG: 新添加的配合才会触发完整求解
            asm.Mate(swMateType_e.swMateANGLE, new[] { asm.In(S, right, "PLANE"), asm.In(R, right, "PLANE") }, TEST_MATE,
                     angleRad: TEST_DEG * Math.PI / 180);
            asm.Doc.EditRebuild3();
            return asm;
        }

        // ================================================================== 校核: 多角度运动学 + 干涉
        static readonly double[] ANGLES = { 10, 25, 45, 60, 90, 120, 150, 180, 0 };

        public override void ExtraChecks(List<AsmTarget> targets)
        {
            Sw.Head("运动学校核 (逐角度临时配合驱动)");
            asmPath = targets.Single(t => t.Main).Path;          // 续跑时 BuildAssemblies 未执行
            Sw.CloseAll();
            ModelDoc2 probe = Sw.Open(Cat.P("SUN"));
            right = Sw.Planes(probe)[2].Name;
            Sw.CloseAll();
            ModelDoc2 md = Sw.Open(asmPath);
            Sw.Activate(md);                                     // SelectByID2 / AddMate5 作用于活动文档
            AssemblyDoc ad = (AssemblyDoc)md;
            var name = Path.GetFileNameWithoutExtension(asmPath);
            var comps = ((object[])ad.GetComponents(true)).Cast<Component2>().ToList();
            Component2 S = comps.Single(c => c.Name2.StartsWith("PG-001")), K = comps.Single(c => c.Name2.StartsWith("PG-004")),
                       R = comps.Single(c => c.Name2.StartsWith("PG-003"));
            var P = comps.Where(c => c.Name2.StartsWith("PG-002")).OrderBy(c => int.Parse(c.Name2.Substring(c.Name2.LastIndexOf('-') + 1))).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("# 运动学与干涉校核");
            sb.AppendLine();
            sb.AppendLine(string.Format("> 传动比理论 i = 1 + {0}/{1} = {2:0.###}。每个角度临时添加角度配合驱动太阳轮 (新配合触发完整求解), 读回各零部件位姿后删除。", ZR, ZS, Ratio));
            sb.AppendLine("> 残差 = 太阳轮转角、行星架转角、各行星轮自转与公转位置相对理论值的最大偏差。该结果只说明配合体系与理论运动关系一致, 不代表真实传动误差。");
            sb.AppendLine();
            sb.AppendLine("| 太阳轮 (°) | 行星架 (°) | 行星架理论 (°) | 行星轮自转理论 (°) | 最大残差 (°) | 干涉 (处) |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|");
            double worst = 0; int itfTotal = 0;
            var extraTurns = new List<string>();
            foreach (double deg in ANGLES)
            {
                md.ClearSelection2(true);
                if (!md.Extension.SelectByID2(right + "@" + S.Name2 + "@" + name, "PLANE", 0, 0, 0, false, 1, null, 0) ||
                    !md.Extension.SelectByID2(right + "@" + R.Name2 + "@" + name, "PLANE", 0, 0, 0, true, 1, null, 0))
                    throw new Exception("选择太阳轮/齿圈基准面失败: " + right + "@" + S.Name2 + "@" + name);
                int err;
                double r = deg * Math.PI / 180;
                if (deg == 0) ad.AddMate5((int)swMateType_e.swMateCOINCIDENT, (int)swMateAlign_e.swMateAlignALIGNED, false, 0, 0, 0, 1, 1, 0, 0, 0, false, false, 0, out err);
                else ad.AddMate5((int)swMateType_e.swMateANGLE, (int)swMateAlign_e.swMateAlignCLOSEST, false, 0, 0, 0, 1, 1, r, r, r, false, false, 0, out err);
                md.ClearSelection2(true);
                md.EditRebuild3();
                if (err != (int)swAddMateError_e.swAddMateError_NoError) throw new Exception("临时配合失败 err=" + err);   // NoError = 1, 不是 0
                double s = MateAsm.AngleZ(S), c = MateAsm.AngleZ(K);
                var spins = new List<double>(); var orbits = new List<double>();
                for (int i = 0; i < P.Count; i++)
                {
                    double[] o; spins.Add(MateAsm.AngleZ(P[i], out o));
                    orbits.Add(Math.Atan2(o[1], o[0]) * 180 / Math.PI - 360.0 * i / N);
                }
                int turns; double ec, ep;
                double e = Residual(-deg, s, c, spins, orbits, out turns, out ec, out ep);
                if (turns != 0) extraTurns.Add(string.Format("驱动到 {0:F0}° 时太阳轮实际转到 {1:F0}° (相差 {2} 圈)", 0.0 - deg, 360.0 * turns - deg, turns));
                int n = Interferences(ad);
                worst = Math.Max(worst, e); itfTotal += n;
                sb.AppendLine(string.Format("| {0:F3} | {1:F3} | {2:F3} | {3:F3} | {4:F5} | {5} |", s, c, MateAsm.Wrap(ec), MateAsm.Wrap(ep), e, n));
                Sw.Log(string.Format("太阳轮 {0,8:F3}°  行星架 {1,8:F3}° (理论 {2,8:F3})  最大残差 {3:F5}°  干涉 {4}", s, c, MateAsm.Wrap(ec), e, n));
                // 删除本次临时配合 (配合文件夹末尾)
                Feature last = null;
                for (Feature f = (Feature)md.FirstFeature(); f != null; f = (Feature)f.GetNextFeature())
                    if (f.GetTypeName2() == "MateGroup")
                        for (Feature sf = (Feature)f.GetFirstSubFeature(); sf != null; sf = (Feature)sf.GetNextSubFeature()) last = sf;
                md.ClearSelection2(true);
                last.Select2(false, 0);
                md.Extension.DeleteSelection2(0);
                md.ClearSelection2(true);
                md.EditRebuild3();
            }
            Sw.CloseAll();    // 不保存: 校核不改变装配体
            if (extraTurns.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("> 整圈修正: " + string.Join("; ", extraTurns) + "。理论值按实际转角计算, 行星架与各行星轮均须与之吻合。");
            }
            string f2 = Path.Combine(Run.Checks, "kinematics.md");
            File.WriteAllText(f2, sb.ToString(), new UTF8Encoding(false));
            Run.Check("运动学", worst < 0.01, string.Format("{0} 个角度, 最大残差 {1:F5}° (限 0.01°)", ANGLES.Length, worst), f2);
            Run.Check("运动中干涉", itfTotal == 0, string.Format("{0} 个角度, 干涉合计 {1} 处", ANGLES.Length, itfTotal), f2);
            Run.Metric("运动学/最大残差°", worst, tol: 0.001);
        }

        /// <summary>
        /// 一个驱动角下的最大残差 (°)。太阳轮转角只能读到模 360° 的值 (配合在相差整圈处同样满足, 求解器可能多转一圈,
        /// 见 api_calibration 1-18), 而行星架与行星轮的位置取决于实际转了几圈 → 逐个整圈数 k 计算理论值, 取最吻合的 k;
        /// 行星架、各行星轮自转与公转必须在同一个 k 下同时吻合。turns = k, ec / ep = 该 k 下行星架与行星轮自转理论值。
        /// spins = 行星轮自转角, orbits = 行星轮公转位置减去其初始相位 (理论上等于行星架转角)。
        /// </summary>
        internal double Residual(double sunCmd, double s, double c, IList<double> spins, IList<double> orbits, out int turns, out double ec, out double ep)
        {
            double best = double.MaxValue; turns = 0; ec = ep = 0;
            for (int k = -3; k <= 3; k++)
            {
                double st = sunCmd + 360.0 * k, eck = st / Ratio, epk = PlanetSpin(st);
                double e = Math.Max(Math.Abs(MateAsm.Wrap(s - st)), Math.Abs(MateAsm.Wrap(c - eck)));
                for (int i = 0; i < spins.Count; i++)
                    e = Math.Max(e, Math.Max(Math.Abs(MateAsm.Wrap(spins[i] - epk)), Math.Abs(MateAsm.Wrap(orbits[i] - eck))));
                if (e < best) { best = e; turns = k; ec = eck; ep = epk; }
            }
            return best;
        }

        static int Interferences(AssemblyDoc a)
        {
            InterferenceDetectionMgr idm = a.InterferenceDetectionManager;
            idm.TreatCoincidenceAsInterference = false;
            idm.IgnoreHiddenBodies = true;
            object[] r = (object[])idm.GetInterferences();
            idm.Done();
            return r == null ? 0 : r.Length;
        }

        public override void ConfigureDrawings()
        {
            Drw.PartGeneralNote = null;
            Drw.AsmNotes = "1. 装配前零件清洗干净, 齿面涂润滑脂。\n2. 行星轮与销轴为间隙配合, 装配后转动灵活无卡滞。\n3. 太阳轮输入、行星架输出, 传动比 i=" + Ratio.ToString("0.###") + "。";
        }
    }
}
