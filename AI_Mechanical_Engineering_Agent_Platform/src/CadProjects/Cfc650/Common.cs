using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using CadKernel;

namespace CadProjects.Cfc650
{
    /// <summary>通用自制件 (GY): 辊子、轴承座、角座、支柱、喷淋管 (样例只保留 M03 用到的)</summary>
    public static class Common
    {
        const string D = @"00_通用自制件";
        public const string GR80 = "GR80", GR80D = "GR80D", GR60 = "GR60", DIPHSG = "DIPHSG", DIPCAP = "DIPCAP",
            WBRK = "WBRK", PILLAR8 = "PILLAR8", MPLATE = "MPLATE", SPRAY = "SPRAY", SPRAYBRK = "SPRAYBRK";

        public const string NOTE_ROLL =
            "辊筒 φ83×5 无缝管 (GB/T 14976) 两端装闷头满焊, 焊后去应力, 整体精车|" +
            "辊面对两端轴承位公共轴线 A-B 径向圆跳动 ≤0.03, 两轴承位同轴度 φ0.02|" +
            "辊面 Ra0.4 抛光, 轴承位 Ra0.8, 其余 Ra3.2|动平衡 G6.3 (n=150r/min)|" +
            "未注倒角 C1, 未注尺寸公差 GB/T 1804-m|酸洗钝化后辊面贴保护膜";
        public const string NOTE_PLATE = "激光切割下料, 去毛刺, 锐边倒钝 C0.5|未注尺寸公差 GB/T 1804-m|表面拉丝 No.4 后酸洗钝化";

        static Part P(string id, string code, string name, Mat mat, double[] col, string spec, string blank, string proc, string drw)
        {
            return new Part { Id = id, Dir = D, Code = code, Name = name, Mat = mat, Color = col, Spec = spec, Blank = blank,
                              Process = proc, DrwType = drw, Surface = mat == Mat.Q235 ? "镀锌" : "酸洗钝化" };
        }

