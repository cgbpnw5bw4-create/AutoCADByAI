# 项目级 Codex 工作规则

## 项目定位

`AI_Mechanical_Engineering_Agent_Platform` 是面向机械设计、CAD 自动化、`SolidWorks`、`AutoCAD` 以及后续工业软件适配的多 Agent 平台。当前重点是平台边界、可验证工作流、真实 CAD 安全开关和可执行文档层。

## 架构边界

- `Gateway` 只暴露 `chief-engineer`。
- `Internal Agent` 不能被 `Gateway` 直接调用。
- `Agent` 不能直接调用 `Worker`。
- `Gateway` 不能直接调用 `Worker`。
- `LLM` 不能直接调用 `Worker`。
- `Worker` 执行后必须进入 `Validator`、`Reviewer` 和 `QualityGate`。
- 真实 CAD 默认关闭，必须同时通过请求级开关和环境变量确认。

## 三类 Agent

- `Codex Agent` 位于 `.codex/agents`，只用于开发协作，不是产品运行时 Agent。
- `Runtime Agent` 位于项目代码中，例如 `chief-engineer`、`cad-modeler`、`drawing-reviewer`，用于产品运行时业务流程。
- `Module` 位于 `src/Modules`，用于封装业务能力，例如 `CADModeling`、`DrawingReview`、`CodeReview`。

`Codex Agent` 不能替代 `src/Modules`，禁止让 `Codex Agent` 绕过项目 `Worker`，禁止让 `Runtime Agent` 修改代码。

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

## 验证规则

每次修改后必须运行：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

如果命令无法运行，必须说明命令、失败原因、是否与本次修改有关，以及是否需要用户处理环境问题。
