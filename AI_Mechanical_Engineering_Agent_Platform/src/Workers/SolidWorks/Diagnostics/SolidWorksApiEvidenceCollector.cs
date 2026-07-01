using System.Text;
using System.Text.Json;
using DomainSchemas;

namespace SolidWorksWorker.Diagnostics;

public sealed record SolidWorksApiEvidenceCollectionResult(
    ApiEvidenceReport Report,
    string JsonReportPath,
    string MarkdownReportPath,
    string MacroRecordingRequestPath,
    bool ReferenceSkillAnalysisRead,
    bool ReferenceRepositoryFound,
    bool ExternalScriptsCopied);

public sealed class SolidWorksApiEvidenceCollector
{
    public const string CutHolesEvidenceFileStem = "cut_holes_failed_api_evidence_report";
    public const string MacroRecordingRequestFileName = "macro_recording_request.md";

    public SolidWorksApiEvidenceCollectionResult CollectCutHolesEvidence(
        string projectRoot,
        string outputRoot,
        SolidWorksApiFailureAnalysis analysis,
        string currentCallSummary)
    {
        var evidenceDirectory = Path.GetFullPath(Path.Combine(outputRoot, "solidworks", "diagnostics", "api_evidence"));
        Directory.CreateDirectory(evidenceDirectory);

        var localSources = new List<ApiEvidenceSource>();
        var thirdPartySources = new List<ApiEvidenceSource>();
        var riskNotes = new List<string>
        {
            "FeatureCut4 是长参数 COM 方法，late binding 下参数数量或版本差异会直接导致调用失败。",
            "切孔前必须明确草图、轮廓或选择状态，不能依赖上一步残留选择。",
            "本项目不能直接复制第三方 scripts 源码，只能吸收 API 调用顺序和封装思想。"
        };
        var implementationNotes = new List<string>
        {
            currentCallSummary,
            "用户第二份录制宏显示：FeatureExtrusion2 用于板件基体拉伸，FeatureCut4 用于后续活动孔草图切除。",
            "本轮选择继续复用 SolidWorksPlateFeatureBuilder.CreateBasePlate 创建基体，并由 SolidWorksPlateFeatureBuilder.CreateThroughHoles 封装宏录制的活动孔草图切除路径。"
        };

        implementationNotes.Add("用户第二份宏已覆盖上一轮一草图假设：正确主链路是先用 FeatureExtrusion2 拉伸板件基体，再在活动孔草图中用 FeatureCut4 切除。");

        var analysisPath = Path.Combine(projectRoot, "references", "external", "solidworks-automation-skill-analysis.md");
        var referenceSkillAnalysisRead = File.Exists(analysisPath);
        if (referenceSkillAnalysisRead)
        {
            var text = File.ReadAllText(analysisPath);
            localSources.Add(new ApiEvidenceSource(
                "local_reference",
                "solidworks-automation-skill-analysis.md",
                analysisPath,
                new[] { "sw_part.py", "sw_review.py", "sw_export.py", "FeatureCut", "CreateCircleByRadius" },
                SummarizeLocalAnalysis(text),
                "本文件是本项目已有中文分析文档，可复用架构思想，不包含可直接生产执行的第三方源码。",
                CanReuseCode: false,
                CanReuseIdea: true));
        }
        else
        {
            riskNotes.Add("missing_reference_analysis: references/external/solidworks-automation-skill-analysis.md 不存在。");
        }

        var referenceRepository = Path.Combine(projectRoot, "references", "external", "solidworks-automation-skill");
        var referenceRepositoryFound = Directory.Exists(referenceRepository);
        if (referenceRepositoryFound)
        {
            thirdPartySources.AddRange(ScanReferenceRepository(referenceRepository));
        }
        else
        {
            riskNotes.Add("missing_reference_repo: references/external/solidworks-automation-skill 目录不存在，已跳过第三方源码只读分析。");
        }

        var officialSources = CreateOfficialSources();
        var candidates = CreateCutHoleCandidates();
        var report = new ApiEvidenceReport(
            $"api-evidence-{Guid.NewGuid():N}",
            DateTimeOffset.UtcNow,
            analysis.FailureStage,
            analysis.TargetOperation,
            officialSources,
            localSources,
            thirdPartySources,
            candidates,
            "macro_recorded_active_sketch_featurecut4_after_base_extrusion",
            new[]
            {
                "只保留原始 FeatureCut4 长参数调用：已在诊断中触发参数数量不匹配，不能继续作为唯一策略。",
                "直接执行第三方 Python COM 脚本：违反平台 Worker 边界和本轮禁止复制脚本的约束。",
                "跳过切孔进入保存：会产生假成功，不能接受。"
            },
            riskNotes,
            implementationNotes,
            "按用户第二份录制宏执行：先用 FeatureExtrusion2 创建板件基体，再进入活动孔草图，用 CreateCircle 创建孔圆并调用 FeatureCut4 切除。后续如果仍失败，再回到 API evidence 流程分析宏参数差异。");

        report = report with
        {
            FinalRecommendation = "按用户第二份录制宏执行：先选择标准基准面并进入基体草图，用 CreateCenterRectangle 创建板件外轮廓，调用 FeatureExtrusion2 生成基体；随后重新选择标准基准面并进入孔草图，用 CreateCircle 创建孔圆，保持孔草图活动状态直接调用 FeatureCut4。若仍失败，记录 api_evidence_insufficient，并继续对照完整文本宏参数。",
            ImplementationNotes = implementationNotes.ToArray()
        };

        var jsonReportPath = Path.Combine(evidenceDirectory, $"{CutHolesEvidenceFileStem}.json");
        var markdownReportPath = Path.Combine(evidenceDirectory, $"{CutHolesEvidenceFileStem}.md");
        var macroRequestPath = Path.Combine(evidenceDirectory, MacroRecordingRequestFileName);

        File.WriteAllText(jsonReportPath, JsonSerializer.Serialize(report, JsonOptions()));
        File.WriteAllText(markdownReportPath, RenderMarkdown(report, analysis));
        File.WriteAllText(macroRequestPath, RenderMacroRecordingRequest());

        return new SolidWorksApiEvidenceCollectionResult(
            report,
            jsonReportPath,
            markdownReportPath,
            macroRequestPath,
            referenceSkillAnalysisRead,
            referenceRepositoryFound,
            ExternalScriptsCopied: false);
    }