        public static void BuildAll()
        {
            Sw.Head("通用自制件");

            // ---------- 导辊 φ80×750 (从动) ----------
            // 半剖轮廓 (Z, r): 轴承位 φ25h7 (475~520) / 甩水环 φ40 (455~463) / 轴身 φ30 / 闷头 20 / 辊筒 φ80×3.5
            double[] idler = {
                -520,0, -520,11.5, -519,12.5, -475,12.5, -475,15, -463,15, -463,20, -455,20, -455,15, -375,15, -375,38, -373,40,
                373,40, 375,38, 375,15, 455,15, 455,20, 463,20, 463,15, 475,15, 475,12.5, 519,12.5, 520,11.5, 520,0,
                355,0, 355,36.5, -355,36.5, -355,0 };
            var pr = P(GR80, "CFC650-GY-001", "导辊 φ80×750", Mat.S304, Cfg.C_SUS, "辊面 φ80×750, 轴承位 φ25h7, 总长 1040",
                "辊筒 φ83×5 L760; 闷头 φ80×20; 轴头 φ35 圆钢", "下料-车闷头/轴头-组焊-去应力-精车-动平衡-抛光-钝化", "turn");
            pr.Fits = "25:h7"; pr.Notes = NOTE_ROLL;
            Cat.Make(pr, m => Turn.Solid(m, 'Z', idler));

            // ---------- 主动导辊 (传动侧 -Z 轴伸 φ20k6 + 键槽 6×3.5) ----------
            double[] drive = {
                -580,0, -580,9, -579,10, -520,10, -520,12.5, -475,12.5, -475,15, -463,15, -463,20, -455,20, -455,15, -375,15, -375,38, -373,40,
                373,40, 375,38, 375,15, 455,15, 455,20, 463,20, 463,15, 475,15, 475,12.5, 519,12.5, 520,11.5, 520,0,
                355,0, 355,36.5, -355,36.5, -355,0 };
            pr = P(GR80D, "CFC650-GY-002", "主动导辊 φ80×750", Mat.S304, Cfg.C_SUS, "辊面 φ80×750, 轴承位 φ25h7, 轴伸 φ20k6×60 键 6×6",
                "同 CFC650-GY-001, 传动端轴头加长", "同 CFC650-GY-001 + 铣键槽", "turn");
            pr.Fits = "25:h7;20:k6"; pr.Notes = NOTE_ROLL + "|键槽 6N9 × 3.5, 对轴线对称度 0.02";
            Cat.Make(pr, m =>
            {
                Turn.Solid(m, 'Z', drive);
                Sk.YZ(m, 0).Rect(-570, 6.5, -530, 11).Cut(6, 0);
            });

            // ---------- 浸没辊 φ60×750 (316L, 全浸没, 塑料轴承支承) ----------
            double[] dip = {
                -449,0, -449,9, -448,10, -375,10, -375,28, -374,30, 374,30, 375,28, 375,10, 448,10, 449,9, 449,0,
                360,0, 360,27, -360,27, -360,0 };
            pr = P(GR60, "CFC650-GY-003", "浸没辊 φ60×750", Mat.S316, Cfg.C_SUS316, "辊面 φ60×750, 轴颈 φ20h7, 密封焊接中空",
                "S31603 管 φ63.5×4.5 L760; 闷头 φ60×15; 轴头 φ25 圆钢", "下料-组焊(全密封)-气密试验-精车-抛光-钝化", "turn");
            pr.Fits = "20:h7";
            pr.Notes = "全部焊缝连续密封焊, 焊后 0.2MPa 气密试验 10min 无泄漏 (防止槽液渗入)|辊面对两轴颈公共轴线径向圆跳动 ≤0.05|" +
                       "辊面 Ra0.4 电解抛光, 轴颈 Ra0.8|未注倒角 C1, 未注尺寸公差 GB/T 1804-m|酸洗钝化";
            Cat.Make(pr, m => Turn.Solid(m, 'Z', dip));

            // ---------- 浸没辊轴承座 (UHMW-PE, U 形滑入槽体托座, 内装 6004 塑料轴承) ----------
            // 局部: 辊轴线在原点, 厚度 z 0..22 (z=0 朝辊子); 轴承孔 φ42 深 17, 轴端让位 φ26
            pr = P(DIPHSG, "CFC650-GY-004", "浸没辊轴承座", Mat.PE, Cfg.C_PE, "U 形 44.5×72.25×22, 轴承孔 φ42H8",
                "UHMW-PE 板 δ25", "铣外形-镗孔", "plate");
            pr.Fits = "42:H8"; pr.Surface = "—";
            pr.Notes = "UHMW-PE 板材, 铣削加工, 去毛刺|外形与槽体 U 形托座滑动配合, 单边间隙 0.1~0.3|轴承孔 φ42H8, 孔口倒角 C0.5";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, 0).Line(-22.25, 50, -22.25, 0).Arc(0, 0, -22.25, 0, 22.25, 0).Line(22.25, 0, 22.25, 50).Line(22.25, 50, -22.25, 50).Ext(22, 1);
                Sk.XY(m, 0).Circ(0, 0, 42).Cut(17, 1);
                Sk.XY(m, 17).Circ(0, 0, 26).Cut(5, 1);
            });

            // ---------- 浸没辊压盖 (防辊子上浮: 包角 180° 时张力合力 2T=200N >> 辊子水中净重 28N) ----------
            pr = P(DIPCAP, "CFC650-GY-005", "浸没辊压盖", Mat.S316, Cfg.C_SUS316, "80×22×6, 2-φ6.6", "S31603 板 δ6", "激光切割", "plate");
            pr.Notes = NOTE_PLATE; pr.Dxf = true; pr.DxfNormal = "Y";
            Cat.Make(pr, m =>
            {
                Sk.XZ(m, 0).Rect(-40, 0, 40, 22).Ext(6, 1);
                Sk.XZ(m, 0).Circ(-31, 11, 6.6).Circ(31, 11, 6.6).CutThru(0);
            });

            // ---------- 墙板角座 (SUS304 δ10 折弯): 立边贴墙板外侧 (z 0..10), 底边搭在机架纵梁上 ----------
            pr = P(WBRK, "CFC650-GY-007", "墙板角座", Mat.S304, Cfg.C_SUS, "δ10 折弯 100×100×159, 2-φ13 / 2-φ11",
                "S30408 板 δ10 100×249 (展开)", "激光切割-折弯", "sheet");
            pr.Notes = NOTE_PLATE + "|折弯内 R10, 两边垂直度 ≤0.3"; pr.Dxf = true; pr.DxfNormal = "X";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, 0).Rect(-50, 0, 50, 100).Ext(10, 1);
                Sk.XZ(m, 0).Rect(-50, 10, 50, 159).Ext(10, 1);
                Sk.XY(m, 0).Circ(-30, 60, 13).Circ(30, 60, 13).CutThru(0);
                Sk.XZ(m, 0).Circ(-30, 119, 11).Circ(30, 119, 11).CutThru(0);
            });

            // ---------- 电机支柱 φ20×140, 两端 M8 螺纹孔深 18 ----------
            pr = P(PILLAR8, "CFC650-GY-008", "电机支柱 φ20×140", Mat.S304, Cfg.C_SUS, "φ20×140, 两端 M8 深 18", "S30408 圆钢 φ22", "车-钻-攻丝", "turn");
            pr.Taps = "8:M8-6H 深18"; pr.Notes = "两端面平行度 ≤0.05, 4 件一组长度差 ≤0.05|未注倒角 C0.5, 未注公差 GB/T 1804-m";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, 0).Circ(0, 0, 20).Ext(WetMod.PILLAR_L, 1);
                Sk.XY(m, 0).Circ(0, 0, 8).Cut(18, 1);
                Sk.XY(m, WetMod.PILLAR_L).Circ(0, 0, 8).Cut(18, -1);
            });

            // ---------- 伺服电机安装板 δ12: 止口孔 φ50H8, 支柱孔 4-φ9, 电机螺钉孔 4-φ5.5 (PCD70) ----------
            pr = P(MPLATE, "CFC650-GY-009", "伺服电机安装板", Mat.S304, Cfg.C_SUS, "170×140×12, φ50H8", "S30408 板 δ12", "激光切割-镗孔", "plate");
            pr.Fits = "50:H8"; pr.Notes = NOTE_PLATE + "|φ50H8 止口孔与 4-φ9 支柱孔位置度 φ0.1"; pr.Dxf = true; pr.DxfNormal = "Z";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, 0).Rect(-85, -70, 85, 70).Ext(WetMod.MPLATE_T, 1);
                var s = Sk.XY(m, 0).Circ(0, 0, 50);
                for (int i = 0; i < 4; i++) s.Circ(WetMod.PILLAR[i, 0], WetMod.PILLAR[i, 1], 9);
                for (int i = 0; i < 4; i++) { double a = Math.PI / 4 + i * Math.PI / 2; s.Circ(35 * Math.Cos(a), 35 * Math.Sin(a), 5.5); }
                s.CutThru(0);
            });

            // ---------- 喷淋管 (S31603 φ32×2): Z -700..+455, 末端封板, 7 只 1/4" 喷嘴座朝 +X ----------
            pr = P(SPRAY, "CFC650-GY-010", "喷淋管 DN25", Mat.S316, Cfg.C_SUS316, "φ32×2 L1155, 7×1/4\" 喷嘴座 @110",
                "S31603 焊管 φ32×2 L1155; 半管箍 1/4\" ×7; 封板 δ3", "下料-钻孔-焊半管箍-焊封板-水压试验-钝化", "weld");
            pr.Notes = "半管箍 1/4\" Rc 内螺纹, 轴线与管轴线垂直度 ≤1°, 7 只共面|0.6MPa 水压试验 10min 无渗漏|焊后酸洗钝化";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, -700).Circ(0, 0, 32).Ext(1155, 1);
                var s = Sk.YZ(m, 0);
                for (int i = 0; i < 7; i++) s.Circ(-330 + 110 * i, 0, 20);
                s.Ext(26, 1);
                Sk.XY(m, -700).Circ(0, 0, 28).Cut(1152, 1);
                var c = Sk.YZ(m, 0);
                for (int i = 0; i < 7; i++) c.Circ(-330 + 110 * i, 0, 11);
                c.Cut(30, 1);
            });

            // ---------- 喷淋管托架 (δ5 折弯, 装操作侧墙板内侧): 局部 z=0 贴墙板, y=0 为托面; 立边向上(避开槽口) ----------
            pr = P(SPRAYBRK, "CFC650-GY-011", "喷淋管托架", Mat.S304, Cfg.C_SUS, "δ5 折弯 60×50×45, 2-φ9", "S30408 板 δ5", "激光切割-折弯", "sheet");
            pr.Notes = NOTE_PLATE; pr.Dxf = true; pr.DxfNormal = "Z";
            Cat.Make(pr, m =>
            {
                Sk.XY(m, 0).Rect(-30, -5, 30, 45).Ext(5, 1);
                Sk.XZ(m, -5).Rect(-30, 5, 30, 45).Ext(5, 1);
                Sk.XY(m, 0).Circ(-20, 25, 9).Circ(20, 25, 9).CutThru(0);
            });
        }
    }
}
