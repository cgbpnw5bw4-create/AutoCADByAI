using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SolidWorks.Interop.sldworks;
using CadKernel;

namespace CadProjects.Imported
{
    /// <summary>
    /// 由 STEP 导入的设计 (cad import 生成 input/&lt;任务&gt;/import.json 与 geometry/*.x_t)。第一阶段:
    /// <list type="bullet">
    /// <item>装配层级与每个实例的位置来自原 STEP, 可在 import.json 中修改 (移动、替换、增删实例), 改后 cad run 即重建;</item>
    /// <item>直管识别为参数化特征 (外径、内径、长度可改); 其余零件载入原几何 —— 外购件不需要重建, 自制件留待第二阶段逐类重建;</item>
    /// <item>BOM 字段 (代号/名称/规格/品牌) 由原零件名拆出, 材料按名称推断, 推断不出标"未定" (质量按钢估算)。</item>
    /// </list>
    /// 专属校核: 每个零件的体积与包围盒、每个零件实例的位置, 都与导入时记录的原 STEP 数据比对 ——
    /// 证明"重建出来的就是原来那台设备"; 有意修改了装配或零件后, 这两项会如实报出差异。
    /// </summary>
    public sealed class ImportedAssembly : CadProject
    {
        public override string Id { get { return "imported"; } }
        public override string Description { get { return "由 STEP 导入的装配 (cad import 生成 import.json)"; } }
        public override string DefaultTitle { get { return "导入装配"; } }

        ImportModel model;
        readonly Dictionary<string, string> asmFiles = new Dictionary<string, string>();

        public override List<string> Validate()
        {
            try { model = ImportModel.Load(Input.Dir); }
            catch (Exception e) { return new List<string> { e.Message }; }
            return model.Validate(Input.Dir);
        }

        public override void Prepare()
        {
            Cat.ProjectName = Title;
            Cat.Version = "V1.0";
        }

        // ================================================================== 零件
        public override void BuildParts()
        {
            foreach (var p in model.Parts)
            {
                var part = ToPart(p);
                var g = p.Model;
                if (g.Type == "tube") Cat.Make(part, m => Tube(m, g));
                else if (g.Type == "bodies")
                {
                    // 按实体重建: 识别出的实体用截面拉伸、钣金特征建出; 未全部重建时先载入原几何, 删去被重建的实体, 其余保留
                    Action<ModelDoc2> build = m =>
                    {
                        foreach (var pr in g.Prisms ?? new List<PrismDef>()) Prism.Build(m, pr);
                        foreach (var sh in g.Sheets ?? new List<SheetDef>()) SheetMetal.Build(m, sh);
                    };
                    var rebuilt = g.Rebuilt();
                    if (rebuilt.Count < p.Bodies)
                        Cat.Import(part, Path.Combine(Input.Dir, g.File), m => { Prism.RemoveRebuilt(m, rebuilt); build(m); });
                    else Cat.Make(part, build);
                }
                else Cat.Import(part, Path.Combine(Input.Dir, g.File));
            }
        }

        static Part ToPart(ImportPart p)
        {
            bool make = p.Kind == "自制件";
            return new Part
            {
                Id = p.Id, Dir = make ? "自制件" : "外购件", Code = p.Code, Name = p.Name, Spec = p.Spec ?? "", Brand = p.Brand ?? "",
                Remark = p.Remark ?? "", Kind = make ? Kind.Make : Kind.Buy, Mat = Mat.ByKey(p.Material),
                Color = make ? Col.Sus : Col.Grey, Bodies = p.Bodies,
                DrwType = "",          // 第一阶段: 导入件没有特征, 不出零件图 (自制件重建后再出)
            };
        }

        /// <summary>直管: 轴线平行于零件坐标轴, 端面在 start 与 start+length (零件坐标, 与原几何位置一致)</summary>
        static void Tube(ModelDoc2 m, ImportGeometry g)
        {
            Sk s = g.Axis == "Z" ? Sk.XY(m, g.Start) : g.Axis == "Y" ? Sk.XZ(m, g.Start) : Sk.YZ(m, g.Start);
            s.Circ(g.Center[0], g.Center[1], g.Dout);
            if (g.Din > 0) s.Circ(g.Center[0], g.Center[1], g.Din);
            s.Ext(g.Length, 1).Name = "管";
        }

