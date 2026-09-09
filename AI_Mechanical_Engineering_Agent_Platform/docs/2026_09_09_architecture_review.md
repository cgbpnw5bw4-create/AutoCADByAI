# 2026-09-09 架构审查与 V2.1-B 可靠性补强

## 目标与适用范围

本轮审查从请求输入、工作流执行、人工审批、运行时协作、CAD 证据到验收报告，识别会使错误请求继续执行、流程状态丢失或验证结论失真的缺口。开发范围定为 **V2.1-B 可靠性补强**，本轮优先修复输入、重试、审批和自检报告五项问题，并支持自检输出定向；不进入 V2.1-C。

总体架构继续保留 `Gateway → chief-engineer → WorkflowEngine → Worker → Validator → Reviewer → QualityGate`。六个 canonical 开发 Agent 足以覆盖审查与开发职责。接下来的重点是补齐这些边界之间的状态、数据与证据合同。

## 输入与输出

输入为当前工作区源码、项目及模块执行协议、三个并行审查职责的已确认发现、当前阶段说明和既有技术债。输出为下面的修复清单、开发顺序、对应回归要求，以及本轮执行完成后回填的构建、测试和自检记录。既有审查报告与真实 CAD 证据只用于查证，不能改写为本轮验证结果。

以下路径行号是**审查前定位**，用于说明问题的原始触发位置；修复后行号可能变化。状态“本轮修复”表示已纳入当前开发范围，完成结论以文末验证记录为准。

## 按流程步骤列出的已确认问题

