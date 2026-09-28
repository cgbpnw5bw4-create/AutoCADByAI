# 项目级 Codex 工作规则

## 项目定位

项目正式名称为 `AI Mechanical Engineer Platform`。平台面向机械设计、CAD 自动化、`SolidWorks`、`AutoCAD` 及后续工业软件适配，采用少量强 Agent 与多个确定性 Worker：强模型负责工程理解、规划和决策，平台负责工程约束、确定性执行、真实 CAD/API 能力和结果验收。

保留 `AI_Mechanical_Engineering_Agent_Platform` 底层目录及解决方案文件名，继续兼容现有构建命令、程序集、命名空间、工具路径和来源绑定证据；正式名称调整不授权全局替换这些稳定标识。当前阶段见 [V2.2-C CAD 基线恢复与阵列证据重采](docs/v2_2_c_cad_baseline_recovery.md)，保留 V2.2-B 架构成果；阶段恢复、完整测试与全局自检分别报告。

## 架构边界

- `Gateway` 只暴露 `chief-engineer`。
- `Internal Agent` 不能被 `Gateway` 直接调用。
- `Agent` 不能直接调用 `Worker`。
- `Gateway` 不能直接调用 `Worker`。
- `LLM` 不能直接调用 `Worker`。
- `Agent` 负责工程理解、规划和决策，`Skill` 封装可复用机械工程能力，`Worker` 负责确定性执行；不得仅为消息转发增加 Runtime Agent。
- 业务层通过 `ModelRuntime` / `IModelProvider` 接入模型，不绑定具体模型名称；模型输出的 API 名称或参数不能成为真实能力证据。
- 正式 CAD 准入链必须保持 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`；Handler 不得直接访问 COM。
- `QualityGate` 验收工程结果，新增专项 Gate 必须有真实检查依据；未实现的 Gate 不能返回占位通过。
- `Worker` 执行后必须进入 `Validator`、`Reviewer` 和 `QualityGate`。
- 本地交互式主流程默认启用真实 CAD；`dry_run=true`、`SW_DISABLE_REAL_EXECUTION=true`、CI、单元测试或 `SW_FORCE_FAKE_WORKER=true` 时必须关闭真实执行。

## 三类 Agent

- `Codex Agent` 位于 `.codex/agents`，只用于开发协作，不是产品运行时 Agent。
- `Runtime Agent` 位于项目代码中，例如 `chief-engineer`、`cad-modeler`、`drawing-reviewer`，用于产品运行时业务流程。
- `Module` 位于 `src/Modules`，用于封装业务能力，例如 `CADModeling`、`DrawingReview`、`CodeReview`。

`Codex Agent` 不能替代 `src/Modules`，禁止让 `Codex Agent` 绕过项目 `Worker`，禁止让 `Runtime Agent` 修改代码。

## Codex Agent 复用规则

`Codex Agent Team` 只能使用 `docs/codex_agent_registry.md` 登记的 canonical agents：`project_manager`、`code_mapper`、`api_researcher`、`cad_worker`、`quality_gate`、`docs_writer`。

执行任务前必须先检查 `.codex/agents/` 是否已有 canonical agent 覆盖当前职责。若只是职责扩展，不创建同职责新 Agent，必须把新要求写入对应 Skill 或 Markdown。`api_researcher` 新要求写入 `.agents/skills/solidworks-api-repair/SKILL.md` 或 `api_evidence.md`；`code_mapper` 新要求写入 canonical agent 或 `docs/codex_execution_protocol.md`；`quality_gate` 新要求写入 `.agents/skills/quality-review/SKILL.md` 或 `review_checklist.md`；`docs_writer` 新要求写入 `.agents/skills/markdown-docs-standard/SKILL.md` 或 `module_document_standard.md`。

active `.codex/agents/` 只能保留 canonical agents。发现重复 Agent 时不要盲删，先合并有价值指令，再移动到 `.codex/agents/archive/`；archive 中的 Agent 不作为 active agent 使用。确实需要新增 Agent 时，必须先更新 `docs/codex_agent_registry.md` 并说明现有 6 个 Agent 为什么无法覆盖。

## Markdown 规则

所有 Markdown 说明文字必须中文。允许保留英文的内容仅限代码标识符、路径、命令、API 名称、NuGet 包名、配置键和第三方许可证原文。

## Codex 执行前必须读取

每次修改前必须先读取：

- `docs/index.md`
- `docs/project_execution_standard.md`
- `docs/codex_execution_protocol.md`
- `docs/version_stage_index.md`

涉及模块时还必须读取该模块的：

- `execution.md`
- `failure_repair.md`
- `api_evidence.md`
- `review_checklist.md`

涉及 `SolidWorks` API 时必须先读取：

- `src/Modules/CADModeling/api_evidence.md`
- `src/Workers/SolidWorks/api_evidence.md`
- 最新 `diagnostic_report.json`

## 失败处理

失败后不能只写 `Failed`。必须写出 `failure_stage`、直接原因、证据来源、修复策略和下一步验证命令。API 失败必须进入 API Evidence Driven Repair Loop；真实 CAD 失败必须先在诊断 Runner 中隔离验证，再回填主 Worker。

## 适用范围、输入输出与执行步骤

本规则适用于项目开发、文档维护与验证。输入为用户目标、当前代码和审查记录；输出为最小改动、验证记录和剩余风险。先读取必读协议和涉及模块文档，再列出优先问题、实施改动并运行下列检查。历史审查与 API evidence 保留原始来源，禁止靠改名、改报告或弱化断言制造验证通过。

## 验证规则

每次修改后必须运行：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

如果命令无法运行，必须说明命令、失败原因、是否与本次修改有关，以及是否需要用户处理环境问题。
