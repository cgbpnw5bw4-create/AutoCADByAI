# Claude 审查协议

Claude 审查必须只读项目源码。唯一允许写入的位置是 `../reviewrep`，用于保存新的审查报告。

## 审查前必须读取

- `docs/claude_review_protocol.md`
- 当前模块的 `execution.md`
- 当前模块的 `failure_repair.md`
- 当前模块的 `api_evidence.md`
- 当前模块的 `review_checklist.md`
- 当前阶段说明
- `output/reports/platform_self_check_report.json`
- 相关 `reviewrep` 历史审查日志

## 审查内容

- 对照任务成功标准。
- 对照 self-check 字段。
- 对照模块 review checklist。
- 检查是否破坏 Gateway、Agent、Skill、Worker、Validator、Reviewer、QualityGate 分层。
- 检查是否默认执行真实 CAD。
- 检查是否复制第三方脚本。
- 检查 Markdown 中文规范。

## 输出要求

审查报告必须中文，必须列出 Blockers、Improvements、测试缺口、是否可以进入下一阶段。Claude 不得修改项目源码，不得覆盖已有审查报告。