    private static IReadOnlyList<ApiEvidenceSource> CreateOfficialSources() =>
        new[]
        {
            new ApiEvidenceSource(
                "official_api",
                "IFeatureManager.FeatureExtrusion2",
                "https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureExtrusion2.html",
                new[] { "FeatureExtrusion2" },
                "官方 API 条目说明 FeatureExtrusion2 属于 FeatureManager 的拉伸特征创建方法；用户第二份录制宏显示它用于先创建板件基体。",
                "官方帮助文档仅作为 API 事实来源，不复制其正文。",
                CanReuseCode: false,
                CanReuseIdea: true),
            new ApiEvidenceSource(
                "official_api",
                "IFeatureManager.FeatureCut4",
                "https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut4.html",
                new[] { "FeatureCut4" },
                "官方 API 条目说明 FeatureCut4 属于 FeatureManager 的切除拉伸特征创建方法；用户第二份录制宏显示它在孔草图仍为活动草图时执行切孔。",
                "官方帮助文档仅作为 API 事实来源，不复制其正文。",
                CanReuseCode: false,
                CanReuseIdea: true),
            new ApiEvidenceSource(
                "official_api",
                "IFeatureManager.FeatureCut3",
                "https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeatureManager~FeatureCut3.html",
                new[] { "FeatureCut3" },
                "官方 API 条目说明 FeatureCut3 也是 FeatureManager 的切除拉伸特征创建方法。相对 FeatureCut4，候选参数签名更短，适合作为当前 late binding 修复候选。",
                "官方帮助文档仅作为 API 事实来源，不复制其正文。",
                CanReuseCode: false,
                CanReuseIdea: true),
            new ApiEvidenceSource(
                "official_api",
                "ISketchManager.CreateCircleByRadius",
                "https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISketchManager~CreateCircleByRadius.html",
                new[] { "CreateCircleByRadius" },
                "官方 API 条目说明 CreateCircleByRadius 属于 SketchManager，用于按圆心和半径创建圆。",
                "官方帮助文档仅作为 API 事实来源，不复制其正文。",
                CanReuseCode: false,
                CanReuseIdea: true),
            new ApiEvidenceSource(
                "official_api",
                "IModelDocExtension.SelectByID2",
                "https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IModelDocExtension~SelectByID2.html",
                new[] { "SelectByID2" },
                "官方 API 条目说明 SelectByID2 依赖对象名称、类型、坐标和选择状态。不同语言模板和对象命名会影响选择稳定性。",
                "官方帮助文档仅作为 API 事实来源，不复制其正文。",
                CanReuseCode: false,
                CanReuseIdea: true)
        };

