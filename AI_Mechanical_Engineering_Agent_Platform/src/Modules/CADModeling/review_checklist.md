# CADModeling 审查清单

## Blockers

- 默认 self-check 启动真实 CAD。
- `Agent`、`Gateway` 或 `LLM` 直接调用 Worker。
- `Skill` 直接执行 CAD 操作。
- COM 类型泄漏到 Contracts、PlatformCore 或 QualityGate。
- 失败只记录 `Failed`，没有可行动 `failure_stage`。
- API 失败没有 evidence report。
- 复制第三方 `scripts` 源码。
- Markdown 中文检查失败。

## Improvements

- evidence 报告可以更细化候选策略。
- diagnostic report 可以增加更多中间对象状态。
- Reviewer 规则可以增加工程合理性检查。

## 进入下一阶段条件

必须通过 build、test、self-check。涉及真实 CAD 的能力必须有默认关闭开关、诊断 Runner、artifact validation 和 QualityGate 记录。
