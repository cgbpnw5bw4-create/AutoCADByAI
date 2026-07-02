---
name: markdown-docs-standard
description: "用于项目 `Markdown` 中文规范和可执行文档编写。适用于新增 `README`、`execution`、`failure_repair`、`api_evidence`、`review_checklist` 或审查说明。"
---

# Markdown 文档标准技能

## 适用范围

新增或修改 Markdown 文档时使用本技能。

## 规则

- 所有说明文字必须中文。
- 代码标识符、路径、命令、API 名称和配置键可以保留英文。
- 文档必须可执行、可复用，不写空洞说明。
- 每个文档必须包含目标、适用范围、执行步骤、验证标准、失败处理和禁止事项。
- Codex Agent 新规则必须优先写入 `docs/codex_agent_registry.md`、`docs/codex_agent_governance.md`、`docs/codex_agent_team_guide.md`、`docs/codex_execution_protocol.md`、`AGENTS.md` 或对应 Skill，不创建同职责重复 Agent。

## 验证

修改后运行 self-check，确认 `markdown_chinese_check_passed=true`。
