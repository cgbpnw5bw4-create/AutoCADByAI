using System.Collections.Generic;
using CadKernel;

namespace CadProjects.Cfc650
{
    /// <summary>
    /// 铜箔清洗机 CFC-650 V2 全局设计参数
    /// 坐标: X=走带方向(0 在放卷端)  Y=竖直向上(0=地面)  Z=幅宽方向(0=箔材中心线, +Z=操作侧, -Z=传动侧)
    /// </summary>
    public static class Cfg
    {
        public const string VER = "V2.0";
        public const string DATE = "2026-09-30";

        // ---- 工艺 ----
        public const double WEB_W = 650;          // 最大幅宽
        public const double CORE_ID = 152.4;      // 6" 卷芯 (V1 为 3", 承载不足)
        public const double CORE_OD = 172;        // 卷芯外径 (ABS/纸芯 壁厚约 10)
        public const double COIL_D = 600;         // 最大卷径

        // ---- 标高 ----
        public const double FOOT_H = 80;          // 地脚 (调节 ±20)
        public const double PAD_T = 20;           // 地脚垫板厚 (M20 螺纹孔)
        public const double TUBE = 80, TUBE_T = 3;    // 机架方管 80×80×3 (GB/T 6728)
        public const double BOT_Y = 100;          // 底梁下沿
        public const double TOP_Y = 718;          // 顶梁下沿
        public const double COL_L = TOP_Y - BOT_Y - TUBE;  // 立柱长 538
        public const double TRAY_T = 2;           // 接液盘厚
        public const double TABLE_Y = 800;        // 接液盘上表面 = 工位安装基准面
        public const double RIB_H = 40;           // 槽底加强筋高
        public const double TANK_Y0 = TABLE_Y + RIB_H;    // 槽外底 840
        public const double TANK_TOP = 1250;      // 槽口
        public const double TANK_TW = 5, TANK_TB = 6;     // 槽壁/槽底厚 (V1 为 8/10, 超设计)
        public const double LIQ_Y = 1180;         // 液位 (= 溢流堰顶)
        public const double ROLL_Y = 1320;        // 上导辊中心
        public const double PASS_Y = 1360;        // 走带线 = 上导辊顶切线 (V1 误取 1320 辊心)
        public const double DIP_Y = 950;          // 浸没辊中心
        public const double WALL_Y0 = 800, WALL_Y1 = 1750;   // 工艺段墙板
        public const double STAND_Y0 = 880;       // 放/收卷纠偏滑座上表面 (墙板底)
        public const double COIL_Y = 1750;        // 放/收卷轴心

        // ---- 幅宽方向 ----
        public const double ROLL_FACE = 750;
        public const double WALL_Z = 475, WALL_T = 12;       // 墙板中心 ±475
        public const double WALL_IN = WALL_Z - WALL_T / 2;   // 469 内侧面
        public const double WALL_OUT = WALL_Z + WALL_T / 2;  // 481 外侧面
        public const double TANK_W = 916;         // 槽外宽 (±458)
        public const double BEAM_Z = 600;         // 机架纵梁中心 ±600 (V1 ±660)
        public const double FRAME_W = 2 * BEAM_Z + TUBE;     // 1280
        public const double TRAY_HALF = 680;      // 接液盘半宽 (含翻边)

        // ---- 机长 / 分段 ----
        public const double FRAME_L = 8600;
        public const double SPLICE_X = 4800;      // 机架分段点 (A 段 4.8m + B 段 3.8m, 法兰螺栓连接)
        public const double FLG_T = 12;           // 分段法兰板厚

        // 立柱 X (两段各自成闭合框)
        public static readonly double[] COL_A = { 40, 825, 1900, 2800, 3900, 4735 };
        public static readonly double[] COL_B = { 4880, 6150, 7350, 8560 };
        // 顶层横梁 X (承托槽体两端 / 导轨 / 烘箱支腿), 含立柱位置
        public static readonly double[] TOPX_A = { 40, 250, 650, 825, 1350, 1900, 2350, 2800, 3350, 3900, 4350, 4735 };
        public static readonly double[] TOPX_B = { 4880, 5250, 5650, 6150, 6810, 7350, 7890, 8100, 8400, 8560 };

        // ---- 颜色 ----
        public static readonly double[] C_FRAME = { 0.16, 0.30, 0.48 };
        public static readonly double[] C_SUS = { 0.76, 0.78, 0.80 };
        public static readonly double[] C_SUS316 = { 0.68, 0.72, 0.76 };
        public static readonly double[] C_STEEL = { 0.45, 0.47, 0.50 };
        public static readonly double[] C_ALU = { 0.82, 0.84, 0.87 };
        public static readonly double[] C_COPPER = { 0.85, 0.52, 0.25 };
        public static readonly double[] C_RUBBER = { 0.12, 0.12, 0.13 };
        public static readonly double[] C_PP = { 0.93, 0.93, 0.88 };
        public static readonly double[] C_PE = { 0.96, 0.96, 0.96 };
        public static readonly double[] C_CAB = { 0.86, 0.87, 0.89 };
        public static readonly double[] C_OVEN = { 0.88, 0.86, 0.76 };
        public static readonly double[] C_MOTOR = { 0.25, 0.42, 0.30 };
        public static readonly double[] C_SERVO = { 0.18, 0.18, 0.20 };
        public static readonly double[] C_ORANGE = { 0.90, 0.45, 0.10 };
        public static readonly double[] C_BLUE = { 0.15, 0.35, 0.70 };
        public static readonly double[] C_YELLOW = { 0.85, 0.70, 0.10 };
        public static readonly double[] C_ZINC = { 0.70, 0.72, 0.74 };
        public static readonly double[] C_BLACK = { 0.10, 0.10, 0.10 };
        public static readonly double[] C_RED = { 0.70, 0.12, 0.10 };

        /// <summary>零件登记表 (即内核登记表)</summary>
        public static Dictionary<string, string> Reg { get { return Cat.Reg; } }
    }

    /// <summary>各工位沿走带方向的起点与长度 (mm)</summary>
    public static class Mod
    {
        public const double M02_X = 150, M02_L = 650;    // 放卷
        public const double M03_X = 850, M03_L = 1000;   // 碱洗
        public const double M05A_X = 1950, M05_L = 800;  // 水洗Ⅰ
        public const double M04_X = 2850, M04_L = 1000;  // 酸洗
        public const double M05B_X = 3950;               // 水洗Ⅱ
        public const double M06_X = 4850, M06_L = 1200;  // 超声
        public const double M07_X = 6250, M07_L = 400;   // 风刀
        public const double M08_X = 6750, M08_L = 1200;  // 烘干
        public const double M09_X = 8000, M09_L = 500;   // 收卷
        public const double OVEN_Y = 1000;               // 烘箱底
        public const double UNW_X = 450;                 // 放卷轴心 X
        public const double REW_X = 8300;                // 收卷轴心 X
    }
}
