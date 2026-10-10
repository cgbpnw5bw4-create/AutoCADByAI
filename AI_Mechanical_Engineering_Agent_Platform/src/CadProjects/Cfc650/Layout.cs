using System;
using System.Collections.Generic;
using CadKernel;

namespace CadProjects.Cfc650
{
    /// <summary>
    /// 湿法工位 (碱洗/水洗/酸洗/超声) 布局 —— 零件开孔与装配定位共用本数据, 保证孔位与紧固件天然对齐。
    /// 局部 X = 全局 X - X0。
    /// </summary>
    public class WetMod
    {
        public string Key, Name, Dir; public double X0, L;
        public bool Hood, Heater, Ultra, OverflowUp, Reservoir;

        public double[] TopX { get { return new[] { 60, L / 2, L - 60 }; } }          // 上导辊 (中间辊为主动辊)
        public double[] DipX { get { return new[] { 0.25 * L, 0.75 * L }; } }         // 浸没辊
        public double[] BracketX { get { return new[] { 120, L - 120 }; } }           // 墙板角座
        public double[] HoodBoltX { get { return new[] { 100, L / 2, L - 100 }; } }   // 排风罩托条螺栓
        public const double SPRAY_Y = 1290;
        /// <summary>喷淋管 X: 位于两段上升箔面(浸没辊->上导辊)的上游侧 75mm (喷嘴端距箔面 ≥15), 高度 1290(槽口以上)</summary>
        public double[] SprayX
        {
            get
            {
                double t = (SPRAY_Y - Cfg.DIP_Y) / (Cfg.ROLL_Y - Cfg.DIP_Y);
                double x1 = 0.25 * L + 30 + t * ((L / 2 - 40) - (0.25 * L + 30)) - 75;
                double x2 = 0.75 * L + 30 + t * ((L - 100) - (0.75 * L + 30)) - 75;
                return new[] { Math.Round(x1), Math.Round(x2) };
            }
        }
        public double OverflowX { get { return OverflowUp ? 35 : L - 35; } }
        public const double OVF_Z = 130, DRAIN_Z = -380;
        public double DrainX { get { return OverflowUp ? L - 120 : 120; } }
        public double[] RibZ { get { return new[] { -420.0, 0, 420 }; } }

        // 主动辊传动: 支柱 4 根 (±65, ±50), 电机板距墙板外侧面 140
        public static readonly double[,] PILLAR = { { -65, -50 }, { 65, -50 }, { -65, 50 }, { 65, 50 } };
        public const double PILLAR_L = 140, MPLATE_T = 12;
    }

    public static class Lay
    {
        /// <summary>样例只含 M03 碱洗 (原设备还有 M04 酸洗 / M05 水洗 / M06 超声, 结构相同, 未纳入平台)</summary>
        public static readonly List<WetMod> Wet = new List<WetMod>
        {
            new WetMod { Key = "M03", Name = "碱洗", Dir = @"M03_碱洗模块", X0 = Mod.M03_X, L = Mod.M03_L, Hood = true, Heater = true, Reservoir = true },
        };

        public static WetMod W(string key) { foreach (var w in Wet) if (w.Key == key) return w; throw new Exception(key); }
    }
}
