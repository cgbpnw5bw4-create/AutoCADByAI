# Module 标准

Module 是完整能力板块，不是脚本文件夹。

每个 Module 应包含：

- `README.md`：说明职责、边界和当前成熟度。
- `module.yaml`：描述名称、版本、能力和注册组件。
- `agents/`：模块拥有的 Agent 实现或适配器。
- `skills/`：结构化转换和辅助能力。
- `workers/`：仅在模块拥有外部执行面时放置执行适配。
- `validators/`：确定性校验。
- `reviewers/`：生成 `ReviewReport` 的复审逻辑。
- `schemas/`：模块专用 Schema 扩展。
- `tests/`：模块级行为和契约测试。

模块之间应通过平台 Contracts 和 `DomainSchemas` 通信。核心任务数据不得只靠自然语言传递。
