# V2.2-B 强模型架构增量优化

## 目标与适用范围

本阶段将项目正式定位和名称统一为 `AI Mechanical Engineer Platform`。核心原则是：**强模型负责工程理解、规划和决策；平台负责工程约束、确定性执行、真实 CAD/API 能力和结果验收。**

本轮在保留现有源码、测试、任务审批与受证 SolidWorks 能力的基础上增量改进，不重建项目结构，不改写历史审查或 API evidence，不将未来能力包装成已实现功能。

## 输入与输出

输入为当前工作区、项目和模块协议、既有测试、`../reviewrep` 历史审查以及 [2026-09-09 架构审查](2026_09_09_architecture_review.md)。当前仓库中的审查目录实际为 `../reviewrep`；用户所指审查记录按这个现有位置只读核查。

输出为五项优先问题、模型/工作流/门禁最小抽象、必要的运行职责收敛、统一中文文档和本轮独立验证记录。基线及最终测试、全局 self-check 与真实 CAD 验收分别裁决。

## 当前最值得处理的五项架构问题

| 问题 | 已确认事实与影响 | 本轮处理 |
|---|---|---|
| 定位与职责不一致 | 当前入口仍以多 Agent 为核心；默认机械路线固定调用多个确定性占位角色，部分旧文档仍称真实 Worker 未接入或默认关闭 | 正式名称和职责统一为少量强 Agent 与确定性 Worker；默认只做确定性规划输入校验，四角色路线保留为显式兼容入口 |
| 模型合同局限于兼容适配项目 | `IRuntimeModelClient` 接收 Microsoft 适配层配置，业务缺少独立中立模型入口 | 新增无供应商 SDK 依赖的 `ModelRuntime` / `IModelProvider`，旧接入以 `ConfiguredModelProvider` 兼容 |
| 闲置桥接入口扩大执行权限 | `ToolBridge.InvokeWorkerAsync` 可以直接调用 `IWorker`，与模型不得绕过正式执行链的约束冲突 | 删除直接 Worker 调用方法及持有的 Worker Registry，保留名称映射和旧构造兼容 |
| 调用方绑定顺序引擎具体类型 | 编排与宿主周边依赖 `SequentialWorkflowEngine`，未来替换调度策略时需要连带改调用方 | 新增 `IWorkflowEngine` 并改为接口依赖，保留当前顺序、重试、审批和恢复实现 |
| QualityGate 缺少统一专项组合边界 | 已有基础策略与领域检查，但没有统一的专项门禁合同、领域分类与收紧规则 | 增加 `IQualityGate`、`QualityGateDomain` 和现有 Gatekeeper 内的组合裁决，复用原质量链 |

事实边界：旧默认路线中的四个内部 Agent 是确定性模拟或占位实现，并非四次独立 LLM 推理。当前代码没有可据此宣称消除的大量内部 JSON 往返；本轮不虚构模型调用减少或性能收益。

## 本轮实际修改

### 中立模型运行时

新增 `src/ModelRuntime`。`IModelProvider.GenerateAsync` 接收 `ModelRequest` 和取消令牌；`ModelRequest` 只包含用途、系统提示与用户消息。`EngineeringModelPurpose` 限定需求理解、设计规划、特征规划、建模顺序、装配策略、恢复建议和工程决策。

`ModelRuntime.GenerateAsync` 在供应方调用前检查用途与输入，拒绝空结果并保持取消语义；合同不携带 Worker、Registry、Adapter 或 COM 对象。模型输出仍需平台校验，不因实现此接口就获得执行权限。

`ConfiguredModelProvider` 复用现有 `IRuntimeModelClient` 和环境配置；`MicrosoftRuntimeAgentInvoker` 经统一 ModelRuntime 调用。`AgentFactory` 和 `RuntimePlatformFactory` 支持注入 `IModelProvider`，旧客户端构造和 Mock 回退继续兼容。具体供应方名称与模型名称留在接入层，本轮没有为尚未需要的供应商添加多个客户端。

运行时重复配置先解包底层 Agent，供应方从 A 替换为 B 时不会叠加包装导致双调用；切回 Mock 恢复原 Agent。存在待审批任务时禁止重配以保护既有建议和恢复链。当前只支持空闲时配置替换，不宣称并发热切换。系统提示同步为工程理解、规划与决策，兼容 JSON 默认不固定推荐四个角色。

