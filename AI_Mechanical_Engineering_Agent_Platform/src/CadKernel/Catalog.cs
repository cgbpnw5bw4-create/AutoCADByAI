using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SolidWorks.Interop.sldworks;

namespace CadKernel
{
    public enum Kind { Make, Buy, Std, Goods }   // 自制件 / 外购件 / 标准件 / 工艺物料(不计入设备 BOM)

    /// <summary>GB 牌号 -> SolidWorks 库材质(外观/剖面线) + 密度(质量覆盖用)</summary>
    public class Mat
    {
        public string GB, SwName; public double Dens;
        public Mat(string gb, string sw, double d) { GB = gb; SwName = sw; Dens = d; }

        public static readonly Mat S304 = new Mat("S30408 (06Cr19Ni10)", "AISI 304", 7930);
        public static readonly Mat S316 = new Mat("S31603 (022Cr17Ni12Mo2)", "AISI 类型 316L 不锈钢", 7980);
        public static readonly Mat Q235 = new Mat("Q235B", "普通碳钢", 7850);
        public static readonly Mat C45 = new Mat("45 (调质 HB220~250)", "AISI 1045 钢，冷拔", 7850);
        public static readonly Mat AL = new Mat("6063-T5", "6063-T5", 2700);
        public static readonly Mat PP = new Mat("PP (聚丙烯板)", "PP 共聚物", 910);
        public static readonly Mat PE = new Mat("UHMW-PE", "交联聚乙烯", 940);
        public static readonly Mat POM = new Mat("POM", "POM 乙缩醛共聚物", 1410);
        public static readonly Mat NBR = new Mat("NBR 丁腈橡胶 邵A70", "橡胶", 1250);
        public static readonly Mat TA2 = new Mat("TA2", "钛", 4510);
        public static readonly Mat CU = new Mat("T2", "铜", 8900);
        public static readonly Mat WOOL = new Mat("岩棉板 120kg/m³", "刚性聚氨酯泡沫体", 120);
        public static readonly Mat HT = new Mat("HT200", "灰铸铁", 7200);
        public static readonly Mat STEEL = new Mat("钢 (外购)", "普通碳钢", 7850);
        public static readonly Mat ST_A2 = new Mat("A2-70 不锈钢", "AISI 304", 7930);
        public static readonly Mat ST_88 = new Mat("8.8 级 发黑", "合金钢", 7850);
        public static readonly Mat PLASTIC = new Mat("工程塑料", "PP 共聚物", 1100);
        // 齿轮/轴类 (SOLIDWORKS 材料库名称已在本机核对)
        public static readonly Mat S4340 = new Mat("40CrNiMoA (AISI 4340, 正火)", "AISI 4340 钢，正火", 7850);
        public static readonly Mat ALLOY = new Mat("42CrMo (调质)", "合金钢", 7700);
        // 导入件 (STEP 不含材料): 按名称推断不出时用它, 质量按钢 7850 估算 (BOM 备注栏注明)
        public static readonly Mat PVC = new Mat("PVC", "PP 共聚物", 1400);
        public static readonly Mat UNKNOWN = new Mat("未定", "普通碳钢", 7850);

        /// <summary>按字段名取材料 (import.json 中的 "material": "S304" 等)</summary>
        public static Mat ByKey(string key)
        {
            var f = typeof(Mat).GetField(key ?? "", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (f == null || f.FieldType != typeof(Mat)) throw new Exception("未知材料: " + key);
            return (Mat)f.GetValue(null);
        }
    }

    /// <summary>零件定义 (元数据 + 出图提示)</summary>
    public class Part
    {
        public string Id, Dir, Code, Name, Spec = "", Process = "", Blank = "", Surface = "", Brand = "", Remark = "";
        public Kind Kind = Kind.Make;
        public Mat Mat = Mat.S304;
        public double[] Color = Col.Sus;
        public double CatalogKg;        // 外购件样本质量 (>0 时覆盖)
        public string DrwType = "";      // plate / turn / weld / sheet / none
        public string Fits = "";         // 配合: "25:h7;20:k6" (直径:公差带)
        public string Notes = "";        // 技术要求, '|' 分隔
        public string Taps = "";         // 螺纹孔 (按公称直径建模): "10:M10-6H;8:M8-6H"
        public int Bodies = 1;           // 允许的实体数 (多材质零件 >1)
        public bool Dxf;                 // 输出激光下料 DXF
        public string DxfNormal = "";    // DXF 取面法向 (X/Y/Z)
        public string Ra = "";           // 其余表面粗糙度: "" = Ra 3.2, "-" = 不标
        /// <summary>工程图参数表 (如齿轮参数): "名称|值;名称|值", 出图时放在右上角</summary>
        public string Table = "";
        /// <summary>回转体零件图只标注这些直径 ("16;31", mm); 空 = 剖视图中全部对称直径。齿轮等非回转特征会投影出伪直径</summary>
        public string Dias = "";

