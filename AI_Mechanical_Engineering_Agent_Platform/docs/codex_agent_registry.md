# Codex Agent 注册表

## 目标

本注册表定义项目唯一允许的 canonical Codex Agents。执行任务时必须优先复用这些 Agent，不得为同一职责反复创建新的 `api_researcher`、`code_mapper`、`quality_gate`、`docs_writer` 或等价角色。

## Active canonical agents

| canonical agent | active 文件 | 权限 | 职责 |
|---|---|---|---|
| `project_manager` | `.codex/agents/project-manager.toml` | `read-only` | 拆解任务、分派角色、识别风险、汇总结果。 |
| `code_mapper` | `.codex/agents/code-mapper.toml` | `read-only` | 定位代码入口、调用链、测试、报告和现有实现边界。 |
| `api_researcher` | `.codex/agents/api-researcher.toml` | `read-only` | 查证官方 API、本地 evidence、SDK、COM 和只读参考资料。 |
| `cad_worker` | `.codex/agents/cad-worker.toml` | `workspace-write` | 在证据充分后做最小 Worker、Skill、Validator、Reviewer 和测试修改。 |
| `quality_gate` | `.codex/agents/quality-gate.toml` | `read-only` | 审查边界、安全开关、self-check、测试和文档同步。 |
| `docs_writer` | `.codex/agents/docs-writer.toml` | `workspace-write` | 维护中文 Markdown、执行协议、失败修复和审查清单。 |

## Active 目录规则

`.codex/agents/` 中只能保留上表 6 个 canonical agents。归档目录 `.codex/agents/archive/` 中的文件不作为 active agent 使用。

## 新增 Agent 例外

只有现有 6 个 canonical agents 无法覆盖新的长期职责时，才允许新增 Agent。新增前必须先更新本注册表，说明职责缺口、为什么不能写入现有 Skill 或 Markdown，以及新 Agent 的权限边界。
