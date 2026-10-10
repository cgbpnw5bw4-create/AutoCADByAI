using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using CadKernel;
using CadLibrary;

namespace CadProjects.Cfc650
{
    /// <summary>湿法工位 (原设备 M03/M04/M05/M06 共用此代码; 平台样例只含 M03 碱洗)</summary>
    public static class Wet
    {
        const double WIN = Cfg.WALL_IN, WOUT = Cfg.WALL_OUT, TW = Cfg.TANK_TW;
        const double TZ = Cfg.TANK_W / 2;           // 458
        const double UCF_T = 14;                    // UCF205 法兰厚 (螺栓夹紧)
        const string NOTE_TANK =
            "S31603 板激光切割、折弯拼焊, 全部焊缝连续满焊 (GB/T 985.1), 槽内焊缝磨平 Ra≤3.2|" +
            "盛水试漏 24h 无渗漏|两侧 U 形托座开口同轴度 φ0.5, 托座与轴承座配作|槽口平面度 ≤2|" +
            "焊后整体酸洗钝化 (ASTM A380)|溢流堰顶面水平度 ≤1, 堰顶标高即工作液位";

        static string Pid(WetMod w, string s) { return w.Key + "_" + s; }

        /// <summary>模块零件的图号前缀; M05A/M05B 共用 M05 零件</summary>
        static string Grp(WetMod w) { return w.Key.StartsWith("M05") ? "M05" : w.Key; }

        public static void BuildParts()
        {
            foreach (var w in Lay.Wet)
            {
                Sw.Head(Grp(w) + " " + w.Name + (w.Key == "M05A" ? " (一/二级共用)" : ""));
                Tank(w); Walls(w);
                if (w.Hood) Hood(w);
            }
        }

