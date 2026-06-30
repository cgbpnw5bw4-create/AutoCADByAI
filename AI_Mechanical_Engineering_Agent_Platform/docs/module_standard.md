# Module 标准

Module 是完整能力板块，不是脚本文件夹。

每个 Module 应包含：

- `README.md`：说明职责、边界和当前成熟度。
- `module.yaml`：描述名称、版本、能力和注册组件。
- `agents/`：模块拥有的 Agent 实现或适配器。
- `skills/`：结构化转换和辅助能力。
- `workers/`：仅在模块拥有外部执行面时放置执行适配。
- `validators/`：确定性校验。
- `reviewers/`：生成 `ReviewReport` 的复审逻辑。
- `schemas/`：模块专用 Schema 扩展。
- `tests/`：模块级行为和契约测试。

模块之间应通过平台 Contracts 和 `DomainSchemas` 通信。核心任务数据不得只靠自然语言传递。

## SolidWorks Module 骨架要求

SolidWorks 能力当前放在 `CADModeling` 模块中，属于 dry-run skeleton。模块内的 Skill 只生成 `SolidWorksBuildPlan`，Worker 才能接收 `SolidWorksWorkerRequest` 并执行 dry-run。Validator 负责检查 BuildPlan、执行模式和输出产物，Reviewer 负责工程合理性复审，`QualityGate` 负责最终裁决。

当前不得把外部 Python COM 脚本直接放进模块并绕过平台，也不得复制 `solidworks-automation-skill/scripts` 源码。任何真实 SolidWorks 执行能力都必须作为后续独立 Worker 接入，并继续遵守 Agent、Skill、Worker、Validator、Reviewer 的边界。

V1.0-B 中，`CADModeling` 模块允许通过 `RealSolidWorksWorker` 执行第一个受控真实构建场景 `plate_basic_4holes`。模块边界仍然不变：Skill 生成 `SolidWorksBuildPlan`，Worker 接收 `SolidWorksWorkerRequest`，`SolidWorksArtifactValidator` 校验真实产物，`SolidWorksBuildPlanReviewer` 做工程规则复审，最终由 `QualityGate` 裁决。Agent、Gateway 和 LLM 仍不得直接调用 Worker。

当前真实构建只支持板件四孔样例，不支持工程图、装配体、通用建模、批量建模或复制外部 Python COM 脚本。任何后续能力都必须作为新的受控 Skill、Worker、Validator 和 Reviewer 增量接入。
