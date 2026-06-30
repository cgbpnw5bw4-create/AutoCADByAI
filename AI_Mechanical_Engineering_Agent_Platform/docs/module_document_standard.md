# 模块文档标准

每个 `Module` 必须有可执行说明文件。文档不是展示材料，而是 Codex 和 Claude 后续执行、修复、审查的协议。

## `README.md`

必须说明模块定位、当前版本能力、不做什么、输入输出、与其他模块关系。

## `execution.md`

必须说明 Codex 执行该模块任务前要读取的材料、阶段步骤、安全开关、self-check 字段和成功标准。

## `failure_repair.md`

必须列出常见失败类型、`failure_stage` 映射、自动修复流程，以及什么时候需要用户提供额外证据。

## `api_evidence.md`

必须说明需要查证的 API、官方资料优先级、本地参考资料优先级、第三方参考仓库使用边界，以及不得直接复制源码。

## `review_checklist.md`

必须提供 Claude 审查清单，明确 Blockers、Improvements、是否可以进入下一阶段。

## 维护要求

当代码流程、失败类型、self-check 字段或安全开关变化时，必须同步更新模块文档。文档缺失时不能声称该模块已完成。