        // ------------------------------------------------------------------ 槽体
        static void Tank(WetMod w)
        {
            double L = w.L; string g = Grp(w);
            var p = new Part { Id = Pid(w, "TANK"), Dir = w.Dir, Code = "CFC650-" + g + "-001", Name = w.Name.TrimEnd('Ⅰ') + "槽",
                Mat = Mat.S316, Color = Cfg.C_SUS316, DrwType = "weld", Notes = NOTE_TANK, Surface = "酸洗钝化",
                Spec = string.Format("{0:F0}×916×450, 壁 δ5 / 底 δ6, 溢流堰 + DN40 排液", L),
                Blank = string.Format("S31603 2B 板 δ5 / δ6; 扁钢 40×6; 管 φ48.3×3.2"),
                Process = "激光切割-折弯-拼焊-试漏-钝化",
                Remark = w.Heater ? "配 L 型钛加热器 6kW" : w.Ultra ? "槽底放置 6 台投入式振板" : "", Taps = "6:M6-6H 深12" };
            if (w.Key == "M05A") { p.Name = "水洗槽"; p.Remark = "一级/二级水洗通用, 共 2 件"; }
            Cat.Make(p, m =>
            {
                Sk.XZ(m, Cfg.TANK_Y0).Rect(0, -TZ, L, TZ).Ext(Cfg.TANK_TB, 1);
                double y1 = Cfg.TANK_Y0 + Cfg.TANK_TB;
                Sk.XZ(m, y1).Rect(0, -TZ, L, TZ).Rect(TW, -TZ + TW, L - TW, TZ - TW).Ext(Cfg.TANK_TOP - y1, 1);
                var rib = Sk.XZ(m, Cfg.TABLE_Y);
                foreach (double z in w.RibZ) rib.Rect(-45, z - 3, L + 45, z + 3);
                rib.Ext(Cfg.RIB_H, 1);
                // 溢流堰箱 (U 形, 堰顶 = 液位 1180), 开口贴端壁
                double a = w.OverflowUp ? TW : L - TW, sg = w.OverflowUp ? 1 : -1;
                Sk.XZ(m, y1).Poly(a + sg * 60, -200, a, -200, a, -197, a + sg * 57, -197, a + sg * 57, 197, a, 197, a, 200, a + sg * 60, 200)
                    .Ext(Cfg.LIQ_Y - y1, 1);
                // 溢流出口 DN40 (φ48.3×3.2) 下伸至 Y640 (储液箱顶上方)
                double xo = w.OverflowX;
                Sk.XZ(m, 640).Circ(xo, WetMod.OVF_Z, 48.3).Ext(y1 - 640, 1);
                Sk.XZ(m, 640).Circ(xo, WetMod.OVF_Z, 41.9).Cut(y1 - 640 + 1, 1);
                // 排液口 DN40 下伸至 Y760, 接球阀
                double xd = w.DrainX;
                Sk.XZ(m, 760).Circ(xd, WetMod.DRAIN_Z, 48.3).Ext(y1 - 760, 1);
                Sk.XZ(m, 760).Circ(xd, WetMod.DRAIN_Z, 41.9).Cut(y1 - 760 + 1, 1);
                // 浸没辊 U 形托座 (两侧内壁), 托座顶 1000, 开口宽 44.5, 底半圆中心 = 辊心 950
                foreach (double z0 in new[] { TZ - TW - 22, -(TZ - TW) })
                {
                    var blk = Sk.XY(m, z0);
                    foreach (double dx in w.DipX) blk.Rect(dx - 40, 910, dx + 40, 1000);
                    blk.Ext(22, 1);
                    var u = Sk.XY(m, z0);
                    foreach (double dx in w.DipX)
                        u.Line(dx - 22.25, 1001, dx - 22.25, Cfg.DIP_Y).Arc(dx, Cfg.DIP_Y, dx - 22.25, Cfg.DIP_Y, dx + 22.25, Cfg.DIP_Y)
                         .Line(dx + 22.25, Cfg.DIP_Y, dx + 22.25, 1001).Line(dx + 22.25, 1001, dx - 22.25, 1001);
                    u.Cut(22, 1);
                }
                var tp = Sk.XZ(m, 1000);
                foreach (double dx in w.DipX) foreach (double zs in new[] { 442.0, -442 }) { tp.Circ(dx - 31, zs, 6); tp.Circ(dx + 31, zs, 6); }
                tp.Cut(12, -1);
            });
        }