        // ================================================================== 装配 (由内向外, 同一子装配只建一次)
        public override List<AsmTarget> BuildAssemblies()
        {
            asmFiles.Clear();
            asmNames = AsmFiles(model);
            var root = model.Asm(model.Root);
            bool modular = root.Children.Any(c => model.Asm(c.Ref) != null);
            // 由模块组成的总装: 明细表只列模块 (各模块的零件在模块装配图里), 否则行数过多放不下 (微波实验炉 58 种零件压出图框)
            var targets = new List<AsmTarget> { new AsmTarget { Path = BuildAsm(model.Root), Main = true, TopLevelBom = modular } };
            foreach (string id in root.Children.Select(c => c.Ref).Distinct())
                if (model.Asm(id) != null) targets.Add(new AsmTarget { Path = asmFiles[id] });    // 一级模块: 校核 + 装配图
            return targets;
        }

        /// <summary>
        /// 各装配体文件 (相对 models/): 根装配 = 名称, 其余在 装配/ 下, 文件名 = 图号_名称 (装配图从文件名取代号与名称写入标题栏):
        /// 以本设备图号开头的拆成图号与其后文字, 否则 = 编号_名称; 与先命名的重名时退为 编号_名称。名称过长会压出标题栏
        /// (实测 "RTWBL340A1000微波炉磁控管电源模块" 整串写进名称栏)。按建模顺序命名 (子装配先于父装配, 重名时先到先得);
        /// 建模与运行前估算路径长度 (ModelFiles) 共用
        /// </summary>
        static Dictionary<string, string> AsmFiles(ImportModel m)
        {
            string prefix = null;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var r = new Dictionary<string, string>();
            void Name(string id)
            {
                if (r.ContainsKey(id)) return;
                var a = m.Asm(id);
                foreach (var c in a.Children) if (m.Part(c.Ref) == null) Name(c.Ref);
                if (id == m.Root) { r[id] = Run.Safe(a.Name) + ".SLDASM"; return; }
                if (prefix == null) prefix = ImportNames.ProjectPrefix(m.Parts.Select(p => p.OriginalName)) ?? "";
                string kind, code, nm, spec, brand;
                ImportNames.Classify(a.Name, prefix == "" ? null : prefix, out kind, out code, out nm, out spec, out brand);
                string f = Run.Safe(kind == "自制件" ? (nm != code ? code + "_" + nm : code) : a.Id + "_" + a.Name);
                r[id] = Path.Combine("装配", (used.Add(f) ? f : Run.Safe(a.Id + "_" + a.Name)) + ".SLDASM");
            }
            Name(m.Root);
            return r;
        }

        Dictionary<string, string> asmNames;

        /// <summary>零件与装配体文件 (相对 models/), 与建模时的命名相同</summary>
        public override IEnumerable<string> ModelFiles()
        {
            var m = model ?? ImportModel.Load(Input.Dir);
            return m.Parts.Select(p => Cat.RelFileOf(ToPart(p))).Concat(AsmFiles(m).Values).ToList();
        }

        string BuildAsm(string id)
        {
            string done;
            if (asmFiles.TryGetValue(id, out done)) return done;
            var a = model.Asm(id);
            var paths = a.Children.Select(c => model.Part(c.Ref) != null ? Cat.P(c.Ref) : BuildAsm(c.Ref)).ToList();
            string file = Path.Combine(Run.Models, asmNames[id]);
            var b = new AsmBuild(file);
            for (int i = 0; i < a.Children.Count; i++) b.Add(paths[i], Xf.FromRow(a.Children[i].Xf));
            b.Build();
            Cat.Reg["ASM_" + id] = file;
            return asmFiles[id] = file;
        }

