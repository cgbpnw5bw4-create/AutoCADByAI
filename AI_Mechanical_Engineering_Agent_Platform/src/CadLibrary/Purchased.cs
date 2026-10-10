using System;
using SolidWorks.Interop.sldworks;
using CadKernel;

namespace CadLibrary
{
    /// <summary>
    /// 外购件样本库: 按样本关键安装尺寸建包络模型, 质量取样本值 (包络体积×密度会严重高估, 风机算出 295kg, 实际约 95kg)。
    /// 型号为选型建议, 采购前须按供应商最新样本核对安装尺寸。
    /// 来源: CFC-650 铜箔清洗机 V2 (2026-09, 全部在 SolidWorks 2023 中建模并参与干涉/孔位校核)。
    /// 用法: 设计项目在零件步骤调用 Buy.BuildAll() (配合 Cat.Lazy 只建实际用到的), 装配时用 Cat.P(Buy.UCF205) 取路径。
    /// 局部坐标约定见各条目注释 (安装面多为 z=0)。
    /// </summary>
    public static class Buy
    {
        const string D = @"外购件";
        public const string UCF205 = "B_UCF205", CHUCK = "B_CHUCK", AIRSHAFT = "B_AIRSHAFT",
            BRAKE = "B_BRAKE", CPL_BIG = "B_CPL100", CPL_BIG35 = "B_CPL100_35", CPL_SMALL = "B_CPL40", SERVO = "B_SERVO", GEARMOTOR = "B_GM15",
            LOADCELL = "B_LOADCELL", RAIL = "B_RAIL", BLOCK = "B_BLOCK", EPC_ACT = "B_EPCACT", EPC_SNS = "B_EPCSNS",
            CYL = "B_CYL", KNIFE = "B_KNIFE", BLOWER = "B_BLOWER", OVENFAN = "B_OVENFAN", HEATBOX = "B_HEATBOX",
            TIHEATER = "B_TIHEATER", USTRANS = "B_USTRANS", USGEN = "B_USGEN", CABINET = "B_CABINET", PUMP = "B_PUMP",
            BAGFILTER = "B_BAGFILTER", VALVE40 = "B_VALVE40", FOOT = "B_FOOT", NOZZLE = "B_NOZZLE", PBRG = "B_PBRG6004",
            BRG6205 = "B_6205", BUSH30 = "B_BUSH30", ELBOW25 = "B_ELB25", ELBOW40 = "B_ELB40", TEE25 = "B_TEE25";

        static Part B(string id, string code, string name, string spec, string brand, double kg, double[] color)
        {
            return new Part { Id = id, Dir = D, Kind = Kind.Buy, Code = code, Name = name, Spec = spec, Brand = brand,
                              CatalogKg = kg, Mat = Mat.STEEL, Color = color };
        }