### 工作流接口与确定性执行

`IWorkflowEngine` 暴露 `ExecuteAsync`、`TryGetPendingHumanApproval` 和 `SubmitHumanApprovalAsync`，沿用当前步骤、上下文、执行结果及审批合同。`SequentialWorkflowEngine` 实现接口，平台 Kernel、编排、SolidWorks 主流程和兼容 Runtime 等调用方按接口接入。

当前支持顺序执行、既有 Retry 和 HumanApproval。`Parallel` 与 `Conditional` 保留为未来方向，本轮不增加未支持的模式枚举、空执行器或无检查的并行。真正的并行需先明确步骤状态隔离、取消、结果聚合与审批恢复，SolidWorks COM 仍保持全局串行。

`ToolBridge` 不再提供 `InvokeWorkerAsync`，也不持有 Worker Registry。旧双参数构造函数仅供源码兼容，名称映射与输出转换保留；正式 Worker 调用继续由平台工作流负责。

`ChiefEngineerOrchestrator` 未显式注入 `InternalWorkflowRoute` 时，仅运行 `engineering-plan-validation` 步骤，由 `EngineeringPlanValidationStep` 校验规划输入。非空需求或 Router 识别的明确 CAD 意图可以继续，空需求失败；无效结构化 JSON 仍在工作流开始前拒绝。这个步骤不生成工程计划、不调用模型、内部 Agent、Worker 或 API，消息明确其验证边界；具体 CAD 建模计划继续由原 Skill/Validator 生成和校验。

默认协作报告的 `CalledAgents` 和 `AgentOutputs` 为空，单步骤 `AgentId` 为空，不以占位 Agent 输出证明工程计划已完成。显式 `InternalWorkflowRoute.EngineeringDefault` 仍运行原四角色路线并保留重试/审批语义；`PlatformBootstrapper.CreateDefault` 新增可选 `internalWorkflowRoute` 参数，未提供时使用新默认。

### 可扩展质量门禁

`IQualityGate` 定义 `Name`、`Domain` 和 `Evaluate(ReviewReport)`，`QualityGateDomain` 提供 `General`、`Geometry`、`APIEvidence`、`Assembly`、`Drawing`、`Manufacturability`。`GateDecisionPolicy` 实现该合同并保留旧 `Decide` 方法。

`DefaultGatekeeper` 保留旧构造并接受附加门禁，始终运行基础策略；独立领域门禁组合优先级为 `Failed > Rejected > NeedsHumanApproval > Passed`，附加门禁只能收紧，不能用人工审批放行另一领域的证据或几何拒绝。单一 `GateDecisionPolicy` 内部既有优先级保持不变。`GateEvaluationResult.Checks` 保存每项领域裁决。异常、空裁决和未知结果失败关闭，重复名称或无效领域在配置时拒绝。

未来 `GeometryQualityGate`、`APIEvidenceQualityGate`、`AssemblyQualityGate`、`DrawingQualityGate`、`ManufacturabilityQualityGate` 可以实现该合同。现有几何、API evidence 和工程图检查不迁移或重写；本轮没有创建五个返回占位通过的类，也未完成新的装配或制造性算法。

## 项目改名范围与底层兼容

| 范围 | 处理 | 原因 |
|---|---|---|
| 仓库与项目 README、当前文档入口、AGENTS 定位、架构和职责规范 | 正式名称统一为 `AI Mechanical Engineer Platform` | 对外和开发定位一致 |
| 当前阶段、执行协议、版本索引、兼容 Runtime README | 同步新职责与实际实现边界 | 避免文档继续指导错误路径 |
| CLI 自检展示与 API Host 服务展示名称 | 使用新正式名称 | 用户可见入口一致，不改变命令或接口路径 |
| 底层目录、`.sln`、程序集、命名空间、稳定类型与注册键 | 保留原名 | 兼容构建、工具、反射加载及已有依赖 |
| `AgentRuntime.Microsoft` 项目名和旧 Runtime 模式/配置键 | 保留并说明兼容定位 | 独立模型合同已分离，无需以破坏配置完成展示改名 |
| 历史阶段文档、审查记录、CAD evidence 和来源绑定路径 | 保留当时内容 | 名称变化不能改写历史来源或验收事实 |

