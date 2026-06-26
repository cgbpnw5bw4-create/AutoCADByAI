# MechanicalDesign Module

本模块负责检查机械结构可行性、参数合理性、材料假设和设计风险。

边界：

- Agent 输出机械设计判断和建议。
- Skill 将设计假设转换为结构化复审输入。
- Validator 和 Reviewer 生成 `ReviewReport`。
- 本模块不直接执行 CAD 操作。
