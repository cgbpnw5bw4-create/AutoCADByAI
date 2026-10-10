using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using CadKernel;
using CadLibrary;

namespace CadProjects.Cfc650
{
    /// <summary>
    /// 回归样例: CFC-650 铜箔清洗机的 M03 碱洗模块 (约 330 件含紧固件, 中型固定布局装配)。
    /// 用途是证明内核与零件库在"变换矩阵批量装配 + GB 紧固件穿孔 + 干涉兼作孔位校核 + 零件/装配出图"上仍然可用,
    /// 不是平台的产品方向。原设备完整源码与交付物在 D:\07_WorkSpace\Claude\20260910 (V2)。
    /// 坐标沿用原设备: X = 走带方向, Y = 竖直向上 (0 = 地面), Z = 幅宽方向 (+Z 操作侧)。
    /// </summary>
    public sealed class Cfc650Module : CadProject
    {
        public override string Id { get { return "cfc650_module"; } }
        public override string Description { get { return "CFC-650 铜箔清洗机 碱洗模块 (中型装配回归样例)"; } }
        public override IEnumerable<string> KnownParams { get { return new[] { "module" }; } }

        string module;

        public override List<string> Validate()
        {
            var r = new List<string>();
            module = Input.Text("module", "M03");
            if (module != "M03") r.Add("平台样例只含 M03 碱洗模块 (module=\"M03\"), 收到: " + module);
            return r;
        }

        public override void Prepare()
        {
            Cat.ProjectName = "铜箔清洗机 CFC-650";
            Cat.Version = Cfg.VER;
            Cat.Date = Cfg.DATE;
            // 只登记定义, 装配时按需建: 外购件样本库很大, 本模块只用到其中 7 种
            Cat.Lazy = true;
        }

        public override void BuildParts()
        {
            Buy.BuildAll();
            Common.BuildAll();
            Wet.BuildParts();
        }

        public override List<AsmTarget> BuildAssemblies()
        {
            string p = Wet.BuildAsm(module);
            Cat.Reg["ASM_" + module] = p;
            return new List<AsmTarget> { new AsmTarget { Path = p, Main = true } };
        }

        public override void ConfigureDrawings()
        {
            Drw.PartGeneralNote = "未注明处按 CFC650 通用技术条件执行。";
            Drw.AsmNotes = "1. 装配前所有零件清洗干净, 不锈钢件不得与碳钢工具直接接触。\n" +
                           "2. 辊系平行度 ≤0.05/1000, 装配后用激光对中仪检测。\n" +
                           "3. 螺栓按拧紧力矩表紧固, 湿区紧固件为 A2-70 不锈钢。\n" +
                           "4. 未注明处按 CFC650 通用技术条件执行。";
        }

        /// <summary>内部视图: 隐藏操作侧墙板与排风罩</summary>
        public override Func<Component2, bool> InteriorHide
        {
            get
            {
                return c =>
                {
                    string f = Path.GetFileName(c.GetPathName() ?? "");
                    return f.Contains("操作侧") || f.Contains("排风罩");
                };
            }
        }
    }
}