        // ------------------------------------------------------------------ 墙板
        static void Walls(WetMod w)
        {
            double L = w.L; string g = Grp(w);
            foreach (bool op in new[] { true, false })
            {
                var h = new List<double[]>();   // {x, y, d}
                foreach (double x in w.TopX)
                {
                    h.Add(new[] { x, Cfg.ROLL_Y, 45 });
                    foreach (double dx in new[] { -35.0, 35 }) foreach (double dy in new[] { -35.0, 35 }) h.Add(new[] { x + dx, Cfg.ROLL_Y + dy, 12 });
                }
                foreach (double bx in w.BracketX) foreach (double dx in new[] { -30.0, 30 }) h.Add(new[] { bx + dx, 860, 12 });   // M12 螺纹孔
                if (w.Hood) foreach (double hx in w.HoodBoltX) h.Add(new[] { hx, 1395, 9 });
                if (op)
                {
                    foreach (double sx in w.SprayX) foreach (double dx in new[] { -20.0, 20 }) h.Add(new[] { sx + dx, 1299, 9 });
                    if (w.Key == "M03") { h.Add(new[] { -9.5, 1460, 9 }); h.Add(new[] { -9.5, 1482, 9 }); }   // 放卷纠偏传感器支架
                }
                else
                {
                    foreach (double sx in w.SprayX) h.Add(new[] { sx, WetMod.SPRAY_Y, 34 });
                    for (int i = 0; i < 4; i++) h.Add(new[] { L / 2 + WetMod.PILLAR[i, 0], Cfg.ROLL_Y + WetMod.PILLAR[i, 1], 9 });
                }
                var p = new Part { Id = Pid(w, op ? "WOP" : "WDR"), Dir = w.Dir,
                    Code = "CFC650-" + g + "-" + (op ? "002" : "003"), Name = (w.Key == "M05A" ? "水洗" : w.Name) + "墙板(" + (op ? "操作侧" : "传动侧") + ")",
                    Mat = Mat.S304, Color = Cfg.C_SUS, DrwType = "plate", Dxf = true, DxfNormal = "Z", Taps = "12:M12-6H 通",
                    Spec = string.Format("δ12 {0:F0}×950", L + 50), Blank = string.Format("S30408 2B δ12 {0:F0}×960", L + 60),
                    Process = "激光切割-成对叠合镗轴承孔-攻丝-拉丝-钝化", Surface = "拉丝 No.4 + 酸洗钝化",
                    Notes = Common.NOTE_PLATE + "|操作侧/传动侧墙板成对叠合加工 φ45 轴承让位孔及 M12 螺孔, 孔位公差 ±0.1|" +
                            "装配后辊系平行度 ≤0.05/1000 (激光对中仪检测)|平面度 ≤0.5/1000" };
                if (w.Key == "M05A") p.Remark = "一级/二级水洗通用, 共 2 件";
                Cat.Make(p, m =>
                {
                    Sk.XY(m, 0).Poly(-25, Cfg.WALL_Y0, L + 25, Cfg.WALL_Y0, L + 25, Cfg.WALL_Y1 - 20, L + 5, Cfg.WALL_Y1,
                                     -5, Cfg.WALL_Y1, -25, Cfg.WALL_Y1 - 20).Ext(Cfg.WALL_T, 0);
                    var s = Sk.XY(m, 0);
                    foreach (var c in h) s.Circ(c[0], c[1], c[2]);
                    s.CutThru(0);
                });
            }
        }

        // ------------------------------------------------------------------ 排风罩 + 托条
        static void Hood(WetMod w)
        {
            double L = w.L, HL = L - 10; string g = Grp(w);
            Cat.Make(new Part { Id = Pid(w, "HOOD"), Dir = w.Dir, Code = "CFC650-" + g + "-004", Name = w.Name + "排风罩",
                Mat = Mat.PP, Color = Cfg.C_PP, DrwType = "weld", Surface = "—",
                Spec = string.Format("PP δ6 {0:F0}×930×340, 排风口 DN150", HL), Blank = "PP 板 δ6; PP 管 φ160×8",
                Process = "PP 板裁切-热风焊接", Remark = "搁置于墙板托条上, 可整体吊起",
                Notes = "PP 板热风焊接, 焊条同材质, 焊缝饱满无虚焊|排风口接 DN150 PP 风管至酸雾净化塔|两端设不锈钢拉手 2 只" },
                m =>
                {
                    Sk.XZ(m, 1744).Rect(0, -465, HL, 465).Ext(6, 1);
                    Sk.XZ(m, 1410).Rect(0, -465, HL, 465).Rect(6, -459, HL - 6, 459).Ext(334, 1);
                    Sk.XZ(m, 1750).Circ(HL / 2, 0, 160).Ext(100, 1);
                    Sk.XZ(m, 1740).Circ(HL / 2, 0, 144).Cut(120, 1);
                });
            Cat.Make(new Part { Id = Pid(w, "HANG"), Dir = w.Dir, Code = "CFC650-" + g + "-005", Name = "排风罩托条",
                Mat = Mat.S304, Color = Cfg.C_SUS, DrwType = "weld", Surface = "酸洗钝化",
                Spec = string.Format("L30×30×3 L={0:F0}, 3-φ9", HL), Blank = "不锈钢角钢 L30×30×3", Process = "锯切-钻孔",
                Notes = "锯切下料, 去毛刺|孔位与墙板配作|酸洗钝化" },
                m =>
                {
                    Sk.YZ(m, 0).Poly(0, 0, 3, 0, 3, 27, 30, 27, 30, 30, 0, 30).Ext(HL, 1);
                    var s = Sk.XY(m, 0);
                    foreach (double hx in w.HoodBoltX) s.Circ(hx - 5, 15, 9);
                    s.CutThru(0);
                });
        }

