# SolidWorks Worker

本目录是未来 SolidWorks 执行层的 dry-run skeleton。当前只保留接口和假实现，不连接真实 SolidWorks，不调用 COM，不启动进程，也不写真实 CAD 文件。

当前范围：

- 提供 `ISolidWorksWorker`。
- 提供 `FakeSolidWorksWorker`。
- 保持 dry-run skeleton，用于平台注册、self-check 和边界验证。
- 不接真实 SolidWorks API、SDK、COM 或 MCP。

未来职责：

- SolidWorks 建模。
- 工程图生成。
- STEP / PDF 导出。
- 读取模型元数据。

架构边界：

- Skill 只生成 `SolidWorksBuildPlan` 或其他结构化计划。
- Worker 才能执行 SolidWorks 操作。
- Validator 负责环境与输出校验。
- Reviewer 负责工程合理性复审。
- `QualityGate` 负责最终裁决。
- 不得使用“一个 Python 脚本直接控制 SolidWorks”的方式绕过平台。
- 不得复制外部 `solidworks-automation-skill/scripts` 源码到本项目。
