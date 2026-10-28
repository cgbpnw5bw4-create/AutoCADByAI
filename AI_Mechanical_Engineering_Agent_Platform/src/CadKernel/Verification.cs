using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace CadKernel
{
    /// <summary>版本 1 的独立复验期望。数值来自设计的解析定义, 不从本次实际测量回填。</summary>
    public sealed class VerifyExpectations
    {
        public int SchemaVersion { get; set; } = 1;
        public List<VerifyPart> Parts { get; set; } = new List<VerifyPart>();
        public List<VerifyStep> Steps { get; set; } = new List<VerifyStep>();
    }

    public sealed class VerifyPart
    {
        public string File { get; set; }
        public List<VerifyConfiguration> Configurations { get; set; } = new List<VerifyConfiguration>();
    }

    public sealed class VerifyConfiguration
    {
        public string Name { get; set; }
        public int? BodyCount { get; set; }
        public double? VolumeM3 { get; set; }
        public double? VolumeToleranceM3 { get; set; }
        public string Oracle { get; set; }
        public List<VerifyDimension> Dimensions { get; set; } = new List<VerifyDimension>();
    }

    public sealed class VerifyDimension
    {
        public string FullName { get; set; }
        public double? ValueM { get; set; }
        public double? ToleranceM { get; set; }
    }

    public sealed class VerifyStep
    {
        public string File { get; set; }
        public int? BodyCount { get; set; }
        public double? VolumeM3 { get; set; }
        public double? VolumeToleranceM3 { get; set; }
        public string Oracle { get; set; }
    }

    public sealed class VerificationReport
    {
        public int SchemaVersion { get; set; } = 1;
        public string Status { get; set; } = "INCOMPLETE";
        public string SourceRun { get; set; }
        public string StartedAtUtc { get; set; }
        public string FinishedAtUtc { get; set; }
        public string ExpectationsSha256Before { get; set; }
        public string ExpectationsSha256After { get; set; }
        public bool SourceRunStatusUsed { get; set; }
        public bool ExplicitRebuildCallsMade { get; set; }
        public bool NativeSaveCallsMade { get; set; }
        public int ForcedCleanupCount { get; set; }
        public bool SessionConnected { get; set; }
        public bool SessionExitedNormally { get; set; }
        public string Scope { get; set; } = "仅复验清单明确列出的原生零件、保存配置及 STEP; 不代表全项目或全部参数通过";
        public List<VerifyFileResult> Files { get; set; } = new List<VerifyFileResult>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class VerifyFileResult
    {
        public string File { get; set; }
        public string Kind { get; set; }
        public string Source { get; set; }
        public string Copy { get; set; }
        public string SourceSha256Before { get; set; }
        public string CopySha256Before { get; set; }
        public string SourceSha256After { get; set; }
        public string CopySha256After { get; set; }
        public bool Passed { get; set; }
        public List<VerifyObservation> Observations { get; set; } = new List<VerifyObservation>();
        public List<string> Errors { get; set; } = new List<string>();
    }

    public sealed class VerifyObservation
    {
        public string Configuration { get; set; }
        public int BodyCount { get; set; }
        public double VolumeM3 { get; set; }
        public Dictionary<string, double> DimensionsM { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// 解析期望 → 复制与哈希 → 新鲜专用实例只读原生副本/导入 STEP 副本 → 读回 → 退出后核对哈希。
    /// 不继承源运行的状态, 不调用重建或保存; 不操作源工件。
    /// </summary>
    public static class Verification
    {
        public const string ExpectationsFileName = "verify_expectations.json";
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        public static VerifyExpectations Parse(string text)
        {
            using (var doc = JsonDocument.Parse(text))
            {
                RejectDuplicates(doc.RootElement);
                if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("schema_version", out _))
                    throw new JsonException("缺少 schema_version");
            }
            return JsonSerializer.Deserialize<VerifyExpectations>(text, Json) ?? throw new JsonException("复验期望为空");
        }

        static void RejectDuplicates(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in value.EnumerateObject())
                {
                    if (!names.Add(p.Name)) throw new JsonException("重复字段: " + p.Name);
                    RejectDuplicates(p.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
                foreach (var v in value.EnumerateArray()) RejectDuplicates(v);
        }

        public static void SaveExpectations(string runDir, VerifyExpectations expectations)
        {
            var errors = Validate(expectations, runDir);
            if (errors.Count != 0) throw new ArgumentException(string.Join("; ", errors));
            string path = Path.Combine(runDir, ExpectationsFileName);
            string json = JsonSerializer.Serialize(expectations, Json);
            if (File.Exists(path))
            {
                if (File.ReadAllText(path) == json) return;
                throw new IOException("已有复验期望与当前解析定义不同, 不允许覆盖: " + path);
            }
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file, new UTF8Encoding(false));
            writer.Write(json);
        }

        /// <summary>纯逻辑校验; 保存期望时 STEP 尚未导出, requireFiles 只在执行复验前启用。</summary>
        public static List<string> Validate(VerifyExpectations e, string runDir, bool requireFiles = false)
        {
            var errors = new List<string>();
            if (e == null) { errors.Add("缺少复验期望"); return errors; }
            if (e.SchemaVersion != 1) errors.Add("仅支持 schema_version=1");
            if (e.Parts == null || e.Parts.Count == 0) errors.Add("至少需要一个原生零件期望");
            if (e.Steps == null || e.Steps.Count == 0) errors.Add("至少需要一个 STEP 期望");
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void FileCheck(string file, params string[] extensions)
            {
                try
                {
                    string full = ResolvePath(runDir, file);
                    if (!extensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase)) errors.Add("复验文件类型不符: " + file);
                    if (!files.Add(full)) errors.Add("复验文件重复: " + file);
                    if (requireFiles && !System.IO.File.Exists(full)) errors.Add("复验文件不存在: " + file);
                }
                catch (Exception x) { errors.Add("复验路径无效: " + file + ": " + x.Message); }
            }
            foreach (var p in e.Parts ?? new List<VerifyPart>())
            {
                if (p == null) { errors.Add("零件期望为空"); continue; }
                FileCheck(p.File, ".SLDPRT");
                if (p.Configurations == null || p.Configurations.Count == 0) errors.Add(p.File + ": 缺少明确的保存配置");
                var configurations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in p.Configurations ?? new List<VerifyConfiguration>())
                {
                    if (c == null) { errors.Add(p.File + ": 配置期望为空"); continue; }
                    string label = p.File + " / " + c.Name;
                    if (string.IsNullOrWhiteSpace(c.Name) || !configurations.Add(c.Name)) errors.Add(label + ": 配置名缺失或重复");
                    ValidateGeometry(label, c.BodyCount, c.VolumeM3, c.VolumeToleranceM3, c.Oracle, errors);
                    if (c.Dimensions == null) errors.Add(label + ": dimensions 不能为 null");
                    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in c.Dimensions ?? new List<VerifyDimension>())
                    {
                        if (d == null) { errors.Add(label + ": 尺寸期望为空"); continue; }
                        if (string.IsNullOrWhiteSpace(d.FullName) || !d.FullName.Contains('@') || !names.Add(d.FullName))
                            errors.Add(label + ": 尺寸参数名缺失、重复或不含特征名");
                        if (!Positive(d.ValueM) || !Positive(d.ToleranceM) || d.ToleranceM >= d.ValueM)
                            errors.Add(label + ": 尺寸及绝对允差须为有限正数, 且允差小于期望值");
                    }
                }
            }
            foreach (var s in e.Steps ?? new List<VerifyStep>())
            {
                if (s == null) { errors.Add("STEP 期望为空"); continue; }
                FileCheck(s.File, ".step", ".stp");
                ValidateGeometry(s.File, s.BodyCount, s.VolumeM3, s.VolumeToleranceM3, s.Oracle, errors);
            }
            return errors;
        }

        static bool Positive(double? v) { return v.HasValue && double.IsFinite(v.Value) && v.Value > 0; }

        static void ValidateGeometry(string label, int? count, double? volume, double? tolerance, string oracle, List<string> errors)
        {
            if (!count.HasValue || count <= 0) errors.Add(label + ": 实体数须为明确正整数");
            if (!Positive(volume) || !Positive(tolerance) || tolerance >= volume)
                errors.Add(label + ": 体积及绝对允差须为有限正数, 且允差小于期望值");
            if (string.IsNullOrWhiteSpace(oracle)) errors.Add(label + ": 缺少解析期望依据 oracle");
        }

        internal static bool WithinTolerance(double actual, double expected, double tolerance)
        {
            return double.IsFinite(actual) && double.IsFinite(expected) && double.IsFinite(tolerance)
                && tolerance > 0 && Math.Abs(actual - expected) <= tolerance;
        }

        /// <summary>只允许源运行内部的普通相对文件路径; 拒绝穿越、驱动器相对路径、ADS 和符号链接。</summary>
        internal static string ResolvePath(string runDir, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                throw new ArgumentException("必须为运行目录内的相对路径");
            string[] segments = relative.Replace('\\', '/').Split('/');
            if (segments.Any(s => s.Length == 0 || s == "." || s == "..")) throw new ArgumentException("不允许空段或 . / ..");
            string root = Path.GetFullPath(runDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("文件不在源运行目录内");
            for (string p = full; p != null && p.Length >= root.Length; p = Path.GetDirectoryName(p))
                if ((System.IO.File.Exists(p) || Directory.Exists(p)) && (System.IO.File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("复验路径不能经过符号链接或目录联接: " + p);
            return full;
        }

        internal static string Hash(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        internal static VerifyFileResult CopyForReadback(string sourceRun, string relative, string copyRoot, string kind)
        {
            string source = ResolvePath(sourceRun, relative), copy = ResolvePath(copyRoot, relative);
            var r = new VerifyFileResult { File = relative, Kind = kind, Source = source, Copy = copy, SourceSha256Before = Hash(source) };
            Directory.CreateDirectory(Path.GetDirectoryName(copy));
            System.IO.File.Copy(source, copy, false);
            r.CopySha256Before = Hash(copy);
            if (r.SourceSha256Before != r.CopySha256Before || r.SourceSha256Before != Hash(source))
                throw new IOException("复制期间源文件或副本不一致: " + relative);
            return r;
        }

        internal static void CheckUnchanged(VerifyFileResult r)
        {
            try
            {
                r.SourceSha256After = Hash(r.Source);
                r.CopySha256After = Hash(r.Copy);
                if (r.SourceSha256Before != r.SourceSha256After) r.Errors.Add("源文件 SHA-256 改变");
                if (r.CopySha256Before != r.CopySha256After) r.Errors.Add("复验副本 SHA-256 改变");
            }
            catch (Exception e) { r.Errors.Add("结束哈希检查失败: " + e.Message); }
        }

        public static int Execute(string runDir)
        {
            string source = Path.GetFullPath(runDir);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("源运行目录不存在: " + source);
            string task = Path.GetFileName(source), project = "verification";
            string sourceManifest = Path.Combine(source, "run.json");
            if (System.IO.File.Exists(sourceManifest))
            {
                // 仅取显示用元数据; 损坏或缺少旧清单都不能替代当前期望和读回。
                try
                {
                    using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(sourceManifest));
                    if (doc.RootElement.TryGetProperty("task", out var t) && t.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(t.GetString())) task = t.GetString();
                    if (doc.RootElement.TryGetProperty("project", out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString())) project = p.GetString();
                }
                catch (JsonException) { }
            }
            Run.Begin(task, project, "独立工件复验", null, RunPurpose.Verification, source);
            var report = new VerificationReport { SourceRun = source, StartedAtUtc = DateTime.UtcNow.ToString("O") };
            string expectedPath = Path.Combine(source, ExpectationsFileName);
            bool prepared = false, sessionAttempted = false;
            try
            {
                Run.Step("独立复验", () =>
                {
                    if (!System.IO.File.Exists(expectedPath)) { report.Errors.Add("缺少 " + ExpectationsFileName + ", 未连接 SolidWorks"); return; }
                    report.ExpectationsSha256Before = Hash(expectedPath);
                    string expectedJson = System.IO.File.ReadAllText(expectedPath);
                    var expected = Parse(expectedJson);
                    report.Errors.AddRange(Validate(expected, source, requireFiles: true));
                    if (report.Errors.Count > 0) return;
                    System.IO.File.WriteAllText(Path.Combine(Run.InputSnapshot, ExpectationsFileName), expectedJson, new UTF8Encoding(false));
                    string copies = Path.Combine(Run.Models, "verification_copies");
                    foreach (var p in expected.Parts) report.Files.Add(CopyForReadback(source, p.File, copies, "native_part"));
                    foreach (var s in expected.Steps) report.Files.Add(CopyForReadback(source, s.File, copies, "step"));
                    prepared = true;
                    if (Sw.App != null) throw new InvalidOperationException("独立复验前已有 COM 连接, 拒绝复用或清理该连接");
                    sessionAttempted = true;
                    Sw.Connect(fresh: true);
                    report.SessionConnected = true;
                    foreach (var p in expected.Parts)
                    {
                        var result = report.Files.Single(x => x.File == p.File);
                        try { Sw.Guard("独立只读复验 " + p.File, 300, () => { ReadPart(p, result); return 0; }); }
                        catch (Exception e) { result.Errors.Add("原生读回失败: " + e.Message); }
                    }
                    foreach (var s in expected.Steps)
                    {
                        var result = report.Files.Single(x => x.File == s.File);
                        try { Sw.Guard("独立 STEP 复验 " + s.File, 300, () => { ReadStep(s, result); return 0; }); }
                        catch (Exception e) { result.Errors.Add("STEP 读回失败: " + e.Message); }
                    }
                });
            }
            catch (Exception e) { report.Errors.Add(e.Message); }
            finally
            {
                if (sessionAttempted)
                {
                    try
                    {
                        Sw.Shutdown();
                        report.ForcedCleanupCount = Sw.ForcedCleanupCount;
                        report.SessionExitedNormally = report.SessionConnected && Sw.LastShutdownNormal && report.ForcedCleanupCount == 0;
                        if (report.ForcedCleanupCount > 0) report.Errors.Add("专用实例发生强制清理, 不计为正常退出");
                        else if (report.SessionConnected && !report.SessionExitedNormally) report.Errors.Add("专用实例未确认正常退出");
                    }
                    catch (Exception e)
                    {
                        report.ForcedCleanupCount = Sw.ForcedCleanupCount;
                        report.Errors.Add("专用实例收尾失败: " + e.Message);
                    }
                }
                foreach (var f in report.Files)
                {
                    CheckUnchanged(f);
                    f.Passed = prepared && f.Errors.Count == 0 && f.Observations.Count > 0;
                }
                if (report.ExpectationsSha256Before != null)
                    try
                    {
                        report.ExpectationsSha256After = Hash(expectedPath);
                        if (report.ExpectationsSha256Before != report.ExpectationsSha256After) report.Errors.Add("源复验期望清单 SHA-256 改变");
                    }
                    catch (Exception e) { report.Errors.Add("复验期望结束哈希检查失败: " + e.Message); }
                report.Status = !prepared ? "INCOMPLETE" : report.Errors.Count == 0 && report.Files.All(x => x.Passed) ? "PASSED" : "FAILED";
                report.FinishedAtUtc = DateTime.UtcNow.ToString("O");
                WriteReport(report);
                bool ok = report.Status == "PASSED";
                Run.Check("独立工件复验", ok, report.Status + ": " + report.Files.Count(x => x.Passed) + "/" + report.Files.Count + " 个工件", Path.Combine(Run.Checks, "verification.json"));
                Run.Finish(ok, report.Errors.Count > 0 ? string.Join("; ", report.Errors) : null);
                Run.CloseLog();
            }
            return report.Status == "PASSED" ? 0 : report.Status == "INCOMPLETE" ? 2 : 1;
        }

        /// <summary>只切换到明确保存且尚未激活的配置; 返回值与活动身份都须吻合, 不显式重建。</summary>
        internal static void SelectSavedConfiguration(IReadOnlyCollection<string> names, string name,
            Func<string> current, Func<string, bool> select)
        {
            if (names == null || !names.Contains(name, StringComparer.Ordinal)) throw new IOException("保存配置列表中不存在: " + name);
            if (string.Equals(current(), name, StringComparison.Ordinal)) return;
            if (!select(name)) throw new IOException("切换到保存配置失败: " + name);
            if (!string.Equals(current(), name, StringComparison.Ordinal)) throw new IOException("活动配置身份不符: " + name);
        }

        static void ReadPart(VerifyPart expected, VerifyFileResult result)
        {
            ModelDoc2 model = null;
            try
            {
                int error = 0, warning = 0;
                model = Sw.App.OpenDoc6(result.Copy, (int)swDocumentTypes_e.swDocPART,
                    (int)(swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_e.swOpenDocOptions_ReadOnly), "", ref error, ref warning);
                int unexpected = warning & ~(int)swFileLoadWarning_e.swFileLoadWarning_ReadOnly;
                if (model == null || error != 0 || unexpected != 0) throw new IOException("只读打开 err=" + error + " warn=" + warning);
                if (!string.Equals(Path.GetFullPath(model.GetPathName()), result.Copy, StringComparison.OrdinalIgnoreCase)) throw new IOException("打开的文档身份不是复验副本");
                Sw.Activate(model);
                string[] configurations = (string[])model.GetConfigurationNames();
                foreach (var e in expected.Configurations)
                {
                    SelectSavedConfiguration(configurations, e.Name,
                        () => model.ConfigurationManager.ActiveConfiguration.Name, cfg => model.ShowConfiguration2(cfg));
                    var observation = Measure(model, e.Name);
                    result.Observations.Add(observation);
                    CompareGeometry(result, observation, e.BodyCount.Value, e.VolumeM3.Value, e.VolumeToleranceM3.Value);
                    int wrong = model.Extension.GetWhatsWrongCount();
                    if (wrong != 0) result.Errors.Add(e.Name + ": 保存特征存在错误/警告 " + wrong);
                    foreach (var d in e.Dimensions)
                    {
                        var dimension = model.Parameter(d.FullName) as Dimension;
                        if (dimension == null) throw new IOException("找不到保存尺寸参数: " + d.FullName);
                        var values = dimension.GetSystemValue3((int)swInConfigurationOpts_e.swThisConfiguration, null) as double[];
                        if (values == null || values.Length != 1 || !double.IsFinite(values[0])) throw new IOException("保存尺寸返回类型或数目无效: " + d.FullName);
                        observation.DimensionsM.Add(d.FullName, values[0]);
                        if (!WithinTolerance(values[0], d.ValueM.Value, d.ToleranceM.Value)) result.Errors.Add(e.Name + ": 尺寸 " + d.FullName + "=" + values[0] + ", 期望 " + d.ValueM.Value);
                    }
                }
            }
            finally { if (model != null) Sw.Close(model); }
        }

        static void ReadStep(VerifyStep expected, VerifyFileResult result)
        {
            ModelDoc2 model = null;
            try
            {
                model = Sw.WithImportPrefs(() =>
                {
                    int error = 0;
                    object data = Sw.App.GetImportFileData(result.Copy);
                    var loaded = (ModelDoc2)Sw.App.LoadFile4(result.Copy, "r", data, ref error);
                    if (loaded == null || error != 0) throw new IOException("STEP 副本导入 err=" + error);
                    return loaded;
                });
                Sw.Activate(model);
                var observation = Measure(model, "STEP 导入副本");
                result.Observations.Add(observation);
                CompareGeometry(result, observation, expected.BodyCount.Value, expected.VolumeM3.Value, expected.VolumeToleranceM3.Value);
            }
            finally { if (model != null) Sw.Close(model); }
        }

        static VerifyObservation Measure(ModelDoc2 model, string configuration)
        {
            var observation = new VerifyObservation { Configuration = configuration };
            void AddPart(PartDoc part)
            {
                var bodies = part.GetBodies2((int)swBodyType_e.swSolidBody, false) as object[] ?? new object[0];
                foreach (Body2 body in bodies)
                {
                    var values = body.GetMassProperties(1.0) as double[];
                    if (values == null || values.Length < 4 || !double.IsFinite(values[3]) || values[3] <= 0) throw new IOException("实体体积读回失败");
                    observation.BodyCount++;
                    observation.VolumeM3 += values[3];
                }
            }
            if (model is PartDoc p) AddPart(p);
            else if (model is AssemblyDoc a)
            {
                foreach (Component2 component in a.GetComponents(false) as object[] ?? new object[0])
                {
                    if (component.IsSuppressed()) throw new IOException("STEP 导入含压缩零部件");
                    var child = component.GetModelDoc2() as ModelDoc2;
                    if (child == null) throw new IOException("STEP 导入含未载入零部件");
                    if (child is PartDoc part)
                    {
                        var transform = component.Transform2.ArrayData as double[];
                        if (transform == null || transform.Length < 13 || !WithinTolerance(transform[12], 1, 1e-12)) throw new IOException("STEP 实例有未覆盖的缩放变换");
                        AddPart(part);
                    }
                }
            }
            else throw new IOException("复验仅支持原生零件或 STEP 导入的零件/装配体");
            return observation;
        }

        static void CompareGeometry(VerifyFileResult result, VerifyObservation actual, int count, double volume, double tolerance)
        {
            if (actual.BodyCount != count) result.Errors.Add(actual.Configuration + ": 实体数 " + actual.BodyCount + ", 期望 " + count);
            if (!WithinTolerance(actual.VolumeM3, volume, tolerance)) result.Errors.Add(actual.Configuration + ": 体积 " + actual.VolumeM3 + " m³, 期望 " + volume + " ± " + tolerance);
        }

        static void WriteReport(VerificationReport report)
        {
            System.IO.File.WriteAllText(Path.Combine(Run.Checks, "verification.json"), JsonSerializer.Serialize(report, Json), new UTF8Encoding(false));
            var text = new StringBuilder("# 独立工件复验\n\n");
            text.AppendLine("状态: **" + report.Status + "**");
            text.AppendLine("\n源运行: " + report.SourceRun + " (未使用其通过状态)");
            text.AppendLine("\n" + report.Scope + "。原生仅只读打开副本, 未显式重建或保存; STEP 仅导入副本。\n");
            text.AppendLine("正常退出: " + report.SessionExitedNormally + "; 强制清理次数: " + report.ForcedCleanupCount + "\n");
            foreach (var file in report.Files)
            {
                text.AppendLine("- " + file.File + ": " + (file.Passed ? "PASSED" : "FAILED"));
                foreach (string error in file.Errors) text.AppendLine("  - " + error);
            }
            foreach (string error in report.Errors) text.AppendLine("- " + error);
            System.IO.File.WriteAllText(Path.Combine(Run.Checks, "verification.md"), text.ToString(), new UTF8Encoding(false));
        }
    }
}
