# SolidWorks Worker 审查清单

## 必查项

- Worker 是否受请求级安全开关和环境变量保护。
- Worker 是否只通过平台调用。
- 真实执行是否默认关闭。
- 是否有 `build_report.json`。
- 是否有 `diagnostic_report.json`。
- 是否有可行动 `failure_stage`。
- 是否有 repair loop。
- 是否有 API evidence。
- 是否有 artifact validation。
- 是否没有 Agent、Gateway、LLM 直接调用 Worker。
- 是否没有 COM 类型泄漏到 Contracts。
- 是否没有复制第三方 scripts。
- 是否没有破坏 `FakeSolidWorksWorker` dry-run。

## Blockers

默认启动 SolidWorks、真实文件缺失却返回 Passed、API 失败无 evidence、复制第三方脚本、Markdown 中文检查失败，均为 Blocker。

## 进入下一阶段条件

默认 self-check Passed；真实 smoke test 若执行失败，必须有明确 `failure_stage`、report 路径和下一步证据需求。