| 编号与优先级 | 流程步骤及触发条件 | 影响与源码定位 | 修复或后续验收要求 |
|---|---|---|---|
| `R01 / P1` | 请求解释：显式 `cad_model_spec_json` 或 `parameter_update_json` 为坏 JSON | [SolidWorksWorkflowRouter.cs](../src/PlatformCore/SolidWorksWorkflowRouter.cs) 第 203–243 行吞掉解析错误并返回 `null`；第 25–32 行可能将模型替换为默认四孔板，且 `dry_run` 默认关闭。更新请求也可能被静默忽略 | **本轮修复**：区分“没有提供”与“提供但无效”；后者在 Worker 前返回可行动失败，禁止以默认模型执行。覆盖坏 JSON、`null` 和缺少必要结构等反例 |
| `R02 / P1` | 质量打回：步骤声明的 `MaxRetries` 小于全局策略上限 | [SequentialWorkflowEngine.cs](../src/PlatformCore/WorkflowEngine/SequentialWorkflowEngine.cs) 第 109 行只把局部上限写入报告，第 143 行是否重试仅由全局策略决定；实际次数可能超过步骤声明 | **本轮修复**：实际与报告统一使用 `min(step.MaxRetries ?? global.MaxRetries, global.MaxRetries)`；`0` 表示首次失败后不重试。覆盖步骤上限更小、相等、更大和未设置 |
| `R03 / P1` | 人工审批：提交时 `CancellationToken` 已取消 | 同一文件第 68 行 `TryTake` 先移除待审批项，第 73 行才检查取消；未执行审批却丢失继续处理入口 | **本轮修复**：预取消检查必须在消费审批项前；预取消保留待审批请求、已完成步骤与审计，随后合法提交仍可处理。执行中取消的持久恢复不属于已完成能力 |
| `R04 / P2` | 审批恢复：同一工作流先后出现两个或更多人工审批步骤 | 同一文件第 204–212、279–287 行再次等待时仅保存恢复段；返回值拼接历史但未把完整历史保存回等待项。第 287、325 行也未包含本次提交及决定审计 | **本轮修复**：每次暂停累计保存完整步骤历史和审计；批准、拒绝、要求修订都记录本次提交与决定，最终结果不得遗漏前序步骤或重复审计 |
| `R05 / P1` | 阶段自检：孔样例为 `{}`、模型为 `null`、读取或反射执行失败 | [V21BHoleSelfCheck.cs](../src/PlatformCore/V21BHoleSelfCheck.cs) 第 33–34 行异常未完全隔离，第 64–65 行捕获后只写布尔失败而丢失原因；第 77 行把布尔值合取命名为测试通过。[PlatformSelfCheckRunner.cs](../src/PlatformCore/PlatformSelfCheckRunner.cs) 第 223 行调用后直到第 921 行才写最终报告，异常可能导致新报告缺失 | **本轮修复**：逐样例保留失败阶段、直接原因、来源及下一步验证；允许其他检查继续并输出本次失败报告；通过 `2.1-b-reliability` schema 将内置检查组、未观测能力及实际测试结果分开，消费方须按下文迁移 |
| `R06 / P2` | 外部宿主：任务执行或进入人工审批 | [AgentMessageDispatcher.cs](../src/Interfaces/AgentGatewayHost/AgentMessageDispatcher.cs) 第 29、46 行创建任务并执行，但未同步后续任务状态；[Program.cs](../src/Interfaces/AgentGatewayHost/Program.cs) 第 22–30 行只提供 Agent 查询和消息入口，无审批提交入口 | **下一批**：统一任务生命周期、状态查询及宿主审批提交，绑定请求身份、工作流和具体审批步骤；覆盖完成、失败、等待、拒绝、恢复及跨审批重放。持久化能力必须单独验收 |
| `R07 / P2` | 内部协作：第二个 Agent 需要第一个 Agent 的结构化输出 | [ChiefEngineerOrchestrator.cs](../src/Modules/RequirementUnderstanding/agents/ChiefEngineerOrchestrator.cs) 第 50 行为各 Agent 使用原始上下文；[InternalAgentWorkflowStep.cs](../src/PlatformCore/WorkflowEngine/InternalAgentWorkflowStep.cs) 第 115–123 行只补尝试次数与标识，未交接上游业务结果 | **下一批**：建立有类型、可追踪的步骤输入输出合同，让下游读取通过门禁的上游结果；失败或拒绝结果不得当作有效输入继续执行 |
| `R08 / P1` | 阵列与证据绑定：空种子引用、多种子文本或被绑定清单遗漏的校验规则变化 | [PatternParameterRules.cs](../src/Workers/SolidWorks/Features/Pattern/PatternParameterRules.cs) 第 25 行空引用跳过绑定；[RealSolidWorksFeatureAdapter.cs](../src/Workers/SolidWorks/Features/RealSolidWorksFeatureAdapter.cs) 第 581–610 行拆出多个种子；[FeatureExecutionEvidencePolicy.cs](../src/Workers/SolidWorks/Features/FeatureExecutionEvidencePolicy.cs) 第 18–47 行绑定清单未覆盖 `PatternParameterRules.cs` 和 `DomainSchemas/EdgeSelection.cs` | **后续优先修复并重采证据**：统一单种子档案与图依赖，补齐影响执行的源码绑定；多种子不得复用单种子证据。承接 [技术债](technical_debt.md) `HIGH-001` |
| `R09 / P1` | 阵列方向：拓扑边方向与声明轴反向，尤其非整周圆周阵列 | [RealSolidWorksFeatureAdapter.cs](../src/Workers/SolidWorks/Features/RealSolidWorksFeatureAdapter.cs) 第 543–547 行用 `Abs(dot)` 去掉符号，第 384–388、437–439 行翻转恒为 `false`；错误落点仍可能有正确体积 | **后续优先修复并重采证据**：使用带符号方向判据，验证同向、反向、非整周和独立落点；结合轴心位置验收。承接技术债 `HIGH-002`、`MEDIUM-001` |
| `R10 / P1（能力准入条件）` | 显式孔真实执行：试图把四类 dry-run 孔增强视为已有真机能力 | 同一 Adapter 第 736–744 行仍将四种显式孔策略置于 `Unverified`；[SolidWorksGeometryReader.cs](../src/Workers/SolidWorks/Features/SolidWorksGeometryReader.cs) 第 65–86 行没有提供逐孔 `MeasuredGeometry.Holes`。[SolidWorksArtifactValidator.cs](../src/Modules/CADModeling/validators/SolidWorksArtifactValidator.cs) 第 433–435 行在三个识别标记均缺失时跳过孔专项检查 | **保留当前前置阻断**：先完成一个明确参数档案的普通孔、真实逐孔 Reader 与产物校验反例，再独立采集同次诊断和主流程证据；此项是待完成能力，不能声称当前已发生真实执行绕过 |
| `R11 / P1` | CAD 证据验收：读取已提交的 7 组 `v2_1_b_refresh` 证据 | 7 个 `model.SLDPRT` 实际长度均比同组报告的 `size_bytes` 大 `4096` 字节；该不一致已存在于 `HEAD=ab63fbd`。[FeatureExecutionEvidencePolicy.cs](../src/Workers/SolidWorks/Features/FeatureExecutionEvidencePolicy.cs) 第 320–323 行正确拒绝物理文件与报告不匹配，三圆切除及 plate 能力也受到连带影响 | **后续首要行动**：恢复来源可信、文件与报告完整一致的 CAD 证据，必要时重新采集同次诊断和主流程产物。保留当前失败；禁止只修改 `size_bytes`、文件或源码 hash 制造通过 |

## 既有证据不一致的核验记录

