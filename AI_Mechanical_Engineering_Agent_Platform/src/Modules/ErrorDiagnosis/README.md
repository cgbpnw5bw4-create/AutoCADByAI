# ErrorDiagnosis Module

本模块负责分析失败、定位可能原因并提出修复路径。

边界：

- Agent 分类失败来源并建议下一步动作。
- Skill 归一化日志和错误报告。
- Worker 未来可以运行诊断工具。
- 输出应结构化为 `ErrorReport` 和修复建议。