        public string KindCn
        {
            get { return Kind == Kind.Make ? "自制件" : Kind == Kind.Buy ? "外购件" : Kind == Kind.Std ? "标准件" : "工艺物料"; }
        }
    }

    /// <summary>通用颜色 (RGB 0..1)</summary>
    public static class Col
    {
        public static readonly double[] Frame = { 0.16, 0.30, 0.48 };
        public static readonly double[] Sus = { 0.76, 0.78, 0.80 };
        public static readonly double[] Sus316 = { 0.68, 0.72, 0.76 };
        public static readonly double[] Steel = { 0.45, 0.47, 0.50 };
        public static readonly double[] Alu = { 0.82, 0.84, 0.87 };
        public static readonly double[] Copper = { 0.85, 0.52, 0.25 };
        public static readonly double[] Rubber = { 0.12, 0.12, 0.13 };
        public static readonly double[] PP = { 0.93, 0.93, 0.88 };
        public static readonly double[] PE = { 0.96, 0.96, 0.96 };
        public static readonly double[] Cabinet = { 0.86, 0.87, 0.89 };
        public static readonly double[] Motor = { 0.25, 0.42, 0.30 };
        public static readonly double[] Servo = { 0.18, 0.18, 0.20 };
        public static readonly double[] Orange = { 0.90, 0.45, 0.10 };
        public static readonly double[] Blue = { 0.15, 0.35, 0.70 };
        public static readonly double[] Yellow = { 0.85, 0.70, 0.10 };
        public static readonly double[] Zinc = { 0.70, 0.72, 0.74 };
        public static readonly double[] Black = { 0.10, 0.10, 0.10 };
        public static readonly double[] Red = { 0.70, 0.12, 0.10 };
        public static readonly double[] Green = { 0.30, 0.65, 0.35 };
        public static readonly double[] Grey = { 0.62, 0.64, 0.68 };
        public static readonly double[] Cast = { 0.33, 0.34, 0.36 };      // 铸件灰
        public static readonly double[] Base = { 0.14, 0.14, 0.15 };      // 底座黑
        public static readonly double[] Ral7035 = { 0.80, 0.81, 0.79 };   // 柜体浅灰
    }

    /// <summary>
    /// 零件登记与建模流水线。登记表 Reg: 标识 -> 已建文件路径。
    /// 按需模式 (Lazy=true): Make 只登记定义, 在 P(标识) 或装配体插入时才真正建模 ——
    /// 只出某个模块时不会把整台设备的零件全部建一遍。
    /// </summary>
    public static class Cat
    {
        public static readonly Dictionary<string, string> Reg = new Dictionary<string, string>();
        public static readonly Dictionary<string, Part> Parts = new Dictionary<string, Part>();
        public static int Built;

        /// <summary>写入每个零件的 "项目/版本/设计日期" 属性 (由设计项目设置)</summary>
        public static string ProjectName = "", Version = "V1.0", Date = DateTime.Now.ToString("yyyy-MM-dd");
        public static bool Lazy;

        // 按需模式: 标识 -> 计划路径; 计划路径 -> 待建定义
        static readonly Dictionary<string, string> Planned = new Dictionary<string, string>();
        static readonly Dictionary<string, Tuple<Part, Action<ModelDoc2>>> Pending =
            new Dictionary<string, Tuple<Part, Action<ModelDoc2>>>(StringComparer.OrdinalIgnoreCase);

        static string Safe(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }

