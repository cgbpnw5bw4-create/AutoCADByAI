using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SolidWorks.Interop.sldworks;

namespace CadKernel
{
    /// <summary>
    /// 设计输入: input/&lt;任务&gt;/design.json
    /// <code>
    /// { "project": "planetary_gear", "title": "行星齿轮减速器 i=5",
    ///   "steps": ["parts","assembly","checks","drawings","export","post"],   // 可省略 = 全部
    ///   "params": { ... 由设计项目解释 ... } }
    /// </code>
    /// </summary>
    public sealed class DesignInput
    {
        public string Task, Project, Title, Dir;
        public List<string> Steps = new List<string>();
        public JsonElement Params;

        public static DesignInput Load(string dir)
        {
            dir = Path.GetFullPath(dir);
            string f = Path.Combine(dir, "design.json");
            if (!File.Exists(f)) throw new FileNotFoundException("输入目录缺少 design.json: " + dir);
            using var doc = JsonDocument.Parse(File.ReadAllText(f), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var r = doc.RootElement;
            var d = new DesignInput { Dir = dir, Task = Path.GetFileName(dir) };
            d.Project = Str(r, "project") ?? throw new Exception("design.json 缺少 project");
            d.Title = Str(r, "title");
            if (r.TryGetProperty("steps", out var st) && st.ValueKind == JsonValueKind.Array)
                foreach (var s in st.EnumerateArray()) d.Steps.Add(s.GetString());
            d.Params = r.TryGetProperty("params", out var p) ? p.Clone() : JsonDocument.Parse("{}").RootElement.Clone();
            return d;
        }

        static string Str(JsonElement e, string k) { return e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null; }

        public bool Has(string key) { return Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(key, out _); }

        public double Num(string key, double def)
        {
            if (Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(key, out var v))
            {
                if (v.ValueKind != JsonValueKind.Number) throw new Exception("参数 " + key + " 应为数值");
                return v.GetDouble();
            }
            return def;
        }

        public int Int(string key, int def)
        {
            double v = Num(key, def);
            if (Math.Abs(v - Math.Round(v)) > 1e-9) throw new Exception("参数 " + key + " 应为整数");
            return (int)Math.Round(v);
        }

        public string Text(string key, string def)
        {
            return Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;
        }

        public List<string> Strings(string key)
        {
            var r = new List<string>();
            if (Params.ValueKind == JsonValueKind.Object && Params.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var s in v.EnumerateArray()) r.Add(s.ToString());
            return r;
        }

        /// <summary>params 中出现但设计项目未声明的键 (多半是拼写错误)</summary>
        public List<string> Unknown(IEnumerable<string> known)
        {
            var set = new HashSet<string>(known);
            return Params.ValueKind != JsonValueKind.Object ? new List<string>()
                : Params.EnumerateObject().Select(p => p.Name).Where(n => !set.Contains(n)).ToList();
        }
    }

    /// <summary>要出图/校核的装配体</summary>
    public sealed class AsmTarget
    {
        public string Path;
        /// <summary>干涉检查时子装配视为整体 (总装只查模块之间)</summary>
        public bool TopLevel;
        public bool Drawing = true;
        /// <summary>主装配体: 出 BOM、截图、STEP</summary>
        public bool Main;
        public bool ForceA0;
    }

    /// <summary>
    /// 设计项目基类。一个设计 = 参数校验 + 零件 + 装配 (+ 项目专属校核与出图配置);
    /// 通用的校核、出图、导出、后处理由 Pipeline 统一完成, 设计项目不重复实现。
    /// </summary>
    public abstract class CadProject
    {
        /// <summary>design.json 中 "project" 的取值</summary>
        public abstract string Id { get; }
        public abstract string Description { get; }
        public virtual string DefaultTitle { get { return Description; } }
        /// <summary>params 中允许出现的键</summary>
        public virtual IEnumerable<string> KnownParams { get { return new string[0]; } }

        public DesignInput Input { get; private set; }
        public string Title { get; private set; }

        public void Bind(DesignInput input)
        {
            Input = input;
            Title = string.IsNullOrWhiteSpace(input.Title) ? DefaultTitle : input.Title;
        }

        /// <summary>纯参数校验 (不连接 SolidWorks), 返回问题列表; 读取参数也在这里完成</summary>
        public virtual List<string> Validate() { return new List<string>(); }

        /// <summary>
        /// 项目级设置: Cat.ProjectName / Version / Date (写入零件属性与标题栏)、Cat.Lazy 等。
        /// 新运行与续跑都在任何步骤之前调用 —— 不要放进 BuildParts (续跑跳过零件步骤时会丢失)。
        /// Cat.Date 默认为本次运行目录的创建日期。
        /// </summary>
        public virtual void Prepare() { }

        /// <summary>零件 (可用 Cat.Lazy 只登记定义, 装配时按需建)</summary>
        public abstract void BuildParts();

        /// <summary>装配体, 返回全部要校核/出图的装配体 (恰好一个 Main)</summary>
        public abstract List<AsmTarget> BuildAssemblies();

        /// <summary>出图前设置 Drw.PartGeneralNote / Drw.AsmNotes 等</summary>
        public virtual void ConfigureDrawings() { }

        /// <summary>项目专属校核 (如机构运动学), 用 Run.Check 登记结论</summary>
        public virtual void ExtraChecks(List<AsmTarget> targets) { }

        /// <summary>BOM 中未建模但须采购的物料</summary>
        public virtual IEnumerable<Checks.BomItem> BomExtras { get { return null; } }

        /// <summary>主装配体内部视图要隐藏的零部件 (null = 不出内部视图)</summary>
        public virtual Func<Component2, bool> InteriorHide { get { return null; } }
    }
}