        // ================================================================== 与原 STEP 核对
        public override void ExtraChecks(List<AsmTarget> targets)
        {
            Sw.Head("与原 STEP 核对");
            var md = new StringBuilder();
            md.AppendLine("# 与原 STEP 核对");
            md.AppendLine();
            md.AppendLine("> 原始数据: `" + Path.GetFileName(model.Source?.File ?? "") + "` (" + (model.Source?.ImportedAt ?? "") + " 导入时记录)。");
            md.AppendLine("> 零件: 体积相差 ≤ 0.1%, 包围盒各边相差 ≤ 0.1 mm; 实例: 位置相差 ≤ 0.01 mm, 轴方向分量相差 ≤ 1e-6。");
            md.AppendLine();

            // 1. 零件几何
            var bad = new List<string>();
            double worstV = 0, worstB = 0;
            foreach (var p in model.Parts)
            {
                string path;
                if (!Cat.Reg.TryGetValue(p.Id, out path)) { bad.Add(p.Id + " 未建"); continue; }
                Sw.CloseAll();
                ModelDoc2 m = Sw.Open(path);
                double v = Sw.Volume(m); double[] bx = Sw.PartBox(m);
                Sw.Close(m);
                double dv = p.VolumeMm3 > 0 ? Math.Abs(v - p.VolumeMm3) / p.VolumeMm3 : 1;
                double db = Enumerable.Range(0, 6).Max(i => Math.Abs(bx[i] - p.BoxMm[i]));
                worstV = Math.Max(worstV, dv); worstB = Math.Max(worstB, db);
                if (dv > 0.001 || db > 0.1) bad.Add(string.Format("{0} {1}: 体积差 {2:P3}, 包围盒差 {3:F3} mm", p.Id, p.Name, dv, db));
            }
            // 1b. 按实体重建的零件: 重建的实体与原几何逐实体做差集 (形状相同, 不只是体积相同), 保留的原实体核对体积、包围盒、面数
            int rebuiltParts = 0, rebuiltBodies = 0; double worstXor = 0;
            foreach (var p in model.Parts.Where(x => x.Model.Type == "bodies"))
            {
                string path;
                if (!Cat.Reg.TryGetValue(p.Id, out path)) continue;
                Sw.CloseAll();
                ModelDoc2 built = Sw.Open(path);
                ModelDoc2 orig = Sw.LoadGeometry(Path.Combine(Input.Dir, p.Model.File));
                var cmp = Prism.Compare(orig, built, p.Model.Rebuilt());
                Sw.App.CloseDoc(orig.GetTitle());
                Sw.Close(built);
                rebuiltParts++; rebuiltBodies += p.Model.Rebuilt().Count; worstXor = Math.Max(worstXor, cmp.MaxXorMm3);
                Sw.Log(string.Format("{0} {1}: 实体 {2}/{3} 与原几何一致 (重建 {4} 个, 最大差集 {5:F6} mm³; 保留原几何 {6} 个)",
                    p.Id, p.Name, cmp.Matched, cmp.Original, cmp.Rebuilt, cmp.MaxXorMm3, cmp.Kept));
                foreach (string s in cmp.Problems) bad.Add(p.Id + " " + p.Name + ": " + s);
            }
            md.AppendLine("## 零件几何 —— " + model.Parts.Count + " 种, 不符 " + bad.Count);
            md.AppendLine();
            if (rebuiltParts > 0)
                md.AppendLine(string.Format("> 按实体重建 {0} 种零件: 特征重建的 {1} 个实体与原几何逐实体做差集, 最大 {2:F6} mm³ (允许 ≤ 原实体体积 × 1e-6); 保留原几何的实体核对体积、包围盒与面数。", rebuiltParts, rebuiltBodies, worstXor));
            md.AppendLine();
            foreach (string s in bad) md.AppendLine("- " + s);
            md.AppendLine();
            Run.Check("零件几何与原 STEP 一致", bad.Count == 0,
                string.Format("{0} 种 (直管参数化 {1}, 按实体重建 {2} 种 / 特征实体 {3}), 最大体积差 {4:P4}, 最大包围盒差 {5:F4} mm{6}{7}", model.Parts.Count,
                    model.Parts.Count(p => p.Model.Type == "tube"), rebuiltParts, rebuiltBodies, worstV, worstB,
                    rebuiltParts > 0 ? string.Format(", 逐实体差集最大 {0:F6} mm³", worstXor) : "",
                    bad.Count > 0 ? "; 不符 " + bad.Count + ": " + string.Join("; ", bad.Take(3)) : ""));
            if (rebuiltBodies > 0) Run.Metric("导入/特征重建实体数", rebuiltBodies);

            // 2. 实例位置
            var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in model.Parts) { string path; if (Cat.Reg.TryGetValue(p.Id, out path)) byPath[Path.GetFullPath(path)] = p.Id; }
            var remain = model.World.ToDictionary(kv => kv.Key, kv => kv.Value.Select(Xf.FromRow).ToList());
            int ok = 0, total = 0; double worstT = 0, worstR = 0;
            var miss = new List<string>();
            Sw.CloseAll();
            ModelDoc2 am = Sw.Open(targets.Single(t => t.Main).Path);
            foreach (Component2 c in (object[])((AssemblyDoc)am).GetComponents(false) ?? new object[0])
            {
                string cp = c.GetPathName(), id;
                if (string.IsNullOrEmpty(cp) || !byPath.TryGetValue(Path.GetFullPath(cp), out id)) continue;
                total++;
                var w = Xf.FromArray((double[])((MathTransform)c.GetTotalTransform(false)).ArrayData);
                List<Xf> list;
                if (!remain.TryGetValue(id, out list) || list.Count == 0) { miss.Add(id + " 多出实例 " + c.Name2); continue; }
                int best = -1; double bt = double.MaxValue, br = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    double dt, dr; Xf.Diff(w, list[i], out dt, out dr);
                    if (dt < bt) { bt = dt; br = dr; best = i; }
                }
                if (bt <= 0.01 && br <= 1e-6) { list.RemoveAt(best); ok++; worstT = Math.Max(worstT, bt); worstR = Math.Max(worstR, br); }
                else miss.Add(string.Format("{0} {1}: 最近的原位置相差 {2:F3} mm / 方向 {3:G3}", id, c.Name2, bt, br));
            }
            Sw.CloseAll();
            foreach (var kv in remain) foreach (var x in kv.Value) miss.Add(string.Format("{0} 缺少实例 (原位置 {1:F1},{2:F1},{3:F1})", kv.Key, x.Tx, x.Ty, x.Tz));
            int expected = model.World.Values.Sum(l => l.Count);
            md.AppendLine("## 实例位置 —— 原 " + expected + " 件, 重建 " + total + " 件, 一致 " + ok);
            md.AppendLine();
            foreach (string s in miss.Take(100)) md.AppendLine("- " + s);
            string f = Path.Combine(Run.Checks, "step_conformance.md");
            File.WriteAllText(f, md.ToString(), new UTF8Encoding(false));
            Run.Check("装配位置与原 STEP 一致", miss.Count == 0 && ok == expected,
                string.Format("原 {0} 件, 重建 {1} 件, 一致 {2}; 最大位置差 {3:F5} mm{4}", expected, total, ok, worstT,
                    miss.Count > 0 ? "; 不符 " + miss.Count + ": " + string.Join("; ", miss.Take(3)) : ""), f);
            Run.Metric("导入/零件种数", model.Parts.Count);
            Run.Metric("导入/零件实例数", ok);
        }

        /// <summary>
        /// 原模型自带的干涉 (cad import 时登记在 import.json 的 accepted_interferences) 记为"已登记接受";
        /// 零件按文件路径对应回零件编号, 相对位置与体积须与登记的一致。
        /// </summary>
        public override Func<Checks.Interf, bool> AcceptInterference
        {
            get
            {
                if (model == null || model.AcceptedInterferences.Count == 0) return null;
                var byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in model.Parts) { string path; if (Cat.Reg.TryGetValue(p.Id, out path)) byPath[Path.GetFullPath(path)] = p.Id; }
                return i =>
                {
                    string a, b;
                    return !string.IsNullOrEmpty(i.PathA) && !string.IsNullOrEmpty(i.PathB)
                        && byPath.TryGetValue(Path.GetFullPath(i.PathA), out a) && byPath.TryGetValue(Path.GetFullPath(i.PathB), out b)
                        && model.Accepts(a, b, i.Rel, i.VolumeMm3);
                };
            }
        }

        public override void ConfigureDrawings()
        {
            Drw.PartGeneralNote = null;
            Drw.AsmNotes = "1. 本图由导入的 STEP 模型生成, 零件几何与原模型一致 (见 checks/step_conformance.md)。\n" +
                           "2. 外购件型号、规格以明细表为准, 采购前核对供应商最新样本。\n" +
                           "3. 未注明处按通用技术条件执行。";
        }
    }
}