    private static IReadOnlyList<ApiCandidate> CreateCutHoleCandidates() =>
        new[]
        {
            new ApiCandidate(
                "SketchManager.CreateCircleByRadius",
                "在孔草图中按米制坐标和半径创建四个通孔轮廓圆。",
                new[]
                {
                    "选择可用基准面或板面",
                    "进入草图",
                    "按四个孔中心调用 CreateCircleByRadius",
                    "确认返回对象非 null",
                    "退出草图"
                },
                new[] { "模型文档存在", "SketchManager 可用", "孔中心和半径已转换为米" },
                "草图处于编辑状态",
                "返回 SketchSegment 或等价 COM 对象，失败时为 null 或抛出 COM 异常。",
                new[] { "草图未激活", "坐标单位错误", "半径为零", "COM 返回 null" },
                "诊断 Runner 记录每个圆的中心、半径和 COM 返回值。"),
            new ApiCandidate(
                "FeatureManager.FeatureExtrusion2",
                "创建板件基体拉伸特征，不作为切孔 API 使用。",
                new[]
                {
                    "选择标准基准面",
                    "进入草图",
                    "创建中心矩形外轮廓",
                    "直接调用 FeatureExtrusion2 拉伸草图生成基体",
                    "检查返回 Feature 非 null",
                    "进入后续孔草图切孔"
                },
                new[] { "矩形外轮廓在活动草图中", "尺寸单位为米" },
                "活动草图包含闭合矩形外轮廓。",
                "返回拉伸 Feature，几何结果应为板件基体。",
                new[] { "草图轮廓不闭合", "FeatureExtrusion2 返回 null", "宏参数与当前 SolidWorks 版本不匹配" },
                "手动运行 SolidWorksSmokeRunner，检查 base_plate_started、cut_holes_started 和后续输出。"),
            new ApiCandidate(
                "FeatureManager.FeatureCut4",
                "对活动孔草图轮廓执行切除，优先使用用户第二份录制宏中的 FeatureCut4 调用顺序。",
                new[]
                {
                    "选择标准基准面",
                    "进入孔草图",
                    "用 CreateCircle 创建四个孔圆",
                    "保持孔草图活动状态",
                    "调用宏录制的 FeatureCut4 候选",
                    "若返回 null，再退出草图并尝试稳定草图引用 fallback",
                    "检查返回 Feature 非 null",
                    "失败时记录参数候选名和异常"
                },
                new[] { "板件基体已创建", "孔草图处于活动状态", "模型有可切除实体", "切除深度和单位已统一为米" },
                "活动孔草图包含四个孔圆；fallback 才依赖草图引用选择。",
                "返回 Feature COM 对象，失败时为 null 或抛出 COM 异常。",
                new[] { "参数签名不匹配", "选择状态错误", "轮廓未闭合", "切除方向错误" },
                "手动运行 SolidWorksSmokeRunner，检查 cut_holes_success 和后续保存输出。"),
            new ApiCandidate(
                "FeatureManager.FeatureCut3",
                "作为 FeatureCut4 短参数候选仍不稳定时的后续备选，不在本轮作为默认执行路径。",
                new[]
                {
                    "记录 FeatureCut4 候选失败原因",
                    "后续可按官方宏或录制宏验证 FeatureCut3 参数",
                    "检查返回 Feature 非 null",
                    "失败时保留参数不匹配证据"
                },
                new[] { "FeatureCut4 短参数候选不可用或返回 null", "孔草图轮廓仍可用" },
                "与 FeatureCut4 相同，但需要单独查证参数签名。",
                "返回 Feature COM 对象，失败时为 null 或抛出 COM 异常。",
                new[] { "Number of parameters specified does not match the expected number", "COM 版本签名差异" },
                "诊断报告记录 cut_holes_candidate_failed 细节。"),
            new ApiCandidate(
                "ModelDocExtension.SelectByID2",
                "仅作为必要时的显式选择补充，不能作为唯一稳定路径。",
                new[]
                {
                    "记录可选对象名称和类型",
                    "尝试按名称或类型选择草图",
                    "失败时回退到对象级 Select2 或宏证据"
                },
                new[] { "对象名称已知", "文档语言和模板命名已确认" },
                "选择状态由对象名称、类型、坐标和 mark 决定。",
                "返回 bool。",
                new[] { "语言环境导致名称不匹配", "对象未激活", "选择 mark 错误" },
                "诊断报告列出每次选择尝试和结果。")
        };

