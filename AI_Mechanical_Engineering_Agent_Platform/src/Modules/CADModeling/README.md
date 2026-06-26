# CADModeling Module

本模块负责把已接受的设计意图转换为 `BuildSpec`，并规划 CAD Worker 执行。

边界：

- Agent 创建建模计划和 Worker 调用计划。
- Skill 生成结构化 `BuildSpec`。
- Worker 未来负责 SolidWorks、AutoCAD 或其他 CAD 执行。
- Agent 永远不直接调用 CAD API、SDK 或 COM。
