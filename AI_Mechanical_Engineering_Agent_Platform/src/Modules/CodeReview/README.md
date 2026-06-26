# CodeReview Module

本模块负责复审未来 CAD 自动化代码的安全性、稳定性和可维护性。

边界：

- Agent 协调代码复审和风险分类。
- Validator 检查确定性的构建要求和策略要求。
- Reviewer 生成结构化 `ReviewReport`。
- Gatekeeper 决定代码是否可以进入后续 CAD workflow。