    private static IReadOnlyList<ApiEvidenceSource> ScanReferenceRepository(string referenceRepository)
    {
        var sources = new List<ApiEvidenceSource>();
        var candidateFiles = Directory
            .EnumerateFiles(referenceRepository, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                           path.EndsWith(".vba", StringComparison.OrdinalIgnoreCase))
            .Take(50);

        foreach (var file in candidateFiles)
        {
            var text = File.ReadAllText(file);
            var apiNames = new[]
                {
                    "CreateCircleByRadius",
                    "FeatureCut4",
                    "FeatureCut3",
                    "SelectByID2",
                    "InsertSketch"
                }
                .Where(api => text.Contains(api, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (apiNames.Length == 0)
            {
                continue;
            }

            sources.Add(new ApiEvidenceSource(
                "third_party_reference",
                Path.GetFileName(file),
                file,
                apiNames,
                "只读扫描到 SolidWorks API 名称，用于提炼调用顺序，不复制源码。",
                "参考仓库采用 MIT License；本项目当前不得复制 scripts 源码，若未来引用片段必须保留版权和许可证。",
                CanReuseCode: false,
                CanReuseIdea: true));
        }

        return sources;
    }

    private static string SummarizeLocalAnalysis(string text)
    {
        var mentionsPreflight = text.Contains("preflight", StringComparison.OrdinalIgnoreCase) || text.Contains("预检", StringComparison.OrdinalIgnoreCase);
        var mentionsReview = text.Contains("review", StringComparison.OrdinalIgnoreCase) || text.Contains("复审", StringComparison.OrdinalIgnoreCase);
        return $"本地分析文档强调预检、Worker 封装和复审边界。preflight={mentionsPreflight}; review={mentionsReview}。";
    }

    private static string RenderMarkdown(ApiEvidenceReport report, SolidWorksApiFailureAnalysis analysis)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# cut_holes_failed API 证据报告");
        builder.AppendLine();
        builder.AppendLine($"- 失败阶段：`{report.FailureStage}`");
        builder.AppendLine($"- 目标操作：`{report.TargetOperation}`");
        builder.AppendLine($"- 选定策略：`{report.SelectedApiStrategy}`");
        builder.AppendLine();
        builder.AppendLine("## 当前调用方式摘要");
        foreach (var note in report.ImplementationNotes)
        {
            builder.AppendLine($"- {note}");
        }

        builder.AppendLine();
        builder.AppendLine("## 官方 API 证据");
        foreach (var source in report.OfficialApiSources)
        {
            builder.AppendLine($"- [{source.SourceName}]({source.SourcePathOrUrl})：{source.EvidenceSummary}");
        }

        builder.AppendLine();
        builder.AppendLine("## 参考 Skill 证据");
        if (report.LocalReferenceSources.Count == 0 && report.ThirdPartyReferenceSources.Count == 0)
        {
            builder.AppendLine("- 未发现可读取的本地参考仓库；本轮只保留官方 API 证据和既有中文分析文档要求。");
        }
        else
        {
            foreach (var source in report.LocalReferenceSources.Concat(report.ThirdPartyReferenceSources))
            {
                builder.AppendLine($"- `{source.SourceName}`：{source.EvidenceSummary} 许可说明：{source.LicenseNote}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## 候选 API");
        foreach (var candidate in report.ExtractedApiCandidates)
        {
            builder.AppendLine($"### `{candidate.ApiName}`");
            builder.AppendLine($"- 用途：{candidate.ApiPurpose}");
            builder.AppendLine($"- 选择状态：{candidate.RequiredSelectionState}");
            builder.AppendLine($"- 期望返回：{candidate.ExpectedReturn}");
            builder.AppendLine($"- 验证方式：{candidate.VerificationMethod}");
        }

        builder.AppendLine();
        builder.AppendLine("## 必须明确的事实");
        builder.AppendLine("- 用户第一份宏曾提示一草图轮廓方案，但本轮以第二份“拉伸后再切除”宏为准。");
        builder.AppendLine("- 当前证据结论：`FeatureExtrusion2` 创建板件基体，`FeatureCut4` 在活动孔草图中执行切除。");
        builder.AppendLine("- `CreateCircle` 是本轮宏证据中的孔圆创建方式，参数为圆心和圆上一点。");
        builder.AppendLine("- `FeatureCut4` 是 SolidWorks API 中用于创建 cut extrude feature 的方法。");
        builder.AppendLine("- `CreateCircleByRadius` 是 `SketchManager` 中用于按圆心和半径创建圆的方法。");
        builder.AppendLine("- `SelectByID2` 依赖对象名称、类型、坐标和选择状态，语言环境和选择状态可能导致失败。");
        builder.AppendLine("- 切除前必须确保草图处于正确状态，或正确选择草图轮廓。");
        builder.AppendLine("- 所有尺寸传入 SolidWorks API 时需要统一为米。");
        builder.AppendLine();
        builder.AppendLine("## 不直接复用第三方源码");
        builder.AppendLine("- 本轮只读取参考仓库结构和 API 名称，不复制 `scripts` 源码。");
        builder.AppendLine("- 第三方脚本不得作为生产执行路径，也不得绕过 Worker、Validator、Reviewer 和 QualityGate。");
        builder.AppendLine();
        builder.AppendLine("## 最终建议");
        builder.AppendLine(report.FinalRecommendation);
        builder.AppendLine();
        builder.AppendLine("## 搜索目标");
        foreach (var term in analysis.RecommendedApiSearchTerms)
        {
            builder.AppendLine($"- {term}");
        }

        return builder.ToString();
    }

    private static string RenderMacroRecordingRequest() =>
        """
        # SolidWorks 切孔宏录制请求

        如果 `cut_holes_failed` 仍然无法通过 C# late binding 稳定修复，请在 SolidWorks 中录制一个最小宏，并只把宏作为 API 调用顺序证据。

        ## 录制步骤

        1. 新建一个 Part。
        2. 创建 160 x 80 x 12 mm 板件。
        3. 选中板面或可用基准面。
        4. 绘制四个直径 10 mm 的圆。
        5. 执行贯穿切除。
        6. 保存宏。
        7. 将宏内容放入 `references/local/solidworks_macros/plate_cut_holes_recorded_macro.vba`。

        ## 使用边界

        - 不自动执行宏。
        - 不把宏作为生产路径。
        - 不复制第三方脚本。
        - 只提炼 API 调用顺序、选择状态和参数含义。
        """;

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true };
}