        // ================================================================== 装配
        public static string BuildAsm(string grp)
        {
            var mods = Lay.Wet.Where(w => Grp(w) == grp).ToList();
            var w0 = mods[0];
            string file = Path.Combine(Run.Models, w0.Dir, "CFC650-" + grp + "-000_" +
                (grp == "M05" ? "水洗" : w0.Name) + "模块装配.SLDASM");
            var a = new AsmBuild(file);
            foreach (var w in mods) Place(a, w);
            return a.Build();
        }

        static void Place(AsmBuild a, WetMod w)
        {
            double x0 = w.X0, L = w.L, Y = Cfg.ROLL_Y;
            a.Add(Cat.P(Pid(w, "TANK")), x0, 0, 0);
            a.Add(Cat.P(Pid(w, "WOP")), x0, 0, Cfg.WALL_Z);
            a.Add(Cat.P(Pid(w, "WDR")), x0, 0, -Cfg.WALL_Z);

            // 墙板角座: 立边 M12 拧入墙板螺纹孔, 底边 M10 穿接液盘拧入机架拉铆螺母
            foreach (double bx in w.BracketX)
            {
                a.Add(Cat.P(Common.WBRK), Xf.T(x0 + bx, Cfg.TABLE_Y, WOUT));
                a.Add(Cat.P(Common.WBRK), Xf.FlipY(x0 + bx, Cfg.TABLE_Y, -WOUT));
                foreach (double dx in new[] { -30.0, 30 })
                {
                    a.BoltTap(12, 10, 19, "-Z", x0 + bx + dx, 860, WOUT + 10);
                    a.BoltTap(12, 10, 19, "+Z", x0 + bx + dx, 860, -WOUT - 10);
                    foreach (double s in new[] { 1.0, -1 })
                        a.BoltTap(10, 10 + Cfg.TRAY_T, 50, "-Y", x0 + bx + dx, Cfg.TABLE_Y + 10, s * Cfg.BEAM_Z);
                }
            }

            // 上导辊 + UCF205 (中间辊为主动辊)
            for (int i = 0; i < 3; i++)
            {
                double x = x0 + w.TopX[i];
                a.Add(Cat.P(i == 1 ? Common.GR80D : Common.GR80), x, Y, 0);
                UcfPair(a, x, Y);
            }

            // 主动辊传动 (传动侧 -Z): 支柱 ×4 -> 电机板 -> 伺服减速电机, 梅花联轴器
            double xm = x0 + L / 2, zp = -WOUT - WetMod.PILLAR_L;   // 电机板内侧面 -621
            for (int i = 0; i < 4; i++)
            {
                double px = xm + WetMod.PILLAR[i, 0], py = Y + WetMod.PILLAR[i, 1];
                a.Add(Cat.P(Common.PILLAR8), Xf.Dir("-Z", px, py, -WOUT));
                a.BoltTap(8, Cfg.WALL_T, 18, "-Z", px, py, -WIN);
                a.BoltTap(8, WetMod.MPLATE_T, 18, "+Z", px, py, zp - WetMod.MPLATE_T);
            }
            a.Add(Cat.P(Common.MPLATE), Xf.Dir("-Z", xm, Y, zp));
            a.Add(Cat.P(Buy.SERVO), Xf.Dir("-Z", xm, Y, zp - WetMod.MPLATE_T));
            for (int i = 0; i < 4; i++)
            {
                double ang = Math.PI / 4 + i * Math.PI / 2;
                a.Add(Std.Socket(5, 16), Xf.Dir("-Z", xm + 35 * Math.Cos(ang), Y + 35 * Math.Sin(ang), zp));
            }
            a.Add(Cat.P(Buy.CPL_SMALL), Xf.Dir("-Z", xm, Y, -555));

            // 浸没辊 + UHMW 轴承座 + 塑料轴承 + 压盖
            foreach (double dx in w.DipX)
            {
                double x = x0 + dx, zi = TZ - TW - 22;   // 431
                a.Add(Cat.P(Common.GR60), x, Cfg.DIP_Y, 0);
                a.Add(Cat.P(Common.DIPHSG), Xf.T(x, Cfg.DIP_Y, zi));
                a.Add(Cat.P(Common.DIPHSG), Xf.FlipY(x, Cfg.DIP_Y, -zi));
                a.Add(Cat.P(Buy.PBRG), x, Cfg.DIP_Y, zi + 5);
                a.Add(Cat.P(Buy.PBRG), x, Cfg.DIP_Y, -zi - 17);
                a.Add(Cat.P(Common.DIPCAP), x, 1000, zi);
                a.Add(Cat.P(Common.DIPCAP), x, 1000, -zi - 22);
                foreach (double zs in new[] { 442.0, -442 })
                    foreach (double d in new[] { -31.0, 31 })
                        a.BoltTap(6, 6, 12, "-Y", x + d, 1006, zs);
            }

            // 喷淋管 + 喷嘴 + 操作侧托架
            foreach (double sx in w.SprayX)
            {
                double x = x0 + sx;
                a.Add(Cat.P(Common.SPRAY), x, WetMod.SPRAY_Y, 0);
                for (int i = 0; i < 7; i++) a.Add(Cat.P(Buy.NOZZLE), x + 26, WetMod.SPRAY_Y, -330 + 110 * i);
                a.Add(Cat.P(Common.SPRAYBRK), Xf.FlipY(x, WetMod.SPRAY_Y - 16, WIN));
                foreach (double d in new[] { -20.0, 20 }) a.BoltNut(8, 5 + Cfg.WALL_T, "+Z", x + d, 1299, WIN - 5);
            }

            // 排液球阀
            a.Add(Cat.P(Buy.VALVE40), Xf.FlipX(x0 + w.DrainX, 760, WetMod.DRAIN_Z));

            if (w.Hood)
            {
                a.Add(Cat.P(Pid(w, "HOOD")), x0 + 5, 0, 0);
                a.Add(Cat.P(Pid(w, "HANG")), Xf.FlipY(x0 + L - 5, 1380, WIN));
                a.Add(Cat.P(Pid(w, "HANG")), Xf.T(x0 + 5, 1380, -WIN));
                foreach (double hx in w.HoodBoltX)
                {
                    a.BoltNut(8, Cfg.WALL_T + 3, "-Z", x0 + hx, 1395, WOUT);
                    a.BoltNut(8, Cfg.WALL_T + 3, "+Z", x0 + hx, 1395, -WOUT);
                }
            }
            if (w.Heater) a.Add(Cat.P(Buy.TIHEATER), x0 + L, Cfg.TANK_TOP, -300);
            if (w.Ultra) throw new NotSupportedException("超声工位 (M06) 未纳入平台样例");
        }

        /// <summary>一对 UCF205 (两侧墙板外侧) + 8 组 M10 螺栓 (头在轴承法兰侧, 螺母在墙板内侧)</summary>
        public static void UcfPair(AsmBuild a, double x, double y)
        {
            a.Add(Cat.P(Buy.UCF205), Xf.T(x, y, WOUT));
            a.Add(Cat.P(Buy.UCF205), Xf.FlipY(x, y, -WOUT));
            foreach (double dx in new[] { -35.0, 35 })
                foreach (double dy in new[] { -35.0, 35 })
                {
                    a.BoltNut(10, UCF_T + Cfg.WALL_T, "-Z", x + dx, y + dy, WOUT + UCF_T);
                    a.BoltNut(10, UCF_T + Cfg.WALL_T, "+Z", x + dx, y + dy, -WOUT - UCF_T);
                }
        }
    }
}