        public static void BuildAll()
        {
            Sw.Head("外购件库");

            // ---- 不锈钢方法兰带座轴承 SUCF205: d25, L95, J70, A27, B34.1, S14.3, 螺栓 M10 ----
            // 局部: 安装面 z=0, 座体朝 +z; 内圈宽 34.1, 中心距安装面 14.3 -> 内圈伸入安装面内 2.75 (墙板须开 φ45 让位孔)
            Cat.Make(B(UCF205, "WG-001", "不锈钢带座轴承 SUCF205", "d25 L95 J70 A27 B34.1, 4-M10",
                "NTN/FYH/ASAHI 不锈钢系列 同等", 0.95, Col.Sus), m =>
            {
                Sk.XY(m, 0).RRect(-47.5, -47.5, 47.5, 47.5, 12).Ext(14, 1);
                Sk.XY(m, 0).Circ(0, 0, 64).Ext(27, 1);
                Sk.XY(m, -2.75).Circ(0, 0, 38).Ext(34.1, 1);
                Sk.XY(m, -5).Circ(0, 0, 25).CutThru(0);
                var s = Sk.XY(m, 0);
                foreach (double x in new[] { -35.0, 35.0 }) foreach (double y in new[] { -35.0, 35.0 }) s.Circ(x, y, 12);
                s.CutThru(0);
            });

            // ---- 塑料轴承 6004 (PP 圈/玻璃珠, 耐酸碱全浸没) 20×42×12 ----
            Cat.Make(B(PBRG, "WG-002", "耐蚀塑料轴承 6004", "20×42×12 PP圈+玻璃珠+PP保持架",
                "KMS / igus xiros 同等", 0.02, Col.PE), m => Sk.XY(m, 0).Circ(0, 0, 42).Circ(0, 0, 20).Ext(12, 1));

            // ---- 深沟球轴承 6205-2RS 25×52×15 (压辊) ----
            Cat.Make(B(BRG6205, "GB/T 276", "深沟球轴承 6205-2RS", "25×52×15 双面接触密封",
                "HRB/ZWZ/SKF", 0.13, Col.Steel), m => Sk.XY(m, 0).Circ(0, 0, 52).Circ(0, 0, 25).Ext(15, 1));

            // ---- 自润滑法兰轴套 φ30 (压辊摆臂转轴) ----
            Cat.Make(B(BUSH30, "WG-003", "法兰型自润滑轴套 FJDB-3040", "d30 D38 L25, 法兰 φ50×5",
                "JDB 铜基镶石墨 同等", 0.12, Col.Yellow), m =>
            {
                Sk.XY(m, 0).Circ(0, 0, 50).Ext(5, 1);
                Sk.XY(m, 5).Circ(0, 0, 38).Ext(20, 1);
                Sk.XY(m, -1).Circ(0, 0, 30).CutThru(0);
            });

            // ---- 安全夹头 □50 (法兰式): 法兰 180 方, 4-M12 @150; 局部 z=0 为安装面(贴墙板内侧), +z 朝机器中心 ----
            // 方孔 50, 深 55, 上方开口(掀盖式); 尾轴 φ40×110 穿出墙板
            Cat.Make(B(CHUCK, "WG-004", "安全夹头 □50 法兰式", "方孔 50, 额定载荷 2000kg, 尾轴 φ40",
                "国产 KSC-50 系列 同等", 16, Col.Red), m =>
            {
                Sk.XY(m, 0).RRect(-90, -90, 90, 90, 15).Ext(20, 1);
                Sk.XY(m, 20).Circ(0, 0, 150).Ext(55, 1);
                Sk.XY(m, 0).Circ(0, 0, 40).Ext(110, -1);
                Sk.XY(m, 75).Rect(-25, -25, 25, 90).Cut(55, -1);        // 方孔 + 上方开口
                var s = Sk.XY(m, 0);
                foreach (double x in new[] { -75.0, 75.0 }) foreach (double y in new[] { -75.0, 75.0 }) s.Circ(x, y, 14);
                s.CutThru(0);
            });

            // ---- 气胀轴 6" 键条式: 胀径 φ152, 有效长 700; 轴颈 φ60, 方头 50×50×50; 总长 888 ----
            Cat.Make(B(AIRSHAFT, "WG-005", "气胀轴 6\" 键条式", "胀后 φ152.4, 有效 700, 方头 □50, 承载 1800kg",
                "国产 6 寸键条式 同等", 48, Col.Alu), m =>
            {
                Sk.XY(m, -350).Circ(0, 0, 150).Ext(700, 1);
                Sk.XY(m, -394).Circ(0, 0, 60).Ext(788, 1);
                Sk.XY(m, 394).Rect(-25, -25, 25, 25).Ext(50, 1);
                Sk.XY(m, -394).Rect(-25, -25, 25, 25).Ext(50, -1);
            });

            // ---- 梅花联轴器 φ100×110: 孔 φ40 (夹头尾轴) / φ30 (制动器) 或 φ35 (减速电机) ----
            foreach (int b2 in new[] { 30, 35 })
            {
                int bb = b2;
                Cat.Make(B(bb == 30 ? CPL_BIG : CPL_BIG35, bb == 30 ? "WG-007" : "WG-007A", "梅花弹性联轴器 LM100-40/" + bb,
                    "D100 L110, 孔 φ40/φ" + bb + " 带键槽, 许用 250N·m", "国产 LM 系列 同等", 3.2, Col.Orange), m =>
                {
                    Sk.XY(m, 0).Circ(0, 0, 100).Ext(110, 1);
                    Sk.XY(m, 0).Circ(0, 0, 40).Cut(50, 1);
                    Sk.XY(m, 110).Circ(0, 0, bb).Cut(50, -1);
                });
            }

            // ---- 小梅花联轴器 φ40×66: 孔 φ20 (辊轴) / φ14 (减速机) ----
            Cat.Make(B(CPL_SMALL, "WG-008", "梅花弹性联轴器 LM40", "D40 L66, 孔 φ20/φ14 带键槽, 许用 20N·m",
                "国产 LM 系列 同等", 0.25, Col.Orange), m =>
            {
                Sk.XY(m, 0).Circ(0, 0, 40).Ext(66, 1);
                Sk.XY(m, 0).Circ(0, 0, 20).Cut(25, 1);
                Sk.XY(m, 66).Circ(0, 0, 14).Cut(25, -1);
            });

            // ---- 伺服电机 400W + 行星减速机 PLF60 i=10: 输出法兰面 z=0, 输出轴 φ14×36 朝 -z, 止口 φ50×3 ----
            Cat.Make(B(SERVO, "WG-009", "伺服减速电机 400W i=10", "伺服 400W 3000rpm + 行星减速机 60 法兰 i=10, 额定输出 12N·m",
                "汇川/台达 伺服 + 精密行星减速机 同等", 3.8, Col.Servo), m =>
            {
                Sk.XY(m, 0).Rect(-30, -30, 30, 30).Ext(70, 1);
                Sk.XY(m, 70).Rect(-30, -30, 30, 30).Ext(120, 1);
                Sk.XY(m, 150).Rect(-15, 30, 15, 55).Ext(30, 1);      // 编码器插座
                Sk.XY(m, 0).Circ(0, 0, 50).Ext(3, -1);
                Sk.XY(m, -3).Circ(0, 0, 14).Ext(33, -1);
                var s = Sk.XY(m, 0);
                for (int i = 0; i < 4; i++) { double a = Math.PI / 4 + i * Math.PI / 2; s.Circ(35 * Math.Cos(a), 35 * Math.Sin(a), 5); }
                s.Cut(10, 1);                                         // 4-M5 深 10
            });

            // ---- 张力检测器 (轴承座式) 500N: 底板 120×90×20, 轴承座 φ72×40, 孔 φ25, 4-φ9 @(±45,±30) ----
            Cat.Make(B(LOADCELL, "WG-011", "张力检测器 轴承座式 500N", "额定 500N/只, 内置轴承 d25, 配张力放大器",
                "三菱 LX-TD / Nireco 同等", 2.6, Col.Blue), m =>
            {
                Sk.XY(m, 0).Rect(-60, -45, 60, 45).Ext(20, 1);
                Sk.XY(m, 0).Circ(0, 0, 72).Ext(40, 1);
                Sk.XY(m, 0).Rect(30, 45, 60, 60).Ext(30, 1);         // 接线盒 (远离纠偏传感器支架一侧)
                Sk.XY(m, -5).Circ(0, 0, 25).CutThru(0);
                var s = Sk.XY(m, 0);
                foreach (double x in new[] { -45.0, 45.0 }) foreach (double y in new[] { -30.0, 30.0 }) s.Circ(x, y, 9);
                s.CutThru(0);
            });

            // ---- 直线导轨 HGR25 × 1200: 轨宽 23 高 22; 沿 Z 居中; 安装孔 φ7 沉孔 φ11 @60 ----
            Cat.Make(B(RAIL, "WG-012", "直线导轨 HGR25-1200", "HGR25 L=1200, 孔距 60, P 级",
                "HIWIN / 上银 同等", 3.9, Col.Steel), m =>
            {
                Sk.XY(m, -600).Rect(-11.5, 0, 11.5, 22).Ext(1200, 1);
                var s = Sk.XZ(m, 0);
                for (int i = 0; i < 20; i++) s.Circ(0, -570 + 60 * i, 7);
                s.CutThru(0);
                var c = Sk.XZ(m, 22);
                for (int i = 0; i < 20; i++) c.Circ(0, -570 + 60 * i, 11);
                c.Cut(9, -1);
            });

            // ---- 滑块 HGH25CA: 宽 48 长 84, 顶面距轨底 40; 顶面 4-M8 @35×35 ----
            Cat.Make(B(BLOCK, "WG-013", "法兰滑块 HGH25CA", "配 HGR25, 顶面 4-M8, 额定动载 26.5kN",
                "HIWIN / 上银 同等", 0.69, Col.Steel), m =>
            {
                Sk.XY(m, -42).Rect(-24, 5.5, 24, 40).Ext(84, 1);
                Sk.XY(m, -50).Rect(-11.5, 0, 11.5, 22).Cut(100, 1);
                var s = Sk.XZ(m, 40);
                foreach (double x in new[] { -17.5, 17.5 }) foreach (double z in new[] { -17.5, 17.5 }) s.Circ(x, z, 8);
                s.Cut(12, -1);
            });

            // ---- 纠偏电动推杆: 局部 +Z 为推出方向; 后耳 z=0, 本体 z 20..400, 推杆 φ25 至 z=470 (中位), 前耳至 z=490 ----
            Cat.Make(B(EPC_ACT, "WG-014", "纠偏电动推杆 行程 100", "行程 100 (±50), 推力 3000N, 24VDC 伺服",
                "BST / E+L / 国产 纠偏专用 同等", 7.5, Col.Servo), m =>
            {
                Sk.XY(m, 0).Rect(-12, -20, 12, 20).Ext(20, 1);
                Sk.XY(m, 20).Rect(-40, -40, 40, 40).Ext(380, 1);
                Sk.XY(m, 60).Rect(-40, 40, 40, 110).Ext(200, 1);     // 电机
                Sk.XY(m, 400).Circ(0, 0, 25).Ext(70, 1);
                Sk.XY(m, 470).Rect(-12, -20, 12, 20).Ext(20, 1);
            });

            // ---- 纠偏边缘传感器 (超声波 U 形叉): 叉口高 20, 深 50 ----
            Cat.Make(B(EPC_SNS, "WG-015", "超声波边缘传感器 U 形", "叉口 20×50, 检测范围 ±15, 对透明/反光箔材适用",
                "E+L FE52 / BST 同等", 0.6, Col.Blue), m =>
            {
                Sk.XZ(m, 0).Rect(-20, -30, 20, 30).Ext(90, 1);
                Sk.XZ(m, 35).Rect(-25, -30, 25, 20).Cut(20, 1);     // 叉口 (Z -30..20, 开口朝 -Z)
            });

            // ---- 气缸 SC32×100 (含后耳/前耳): 局部 +Z 为推出方向, 后耳销孔 z=0, 前耳销孔 z=285 (中位) ----
            Cat.Make(B(CYL, "WG-016", "标准气缸 SC32×100 带耳环", "缸径 32, 行程 100, 0.5MPa 推力 400N",
                "亚德客 / SMC 同等", 1.4, Col.Alu), m =>
            {
                Sk.XY(m, -10).Rect(-8, -10, 8, 10).Ext(30, 1);
                Sk.XY(m, 20).Rect(-22.5, -22.5, 22.5, 22.5).Ext(150, 1);
                Sk.XY(m, 170).Circ(0, 0, 12).Ext(105, 1);
                Sk.XY(m, 275).Rect(-8, -10, 8, 10).Ext(20, 1);
            });

            // ---- 不锈钢风刀 有效长 800: 刀体 50×60, 两端安装耳 至 ±469, 进气口 φ50 朝 +Y ----
            Cat.Make(B(KNIFE, "WG-017", "不锈钢风刀 L800", "有效长 800, 缝隙 0.8 可调, 进气 φ50, 出口风速 >80m/s",
                "国产 不锈钢风刀 同等", 5.2, Col.Sus), m =>
            {
                Sk.XY(m, -400).Rect(-25, -30, 25, 30).Ext(800, 1);
                Sk.XY(m, 400).Rect(-15, -15, 15, 15).Ext(69, 1);
                Sk.XY(m, -400).Rect(-15, -15, 15, 15).Ext(69, -1);
                Sk.XZ(m, 30).Circ(0, 0, 50).Ext(60, 1);
                Sk.XY(m, 469).Circ(0, 0, 10).Cut(25, -1);           // 两端耳部 M10 螺纹孔 深 25
                Sk.XY(m, -469).Circ(0, 0, 10).Cut(25, 1);
            });

            // ---- 电加热风箱 18kW ----
            Cat.Make(B(HEATBOX, "WG-020", "电加热风箱 18kW", "SUS304 翅片加热管 18kW, 带超温保护, 500×300×300",
                "国产 定制 同等", 35, Col.Orange), m =>
            {
                Sk.XZ(m, 0).Rect(0, -150, 500, 150).Ext(300, 1);
                Sk.XZ(m, 300).Rect(100, -100, 400, 100).Ext(80, 1);  // 接线盒
            });

            // ---- L 型投入式钛加热器 6kW (挂槽端壁): 局部原点 = 槽端壁顶面外沿中点; -X 伸入槽内 ----
            Cat.Make(B(TIHEATER, "WG-021", "L型投入式钛加热器 6kW", "TA2 钛管 φ16×3 支, 380V 6kW, 配 PT100 与液位开关",
                "国产 钛加热器 定制 同等", 6, Col.Orange), m =>
            {
                foreach (double z in new[] { -30.0, 0, 30.0 })
                {
                    Sk.YZ(m, -625).Circ(z, -375, 16).Ext(610, 1);           // 水平发热段 X -625..-15, Y -375
                    Sk.XZ(m, -383).Circ(-20, z, 16).Ext(398, 1);            // 立管 X -20, Y -383..15
                }
                Sk.YZ(m, -28).Rect(-45, 5, 45, 25).Ext(58, 1);              // 挂钩横跨 X -28..30
                Sk.XY(m, -50).Rect(10, -20, 70, 90).Ext(100, 1);            // 接线盒
            });

            // ---- 投入式超声振板 1.2kW 300×250×60 ----
            Cat.Make(B(USTRANS, "WG-022", "投入式超声振板 1.2kW", "40kHz 1200W, SUS316L 外壳 300×250×60, 配 φ30 护管",
                "国产 超声设备 同等", 12, Col.Yellow), m =>
            {
                Sk.XZ(m, 0).Rect(-150, -125, 150, 125).Ext(60, 1);
                Sk.XZ(m, 60).Circ(-120, 0, 30).Ext(20, 1);
            });

            Cat.Make(B(VALVE40, "WG-027", "PVC 双活接球阀 DN40", "DN40 PN10, U-PVC/EPDM",
                "国产 同等", 0.8, Col.Blue), m =>
            {
                Sk.XZ(m, 0).Circ(0, 0, 70).Ext(130, 1);
                Sk.XY(m, 0).Rect(0, 55, 120, 75).Ext(20, 0);               // 手柄
            });

            // ---- 重型可调地脚 M20: 底盘 φ120, 螺杆 M20×150 (局部 +Y) ----
            Cat.Make(B(FOOT, "WG-028", "重型可调地脚 M20×150", "底盘 φ120 带防滑垫, 螺杆 M20×150, 静载 3000kg",
                "国产 同等", 1.9, Col.Zinc), m =>
            {
                Sk.XZ(m, 0).Circ(0, 0, 120).Ext(20, 1);
                Sk.XZ(m, 20).Circ(0, 0, 20).Ext(150, 1);
                Sk.XZ(m, 55).Hex(0, 0, 30).Circ(0, 0, 20).Ext(18, 1);      // 锁紧螺母
            });

            // ---- 扇形喷嘴 1/4" (PP, 快拆): 局部 +X 为喷射方向 ----
            Cat.Make(B(NOZZLE, "WG-029", "PP 扇形喷嘴 1/4\"", "1/4\" 外螺纹, 喷角 110°, 0.8L/min@2bar, 带快拆座",
                "Spraying Systems / 国产 同等", 0.02, Col.Blue), m =>
            {
                Sk.YZ(m, 0).Hex(0, 0, 18).Ext(12, 1);
                Sk.YZ(m, 12).Circ(0, 0, 14).Ext(16, 1);
            });

            // ---- 管件 ----
            Elbow(ELBOW25, "WG-030", "90° 弯头 DN25", "SUS316L / PP-H φ32", 32, 28);
            Elbow(ELBOW40, "WG-031", "90° 弯头 DN40", "PP-H φ50", 50, 36);
            Cat.Make(B(TEE25, "WG-032", "等径三通 DN25", "PP-H φ32 热熔", "国产 同等", 0.05, Col.Blue), m =>
            {
                Sk.XY(m, -28).Circ(0, 0, 38).Ext(56, 1);          // 主管沿 Z
                Sk.XZ(m, 0).Circ(0, 0, 38).Ext(28, 1);             // 支管朝 +Y
            });
        }

        /// <summary>90° 弯头: 两臂分别沿 +Z 与 +Y, 臂长 r; 局部原点在两臂轴线交点</summary>
        static void Elbow(string id, string code, string name, string spec, double d, double r)
        {
            Cat.Make(B(id, code, name, spec, "国产 同等", d > 40 ? 0.12 : 0.05, Col.Blue), m =>
            {
                Sk.XY(m, -d / 2).Circ(0, 0, d + 6).Ext(r + d / 2, 1);
                Sk.XZ(m, d / 2).Circ(0, 0, d + 6).Ext(r - d / 2, 1);
            });
        }
    }
}