本轮只读核验确认：以下目录中的 `model.SLDPRT` 工作区二进制与 `HEAD=ab63fbd` 相同，同组报告也与 `HEAD` 相同。每个实际文件都比报告声明多 `4096` 字节。通用执行源码绑定 hash 在工作区、`HEAD` 和这批 evidence 中均为 `e97ed88693b4066001fe6033d211e30121b37f99a7f0ffb5c8dec267f09aed09`。这证明该文件与报告不一致不是本轮源码修改引入；形成原因尚未查明，不能推测为某种保存、格式转换或环境行为。

下表目录均位于 `evidence/solidworks/v2_1_b_refresh/`；报告链接可直接定位同组证据。数值单位为字节。

| 目录与报告 | 报告 `size_bytes` | `model.SLDPRT` 实际长度 | 实际减报告 |
|---|---:|---:|---:|
| [20260907_013221_3210868](../evidence/solidworks/v2_1_b_refresh/20260907_013221_3210868/feature_execution_report.json) | 72843 | 76939 | +4096 |
| [20260907_013408_5080299](../evidence/solidworks/v2_1_b_refresh/20260907_013408_5080299/feature_execution_report.json) | 88345 | 92441 | +4096 |
| [20260907_013423_5400787](../evidence/solidworks/v2_1_b_refresh/20260907_013423_5400787/feature_execution_report.json) | 76621 | 80717 | +4096 |
| [20260907_013438_4901315](../evidence/solidworks/v2_1_b_refresh/20260907_013438_4901315/feature_execution_report.json) | 76466 | 80562 | +4096 |
| [20260907_013451_0073586](../evidence/solidworks/v2_1_b_refresh/20260907_013451_0073586/feature_execution_report.json) | 80862 | 84958 | +4096 |
| [20260907_013503_7710789](../evidence/solidworks/v2_1_b_refresh/20260907_013503_7710789/feature_execution_report.json) | 70136 | 74232 | +4096 |
| [20260907_013514_0906527](../evidence/solidworks/v2_1_b_refresh/20260907_013514_0906527/feature_execution_report.json) | 74054 | 78150 | +4096 |

门禁在这一条件下失败符合当前实现，完整测试与全局自检须保留实际失败。恢复标准是可信来源、原始文件、报告、源码与参数档案绑定、独立几何检查和同次主流程结论全部一致；仅把报告长度改成当前文件长度不构成证据恢复。

## 架构下一步的执行顺序

1. **完成本轮 V2.1-B 可靠性补强**：关闭 `R01`–`R05`，把自检报告写入指定输出目录。每项同时具备可复现反例、最小修复和回归结果。
2. **优先恢复可信完整的 CAD 证据**：处理 `R11`，先确认原始产物和报告的可信来源；无法完整恢复时重新采集。保留现有报告与失败事实；证据恢复前，三圆切除及 plate 不得被标记为可真实执行。
3. **补齐任务生命周期、宿主审批与类型交接**：实施 `R06`、`R07`，先统一任务及工作流状态合同，再接宿主入口与上游结果交接；补具体审批步骤身份、跨审批重放保护和持久恢复边界。
4. **修复阵列绑定与方向并重采证据**：实施 `R08`、`R09`；先通过不依赖 COM 的输入、方向、位置反例，再在隔离 Runner 中重新取证，最后恢复对应精确档案的主流程验收。前述证据恢复不替代修改后重新采证。
5. **完成一个显式普通孔档案与真实逐孔 Reader**：闭合 `R10` 中的最小普通孔路径，逐孔读取孔径、深度、位置和孔面，缺值必须失败；同步补产物识别标记缺失的反例。最终通过仍来自同次主流程质量链与发布包。
6. **其他孔逐类型取证**：沉孔、沉头孔、攻丝孔分别完成自己的参数档案、独立几何/语义测量与同次证据。攻丝必须有真实螺纹语义证据；既有普通切除证据不能替代。

上述顺序是后续开发建议，不表示本轮已经实施全部项目，也不自动改变阶段编号或真实执行授权。

## 本轮字段迁移与能力边界

自检报告 `schema_version` 升为 `2.1-b-reliability`。新增 `workflow_step_retry_limit_enforced`、`workflow_cancelled_approval_preserved`、`workflow_multi_approval_history_preserved` 和 `structured_cad_input_fails_closed`，分别对应有效重试上限、预取消保留、累计审批历史和显式坏输入失败关闭。

孔检查汇总改为 `hole_self_check_group_passed`，删除旧的 `hole_feature_regression_tests_passed`。孔能力字段使用可空 `bool?`：`true` 表示该内置检查已观测通过，`false` 表示已观测失败，`null` 表示未完成所需观测；`hole_self_check_unobserved_capabilities` 和 `hole_self_check_issues` 提供未观测项与原因。检查组未完整执行时不能通过，组汇总也不代表 `dotnet test` 已运行。此为有意的 schema 迁移，消费方必须处理新字段、旧字段删除与 `null`，不能声称旧消费方完全兼容。

