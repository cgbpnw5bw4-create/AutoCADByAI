# DrawingReview Module

本模块负责复审工程图、PDF、尺寸、必要视图和标题栏完整性。

边界：

- Agent 协调工程图复审。
- Validator 在可能时执行确定性检查。
- Reviewer 生成 `ReviewReport`。
- Gatekeeper 根据报告裁决通过、打回、失败或人工审批。
