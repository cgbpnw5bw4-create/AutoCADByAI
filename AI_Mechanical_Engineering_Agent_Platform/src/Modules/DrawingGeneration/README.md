# DrawingGeneration Module

本模块根据模型 artifacts 和 `DrawingSpec` 规划工程图生成。

边界：

- Agent 判断出图意图、图纸规格和视图要求。
- Skill 创建 `DrawingSpec`。
- Worker 未来通过 CAD 专用执行层创建工程图。
- 工程图复审交给 `DrawingReview`。
