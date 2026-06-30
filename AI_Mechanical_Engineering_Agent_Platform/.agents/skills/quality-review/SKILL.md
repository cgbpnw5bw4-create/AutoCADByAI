---
name: quality-review
description: "用于项目质量审查，检查架构边界、self-check 字段、分层规则、真实 CAD 默认关闭、第三方脚本边界和 Markdown 中文规范。"
---

# 质量审查技能

## 审查范围

检查 Agent、Skill、Worker、Validator、Reviewer、QualityGate 分层是否被破坏。检查 Gateway 是否仍只暴露 `chief-engineer`，Internal Agent 是否仍不能被外部直接调用。

## 必查项

- self-check 字段是否覆盖新增能力。
- 是否默认执行真实 CAD。
- 是否复制第三方脚本。
- 是否让 Agent、Gateway 或 LLM 直接调用 Worker。
- Markdown 中文检查是否通过。

## 输出格式

输出 Blockers、Improvements、测试结果和是否可以进入下一阶段。