        /// <summary>清空登记与项目设置。每次运行开始时由 Pipeline 调用: 静态状态不得带到下一次运行 (路径属于上一个运行目录)</summary>
        public static void Reset()
        {
            Reg.Clear(); Parts.Clear(); Planned.Clear(); Pending.Clear();
            Built = 0; Lazy = false;
            ProjectName = ""; Version = "V1.0"; Date = DateTime.Now.ToString("yyyy-MM-dd");
        }

        public static string FileOf(Part p)
        {
            return Path.Combine(Run.Models, p.Dir ?? "", Safe(p.Code + "_" + p.Name) + ".SLDPRT");
        }

        /// <summary>建零件 -> 校核(孤立草图/实体数/体积) -> 材质/颜色 -> 质量覆盖 -> 属性 -> 保存 -> 登记</summary>
        public static string Make(Part p, Action<ModelDoc2> build)
        {
            if (Reg.ContainsKey(p.Id) && Parts.ContainsKey(p.Id)) return Reg[p.Id];   // 同一次运行内已建
            if (Lazy)
            {
                string fp;
                if (!Planned.TryGetValue(p.Id, out fp))
                {
                    fp = FileOf(p);
                    Planned[p.Id] = fp;
                    Pending[fp] = Tuple.Create(p, build);
                }
                return fp;
            }
            return Build(p, build);
        }

        /// <summary>零件路径; 按需模式下若尚未建则立即建</summary>
        public static string P(string id)
        {
            string v;
            if (Reg.TryGetValue(id, out v)) return EnsureBuilt(v);
            if (Planned.TryGetValue(id, out v)) return EnsureBuilt(v);
            throw new Exception("登记表缺少: " + id);
        }

        /// <summary>同一文件登记第二个标识 (如两个工位共用同一零件)</summary>
        public static void Alias(string id, string existingId)
        {
            string v;
            if (Reg.TryGetValue(existingId, out v)) Reg[id] = v;
            else if (Planned.TryGetValue(existingId, out v)) Planned[id] = v;
            else throw new Exception("登记表缺少: " + existingId);
        }

        public static bool Has(string id) { return Reg.ContainsKey(id) || Planned.ContainsKey(id); }

        /// <summary>按路径确保零件已建 (装配体插入前调用); 不是待建零件的路径原样返回</summary>
        public static string EnsureBuilt(string path)
        {
            Tuple<Part, Action<ModelDoc2>> def;
            if (Pending.TryGetValue(path, out def))
            {
                Pending.Remove(path);
                Build(def.Item1, def.Item2);
                foreach (var kv in new List<KeyValuePair<string, string>>(Planned))
                    if (string.Equals(kv.Value, path, StringComparison.OrdinalIgnoreCase)) Reg[kv.Key] = path;
            }
            return path;
        }

        /// <summary>
        /// 导入几何零件 (cad import 从 STEP 拆出的 Parasolid x_t): 载入 → 实体数 → 材质/颜色/质量/属性 → 保存 → 登记。
        /// 与 Make 共用后半段; 导入件没有特征历史, 没有特征级体积校核, 几何是否与原件一致由设计项目另行核对。
        /// </summary>
        public static string Import(Part p, string geometryFile, Action<ModelDoc2> extra = null)
        {
            if (Reg.ContainsKey(p.Id) && Parts.ContainsKey(p.Id)) return Reg[p.Id];
            ModelDoc2 m = Sw.LoadGeometry(geometryFile);
            if (extra != null)
            {
                // 在原几何之上再建特征实体 (按实体重建的零件: 未重建的实体来自 geometryFile)
                try
                {
                    extra(m);
                    m.ForceRebuild3(false);
                    var orphan = Sw.OrphanSketches(m);
                    if (orphan.Count > 0) throw new Exception("存在未消费草图(特征静默失败): " + string.Join(",", orphan.ToArray()));
                }
                catch (Exception e)
                {
                    try { Sw.App.CloseDoc(m.GetTitle()); } catch { }
                    throw new Exception(p.Code + " " + p.Name + " -> " + e.Message, e);
                }
            }
            return Finish(p, m);
        }

        static string Build(Part p, Action<ModelDoc2> build)
        {
            ModelDoc2 m = Sw.NewPart();
            try
            {
                build(m);
                m.ForceRebuild3(false);
                var orphan = Sw.OrphanSketches(m);
                if (orphan.Count > 0) throw new Exception("存在未消费草图(特征静默失败): " + string.Join(",", orphan.ToArray()));
            }
            catch (Exception e)
            {
                try { Sw.App.CloseDoc(m.GetTitle()); } catch { }
                throw new Exception(p.Code + " " + p.Name + " -> " + e.Message, e);
            }
            return Finish(p, m);
        }