人工审批目前只补强**提交前已取消**时的无损行为；执行中取消后的持久恢复尚未完成。提交合同尚无具体审批步骤身份，跨审批重放仍待修复；累计历史与审计不等于具备完整幂等语义。

`--output` 只保证默认自检的持久产物写入指定输出根目录。内部临时夹具仍使用系统临时目录；显式调用的既有 smoke 流程不在本轮输出定向承诺内。

## 验证标准与本轮结果回填

从项目根目录运行，输出根目录应选择本轮新目录：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/20260909_reliability
```

| 验证项 | 必须提供的证据 | 本轮结果 |
|---|---|---|
| 编译 | 命令、退出码、警告与错误计数 | `dotnet build` 退出 `0`，`0` 错误、`0` 警告 |
| 完整测试 | 当前修改对应的实际执行总数、失败数与日志 | `498` 项：`490` 通过、`8` 失败、`0` 跳过，退出 `1`；见 [最终 TRX](../output/tests/20260909_reliability/verified_final.trx) |
| `R01`–`R05` 回归 | 非法请求无 Worker 调用、有效重试次数、预取消无损、连续审批历史完整、坏样例仍生成诊断报告 | 新增 `49/49` 测试通过；包含嵌套空节点、空参数、缺少更新上下文和自定义模拟产物目录；见 [验证汇总](../output/tests/20260909_reliability/validation_summary.json) |
| 自检输出定向 | 默认自检的持久产物、指定目录的新 `reports/platform_self_check_report.json` 及默认持久路径未被本次定向命令写入的核查 | 已核验原 `output` 目录文件清单、大小和修改时间，变化数 `0`；见 [隔离记录](../output/tests/20260909_reliability/output_isolation_check.json)；内部临时夹具与显式旧 smoke 不属于此项承诺 |
| 阶段与中文文档 | 四个可靠性字段、`hole_self_check_group_passed`、可空孔能力字段与 `markdown_chinese_check_passed` | 全部为 `true`，孔 issues 和未观测列表为空；见 [本轮定向自检](../output/tests/20260909_reliability/isolated_platform_self_check_report.json) |
| 全局状态 | `final_status`、`failure_stage`、全部未满足项；不得只摘取阶段通过字段 | `final_status=Failed`、`v2_0_e_final_status=Failed`。证据门禁为 `feature_api_unverified`（`R11`），夹套真实能力继续冻结；未宣称整体就绪 |
| 真实 CAD | 本轮是否执行及证据范围 | 本文未提供新的真机取证，不据此扩大能力 |
| 保护范围 | `reviewrep` 与既有 evidence 的修改核查；区分既有变化与本轮写入 | 起始工作区干净；`reviewrep` 文件 SHA256 差异为 `0`，`git status --short -- ../reviewrep evidence` 无输出。没有修改真实绑定源码或证据，没有提交 |

最终完整测试命令为 `dotnet test --no-build --logger "trx;LogFileName=verified_final.trx" --results-directory output/tests/20260909_reliability --verbosity quiet`，在完整构建后执行。八个失败分别为五类复杂特征的证据检查、基础 Feature 证据检查、V2.0-D 候选检查，以及依赖生产证据的总自检测试；前七项直接报告模型与报告物理不一致，最后一项为对应能力字段断言失败。没有通过修改断言或产物长度来隐藏这些失败。

只读质量复审已核对本轮发现的必要修复，未发现本轮范围内剩余代码阻断。审查不代替真实 CAD 验收。下一步优先处理 `R11`：核实可信原始产物或重新采集完整且一致的证据，再运行上述证据测试与同次主流程验收。任何新增类型或阵列参数变更都仍需独立准入。

## 常见失败、修复入口与禁止事项

输入无效时定位 Router 的字段与失败阶段，修复请求后再执行，不得回退默认模型。审批失败先查待审批存储及审计链，不得通过重建空任务隐藏已丢失历史。孔自检失败按样例来源、异常类型和具体检查排查，不得使用旧报告或全字段置真代替本次结果。

真实 CAD 或 API 失败进入 [Worker 失败修复手册](../src/Workers/SolidWorks/failure_repair.md) 与对应特征 `api_evidence.md`；影响源码绑定或参数档案的改动必须重新采证。禁止改写 `reviewrep`、旧证据的运行标识或源码绑定；禁止为制造全局通过降低能力基线；禁止在自检、单元测试或 CI 启动真实 CAD；禁止绕过 Worker、Validator、Reviewer、QualityGate 或提前进入 V2.1-C。

证据物理长度不一致时保留原文件与报告并核查来源，禁止仅改 `size_bytes` 或 hash 使门禁通过。此次 7 组不一致的原因尚未查明，不能把推测写成事实，也不能把完整测试失败改述为通过。
