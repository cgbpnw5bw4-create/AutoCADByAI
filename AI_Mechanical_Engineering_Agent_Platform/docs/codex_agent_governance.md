# Codex Agent 治理规则

## 目标

Codex Agent Team 必须复用 canonical agents，避免每轮任务创建同职责重复 Agent。新要求优先写入已有 Skill 或 Markdown，使执行规则可复用、可审查、可测试。

## 去重规则

- 不允许重复创建同职责 Agent。
- active `.codex/agents/` 中只能保留 `docs/codex_agent_registry.md` 登记的 canonical agents。
- `.codex/agents/archive/` 只用于保存历史或重复 Agent，不作为 active agent 使用。
- 扫描发现重复 Agent 时，先判断是否与 canonical agent 职责重复，不要盲删。
- 重复 Agent 中有价值的新指令必须先合并到 canonical agent、对应 Skill 或 Markdown。
- 合并后重复文件可以移动到 `.codex/agents/archive/`。

## 新要求沉淀位置

| 需求来源 | 优先沉淀位置 |
|---|---|
| `api_researcher` 新要求 | `.agents/skills/solidworks-api-repair/SKILL.md` 或 `src/Workers/SolidWorks/api_evidence.md`、`src/Modules/CADModeling/api_evidence.md` |
| `code_mapper` 新要求 | canonical `code_mapper` 配置或 `docs/codex_execution_protocol.md` |
| `quality_gate` 新要求 | `.agents/skills/quality-review/SKILL.md` 或 `src/Workers/SolidWorks/review_checklist.md` |
| `docs_writer` 新要求 | `.agents/skills/markdown-docs-standard/SKILL.md` 或 `docs/module_document_standard.md` |
| 跨角色执行规则 | `docs/codex_agent_team_guide.md`、`docs/codex_execution_protocol.md` 或 `AGENTS.md` |

## 执行前检查

每轮任务开始前必须检查：

1. 是否已有 canonical agent 覆盖当前职责。
2. active `.codex/agents/` 是否只包含 canonical agent 文件。
3. 是否存在同职责重复文件。
4. 新要求是否可以写入对应 Skill 或 Markdown。

如果只是职责扩展，不创建新 Agent。只有明确证明现有 6 个 Agent 无法覆盖时，才更新 registry 并新增 Agent。

## V1.1 当前扫描结论

本轮扫描 active `.codex/agents/`，发现 6 个 canonical agent 文件：`project-manager.toml`、`code-mapper.toml`、`api-researcher.toml`、`cad-worker.toml`、`quality-gate.toml`、`docs-writer.toml`。未发现同职责重复 Agent，因此没有归档文件。