        static string Finish(Part p, ModelDoc2 m)
        {
            try
            {
                // 钣金零件按折叠状态计质量、保存 (装配、干涉、出图都用折叠后的几何)
                var flat = Sw.Fold(m);
                if (flat.Count > 0) Sw.Log("    零件处于展平状态 (平板型式未压缩), 已压缩回折叠: " + string.Join(",", flat));
                int nb = Sw.BodyCount(m);
                if (nb != p.Bodies) throw new Exception("实体数 " + nb + " ≠ " + p.Bodies + " (存在悬空/未连接的几何)");
                Sw.SetMaterial(m, p.Mat.SwName);
                Sw.SetColor(m, p.Color);
                double vol = Sw.Volume(m);
                if (vol <= 0) throw new Exception("零件体积为 0");
                double kg = p.CatalogKg > 0 ? p.CatalogKg : vol * 1e-9 * p.Mat.Dens;
                Sw.OverrideMass(m, kg);

                Sw.Prop(m, "代号", p.Code); Sw.Prop(m, "零件号", p.Code); Sw.Prop(m, "名称", p.Name);
                Sw.Prop(m, "材料", p.Kind == Kind.Make ? p.Mat.GB : (p.Kind == Kind.Std ? p.Mat.GB : "—"));
                Sw.Prop(m, "质量", kg.ToString(kg < 1 ? "F3" : "F2"));
                Sw.Prop(m, "类别", p.KindCn); Sw.Prop(m, "规格", p.Spec); Sw.Prop(m, "工艺", p.Process);
                Sw.Prop(m, "下料", p.Blank); Sw.Prop(m, "表面处理", p.Surface); Sw.Prop(m, "品牌", p.Brand);
                Sw.Prop(m, "备注", p.Remark); Sw.Prop(m, "版本", Version); Sw.Prop(m, "设计日期", Date);
                Sw.Prop(m, "项目", ProjectName);
                Sw.Prop(m, "_DRW", p.Kind == Kind.Make ? p.DrwType : "none");
                Sw.Prop(m, "_FITS", p.Fits); Sw.Prop(m, "_NOTES", p.Notes); Sw.Prop(m, "_TAPS", p.Taps);
                Sw.Prop(m, "_DXF", p.Dxf ? p.DxfNormal : "");
                Sw.Prop(m, "_RA", p.Ra); Sw.Prop(m, "_TABLE", p.Table); Sw.Prop(m, "_DIAS", p.Dias);

                string path = Sw.Save(m, FileOf(p));
                double[] b = Sw.PartBox(m);
                Sw.Log(string.Format("{0,-16} {1,-24} {2,8:F2}kg  {3}", p.Code, p.Name, kg, Sw.BoxStr(b)));
                Sw.Close(m);
                Reg[p.Id] = path; Parts[p.Id] = p; Built++;
                return path;
            }
            catch (Exception e)
            {
                try { Sw.App.CloseDoc(m.GetTitle()); } catch { }
                throw new Exception(p.Code + " " + p.Name + " -> " + e.Message, e);
            }
        }

        // ---------------- 登记表持久化 (随运行目录保存, 供续跑) ----------------
        public static string RegFile { get { return Path.Combine(Run.Dir, "registry.tsv"); } }

        public static void SaveReg()
        {
            var sb = new StringBuilder();
            foreach (var kv in Reg) sb.AppendLine(kv.Key + "\t" + kv.Value);
            File.WriteAllText(RegFile, sb.ToString(), Encoding.UTF8);
            Sw.Log("登记表已保存: " + Reg.Count + " 项");
        }

        public static void LoadReg()
        {
            if (!File.Exists(RegFile)) throw new Exception("找不到登记表, 请先完成零件步骤: " + RegFile);
            foreach (string line in File.ReadAllLines(RegFile, Encoding.UTF8))
            {
                string[] q = line.Split('\t');
                if (q.Length == 2) Reg[q[0]] = q[1];
            }
            Sw.Log("登记表已载入: " + Reg.Count + " 项");
        }
    }
}