## 修改后的核心架构

```text
Frontier Model / Future AGI
→ Engineering Agent
→ Engineering Plan
→ Deterministic Workers
→ Verified CAD/API Layer
→ SolidWorks
→ QualityGate
```

`ModelRuntime` 负责接入和切换，Agent 负责工程理解、规划和决策，Skill 负责可复用能力，Registry 负责发现和映射。当前建模计划仍由 `CADModelSpec`、`FeatureGraph` 与 `SolidWorksBuildPlan` 承载，没有为展示方向复制一套并行计划 Schema。

本轮没有新增自然语言自动转换为正式工程计划的完整闭环。模型建议和默认输入校验通过不意味着已生成可执行计划；这一能力应在下一阶段选择明确用例后，接入既有 `CADModelSpec → FeatureGraph → SolidWorksBuildPlan` 并验证约束和拒绝路径。

正式能力准入链保持 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`。这表达能力发现、验证、证据授权和受控执行责任；既有实际调用和证据复核位置保持不变。API Evidence 必须来自真实 API 验证、参数档案、源码/环境与物理产物绑定，模型生成的 API 名称和参数不能成为能力证据。

## 删除或合并的冗余层

实际删除闲置 `ToolBridge.InvokeWorkerAsync` 及其 Worker Registry 字段，阻止模型适配层形成另一条 Worker 执行入口。保留已有 Skill 调用、工具映射与旧构造，不通过删除稳定 Adapter、Worker 或门禁来缩短图示。

默认工程流程合并原四个确定性占位角色调用为一个规划输入校验步骤，减少没有实际工程交接的固定调度、重复状态包装与模拟产物。原角色、显式四角色路线和审批/重试能力保留，减少的不是四次真实模型调用，也没有声称已完成通用工程计划生成。

## 保留未修改部分及原因

保留已有 Agent、Skill、Worker、Module、Registry 与 Contracts 的基础边界；保留 `FeatureHandlerRegistry`、Verified Handler、SolidWorks API Evidence 与 Adapter 的受控链。来源绑定的真实 CAD 代码和证据不为接口美化而改动，降低损伤已验证能力的风险。

保留 V2.2-A 的任务访问令牌、具体审批身份、原子防重放和恢复后的原业务收尾。任务与审批仍为单进程内存，未增加持久化、后台队列或跨重启恢复。保留现有模型输出映射、错误分类、超时、取消和配置兼容行为。

本轮不处理历史审查中的阵列方向、单种子绑定、四类显式孔真实取证、逐孔 Reader 与物理证据不一致；它们需要专门范围和新证据，不能通过平台抽象消除。

## 执行步骤与验证记录

执行顺序为：只读审查并保存基线；列五项优先问题；完成必要接口和接线；更新中文文档；运行完整构建、所有测试和定向自检；对照基线记录新增回归及已知失败；核验历史审查与 CAD evidence 未被改写。

本轮独立输出目录为 `output/validation/v2_2_b_frontier/`。文档职责不重复编译与测试，以下结果只依据执行代理的实际日志回填。

| 检查 | 当前记录 | 证据 |
|---|---|---|
| 修改前完整测试 | 总数 551，通过 543，失败 8 | [基线日志](../output/validation/v2_2_b_frontier/baseline.log)、[基线 TRX](../output/validation/v2_2_b_frontier/baseline/baseline.trx) |
| 修改后完整构建 | 通过，0 警告、0 错误 | [构建日志](../output/validation/v2_2_b_frontier/build.log) |
| 修改后完整测试 | 总数 591，通过 583，失败 8，跳过 0；净增 40 项，新增失败 0 | [完整测试日志](../output/validation/v2_2_b_frontier/full_test.log)、[完整 TRX](../output/validation/v2_2_b_frontier/full/full.trx) |
| 修改后定向 self-check | 退出码 2，全局 `Failed`；本轮字典四项均为 `true`，V2.2-A 四项均为 `true` | [自检日志](../output/validation/v2_2_b_frontier/self_check.log)、[自检报告](../output/validation/v2_2_b_frontier/reports/platform_self_check_report.json) |
| Markdown 中文检查 | `markdown_chinese_check_passed=true`，扫描 91 份，全部通过 | [中文检查报告](../output/validation/v2_2_b_frontier/reports/markdown_language_report.json) |
| 历史审查保留检查 | 44 个文件的 SHA-256 与修改前完全一致，文件集合一致 | [验证汇总](../output/validation/v2_2_b_frontier/verification_summary.json) |
| CAD 源码及 evidence 保留检查 | `Workers/SolidWorks`、`Modules/CADModeling`、`DomainSchemas`、`evidence` 与能力基线均无差异 | 同上 |

修改前 8 项失败与既有 CAD 证据有关，直接原因包含 `diagnostic model artifact does not physically match its report`。这已经是本轮修改前的失败，不能写成新模型运行时造成，也不能因它是已知问题就跳过完整测试或标记全绿。

修改后 8 个失败名称与基线完全相同：五类复杂特征 evidence、基础 Feature evidence、V2.0-D 候选 evidence，以及依赖相关生产能力字段的总自检测试。前七项直接报告物理产物不匹配，最后一项为对应能力断言失败。原固定四角色测试随默认行为变更改为默认输入校验测试，并增加显式四角色兼容回归；既有流程并未通过删除断言或跳过测试被隐藏。

全局自检中的 `v2_0_e_final_status` 仍为 `Failed`，能力差异字段为 `feature_production_evidence_active`、`unverified_feature_blocks_execution`、`v2_0_d_production_evidence_active`、`v2_0_e_controlled_plate_evidence_active`、`v2_0_e_step_content_gate_active`；`jacket_real_workflow_supported` 与 `jacket_production_evidence_active` 均为 `false`。本轮没有启动真实 CAD，也没有获得新的真机能力验收结论。

完整测试改写的四份已跟踪模拟 `build_report.json` 已单独保存在本轮 `generated_test_reports/`，随后按修改前已确认的 HEAD 内容及原检出换行规则恢复。验证产物与历史证据分开保留，不以工作区输出时间变化制造源码或 evidence 差异。

自检沿用 `2.2-a-task-approval` schema，增加 `frontier_architecture_checks` 字典，包含 `model_provider_contract_executed`、`default_planning_route_consolidated`、`quality_gate_extension_blocks_approval_bypass`、`workflow_contract_preserves_gate_blocking` 四项行为。旧内部协作字段通过显式四角色兼容路线验证，保持原有语义和能力基线；新默认单步骤另行检查。字典全通过不等于完整测试或全局 self-check 通过。

验证命令：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/v2_2_b_frontier
```

