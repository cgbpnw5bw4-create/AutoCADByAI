# Codex Agent Team 使用说明

## 什么时候使用

当任务包含多个独立工作面，例如代码定位、API 查证、最小修复、质量审查和文档更新时，可以使用 Codex Agent Team。小型单文件修改不需要 spawn agents。

## 三类对象边界

- `Codex Agent` 位于 `.codex/agents`，用于开发协作。
- `Runtime Agent` 位于项目代码中，用于产品运行时流程。
- `Module` 位于 `src/Modules`，用于封装业务能力。

`Codex Agent Team` 不能替代 `src/Modules`，不能绕过 Worker，不能直接执行真实 CAD。

## 标准角色

- `project_manager`：只读拆解任务、分派和汇总。
- `code_mapper`：只读扫描入口文件、调用链、测试和报告。
- `api_researcher`：只读查证 API、SDK、COM 和 evidence。
- `cad_worker`：小范围实现 Worker、Skill、Validator、Reviewer 修复。
- `quality_gate`：只读审查边界、self-check 字段和测试。
- `docs_writer`：只修改 Markdown 文档，保持中文规范。

## SolidWorks API 失败流程

1. `code_mapper` 读取最新 `diagnostic_report.json`。
2. `project_manager` 判断 `failure_stage`。
3. `api_researcher` 读取 `api_evidence.md`、官方 API、本地参考资料和 evidence。
4. `cad_worker` 只做最小 Worker 封装修复。
5. `quality_gate` 审查是否破坏边界和安全开关。
6. `docs_writer` 更新说明文件和失败 playbook。

## 示例提示词

```text
请 project_manager 只读拆解 V1.0-B-REPAIR 的 cut_holes_failed 修复任务，先读取 AGENTS.md、docs/index.md 和 SolidWorks 模块文档，不要修改文件。
```

```text
请 api_researcher 只读分析最新 diagnostic_report.json 中的 failure_stage，读取 SolidWorks api_evidence.md，并输出候选 API 证据，不要复制第三方 scripts。
```

## 禁止事项

- 不要让多个可写 Agent 同时大范围修改代码。
- 不要让 Codex Agent 直接执行真实 CAD。
- 不要让 Codex Agent 修改 `../reviewrep`。
- 不要用 Codex Agent 替代 Runtime Agent 或 Module。
