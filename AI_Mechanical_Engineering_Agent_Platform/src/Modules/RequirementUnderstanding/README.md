# RequirementUnderstanding Module

本模块负责把自然语言机械需求转换为结构化 `CADModelSpec`。

边界：

- Agent 判断需求含义、缺失信息和澄清方向。
- Skill 执行结构化抽取和归一化。
- Validator 检查生成的 spec 是否足够进入后续设计。
- 当前版本不需要 Worker。