## 当前风险、常见失败与禁止事项

已知 CAD 证据物理不一致继续影响全局能力检查，必须保持失败关闭。新增模型合同只约束接入用途和结构，不能保证模型建议工程上正确；正式执行仍依赖计划校验、来源 evidence、Worker 和结果验收。旧兼容宿主的模式和配置仍在 Microsoft 适配层，跨重启任务恢复、通用工程步骤数据交接、并行/条件执行以及装配/制造性专项验收未完成。

遇到接口行为、取消、审批或门禁回归时先保留具体反例并做最小修复；遇到 CAD evidence 失配按模块 `failure_repair.md` 核验来源，必要时重采同次诊断与主流程证据。禁止改 `size_bytes`、hash、旧报告或测试断言制造通过，禁止降低能力基线，禁止启动本轮不需要的真实 CAD 或修改只读 `reviewrep`。

## 下一阶段最安全的开发建议

先恢复来源可信、物理一致的现有 CAD 证据，必要时按既有指纹算法重新采集同次诊断和主流程产物，再修复阵列单种子绑定及带符号方向并重新验证。平台方向优先选一个真实工程用例，建立通过门禁的结构化步骤输出到下游的类型合同；之后再决定持久化或条件执行的必要范围。

新的模型供应方以 `IModelProvider` 替身和同一工程用例对照接入，真实模型只产出计划建议。整机方向先选有限零部件和明确装配约束，补齐装配 evidence 与独立验收后扩展 `AssemblyQualityGate`；不同时堆叠多个 Agent、通用工作流语法和无证据 CAD 能力。
