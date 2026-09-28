# 架构说明

`AI Mechanical Engineer Platform` 是面向机械工程自动化的长期平台。强模型负责工程理解、规划和决策，平台负责工程约束、确定性执行、真实 CAD/API 能力和结果验收。运行架构优先采用少量强 Agent 与多个确定性 Worker，当前真实 SolidWorks 主流程继续遵循本地交互式默认启用、测试与 dry-run 禁用、证据不匹配失败关闭的策略。

## 目标、适用范围与输入输出

本说明用于维护当前平台边界和增量演进方向。输入为工程需求、约束、结构化模型与现有 API evidence；输出为工程计划、受控执行、验收报告与可追溯产物。当前阶段见 [V2.2-B 强模型架构增量优化](v2_2_b_frontier_model_architecture.md)，后文 V1.8–V2.0-A 保留各阶段当时的架构范围，不能替代当前能力准入判断。

```text
Frontier Model / Future AGI
→ Engineering Agent
→ Engineering Plan
→ Deterministic Workers
→ Verified CAD/API Layer
→ SolidWorks
→ QualityGate
```

`Frontier Model / Future AGI` 表示模型可替换的长期方向，现有结构化建模计划由 `CADModelSpec`、`FeatureGraph`、`SolidWorksBuildPlan` 承载。平台只接收受约束的数据和建议，不把模型输出提升为 Worker 权限或真实 API 能力。

正式能力准入链保持 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter`。这是能力发现、校验、授权和执行责任的顺序；具体证据复核在既有管线中完成，不移动或重写来源绑定的 CAD 代码以迎合图示。

## 职责边界

| 层 | 职责 | 必须保持的边界 |
|---|---|---|
| `Agent` | 工程理解、设计与特征规划、建模顺序、装配策略、异常恢复建议和工程决策 | 不直接调用 Worker、COM 或正式执行 API |
| `Skill` | 可复用机械工程能力与结构化转换 | 不作为单纯消息转发的独立 Agent |
| `Worker` | 按已校验计划确定性执行、产物与诊断输出 | 不允许模型绕过正式工作流直接调用 |
| `Registry` | 能力发现和映射，包含 Feature Handler 注册 | 注册存在不等于真实能力已受证 |
| `API Evidence` | 真实 SolidWorks API 的来源、参数档案、源码和环境绑定证据 | 模型建议、dry-run 和旧档案不能授权新路径 |
| `Adapter` | 外部系统执行接口及受控 API 实现 | Handler 不持有 COM，Adapter 只能由受控执行层进入 |
| `QualityGate` | 工程结果验收和继续、打回、失败或审批裁决 | 缺少检查依据不得占位通过 |
| `ModelRuntime` | 模型能力接入和切换 | 业务层不依赖具体模型名称或供应商 SDK |

## PlatformCore

`PlatformCore` 提供与具体 Agent Runtime 和 CAD 工具无关的平台能力：

- `TaskSystem`：任务生命周期和状态。
- `WorkflowEngine`：通过 `IWorkflowEngine` 隔离调用方；现有 `SequentialWorkflowEngine` 承担顺序执行、Retry、FailureReport 和 HumanApprovalRequest。
- `AgentRegistry`、`SkillRegistry`、`ModuleRegistry`、`WorkerRegistry`：内存注册与查找。
- `ModuleManifestLoader`：读取 `module.yaml`，并记录 fallback 来源。
- `InternalAgentRouter`：只允许平台内部调用 Internal Agent，并记录审计日志。
- `ContextManager`：创建工作流上下文。
- `PermissionManager`：处理 Agent 可见性和 Gateway 暴露规则。
- `EventBus`：记录系统事件。
- `AuditLog`：记录任务、Agent、Skill、Worker 和 Gate 的执行日志。

## Contracts

`AgentContracts`、`ModuleContracts`、`SkillContracts` 和 `WorkerContracts` 定义平台稳定边界，`ModelRuntime` 增加模型供应方的中立合同。

Agent 负责工程理解、规划、决策和结构化输出。Skill 负责可复用工程能力。Worker 负责确定性外部系统执行。Module 是能力组织边界，不要求每个 Module 都增加独立推理 Agent；已有内部角色可作为兼容入口保留。

## DomainSchemas

`DomainSchemas` 存放机械和 CAD 任务之间传递的标准结构：

- `CADModelSpec`
- `BuildSpec`
- `DrawingSpec`
- `ReviewReport`
- `RejectReport`
- `GateDecision`
- `ArtifactInfo`
- `ErrorReport`
- `FinalReport`
- `InternalCollaborationReport`
- `FailureReport`
- `HumanApprovalRequest`
- `MarkdownLanguageReport`

核心任务状态必须通过这些 Schema 传递，不能只依赖自然语言。

## ModelRuntime 与兼容适配

独立 `ModelRuntime` 通过 `IModelProvider`、`ModelRequest` 和受限用途枚举表达工程模型调用，由 `ModelRuntime.GenerateAsync` 统一进入供应方。供应商名称、模型名称与连接配置留在接入层；现有 `AgentRuntime.Microsoft` 继续兼容既有配置、错误转换和 Mock 行为。

`AgentRuntime.Microsoft` 是唯一允许引用 Microsoft Agent Framework 相关包的项目。当前引用 `Microsoft.Agents.AI`，并默认使用 `MockRuntime`。

业务模块、Worker、`PlatformCore` 和 Contracts 不依赖 Microsoft Runtime API。真实 Runtime 只允许包装 `chief-engineer`，并且必须把模型输出转换为平台自己的 `AgentOutput`。

模型输出负责工程理解、规划和决策建议。正式调度由 `ChiefEngineerOrchestrator`、`IWorkflowEngine` 与 `QualityGate` 控制。旧默认路线中的内部 Agent 实际是确定性模拟或占位实现，并没有四次 LLM 推理；本轮收敛不得虚称已经消除多次模型调用或内部 JSON 中转。

默认 `ChiefEngineerOrchestrator` 仅运行 `EngineeringPlanValidationStep` 校验规划输入，不调用四个占位内部角色，也不生成工程计划。具体 CAD 计划仍由后续原 Skill/Validator 生成和校验；默认报告的 `CalledAgents` / `AgentOutputs` 为空。显式传入 `InternalWorkflowRoute.EngineeringDefault` 继续运行原四角色流程，保留其重试和审批用法。

旧 `ToolBridge` 只保留兼容名称映射，不再提供直接执行 Worker 的能力。模型输出中的 API 名称和参数必须接受 Registry、Handler 与 API Evidence 的独立准入检查。

## WorkflowEngine 演进

保留 `SequentialWorkflowEngine` 的已验证行为并实现 `IWorkflowEngine`，调用方依赖接口而非顺序引擎具体类型。当前能力仍为顺序步骤、既有重试、具体身份绑定的人工审批和恢复；`Parallel` 与 `Conditional` 仅是未来方向，当前不接收或伪装执行未支持的流程。

后续并行仅用于无共享外部状态的步骤，SolidWorks COM 保持既有全局串行约束。条件分支应先确定持久化、审批身份和结果聚合合同，再实现对应引擎或计划类型；本轮不增加空执行器。

## Modules

Module 按实际能力组织 agents、skills、workers、validators、reviewers、schemas 和 tests；目录完整不代表全部能力已实现，也不要求每个板块独立调用模型。

当前模块：

- `RequirementUnderstanding`
- `MechanicalDesign`
- `CADModeling`
- `DrawingGeneration`
- `DrawingReview`
- `CodeEngineering`
- `CodeReview`
- `ErrorDiagnosis`

Module 元数据优先从 `module.yaml` 加载。只有在 YAML 加载失败时才允许使用 fallback manifest，并必须写入审计。

## Workers

Worker 是调用 SolidWorks、AutoCAD、API、SDK、COM 或工业软件桥接的确定性执行层。默认平台验证注册以下替身，真实 SolidWorks 主流程按现有运行策略选择受控 Real Worker：

- `FakeSolidWorksWorker`
- `FakeAutoCADWorker`

Agent 不允许直接调用 CAD API、COM 对象或外部进程。Agent 只能生成结构化计划并通过平台边界交给 Worker。

V0.9-B 中，`CADModeling` 模块新增 SolidWorks dry-run skeleton。`SolidWorksBuildPlan` 是从 `CADModelSpec` 到 `SolidWorksWorkerRequest` 的中间层，`FakeSolidWorksWorker` 只生成文本形式的模拟产物和 `build_report.json`。V2.0 已废弃请求级 `allow_real_cad_execution` 前置确认；当前仍不得直接复用外部 Python COM 脚本绕过平台，也不得让 Agent、Gateway 或 LLM 直接调用 SolidWorks Worker。

V1.0-A 中，SolidWorks 能力新增真实执行前安全边界：`SolidWorksRuntimeOptions`、`SolidWorksPreflightReport`、`SolidWorksEnvironmentValidator`、`SolidWorksSessionManager` 和 `RealSolidWorksWorker` skeleton。V2.0 继续保证 self-check、CI 和单元测试不连接 SolidWorks、不调用 COM；本地交互式主流程默认允许连接，独立 diagnostic 仍使用其专用入口。

V2.0 的真实 CAD Worker 由统一运行策略约束：本地交互式且 `dry_run=false` 时默认允许；`dry_run=true`、`SW_DISABLE_REAL_EXECUTION=true`、CI、单元测试或 `SW_FORCE_FAKE_WORKER=true` 时不得连接 COM。V1.0-A 的历史能力范围不变。

V1.0-B 在上述边界内新增第一个受控真实构建场景：`RealBuildPlateBasic4Holes`。该场景只接受 `BuildPlan.PartType = plate_basic_4holes`，用于生成 160 x 80 x 12 mm 板件、四个直径 10 mm 通孔，并输出真实 `.SLDPRT`、`.STEP` 和 `build_report.json`。V2.0 的本地交互式主流程默认调用该能力；self-check、CI、单元测试和 dry-run 不调用。

真实构建链路必须保持为 `SolidWorksBuildPlan` → `SolidWorksWorkerRequest` → `RealSolidWorksWorker` → `SolidWorksArtifactValidator` → `SolidWorksBuildPlanReviewer` → `QualityGate`。它不改变 Agent 可见性，也不允许 Gateway、LLM 或 Agent 直接持有 COM 对象或直接调用 Worker。

## QualityGate

`QualityGate` 负责校验、复审、打回、失败报告和人工审批挂起。它包含：

- Validator
- Reviewer
- Gatekeeper
- `GateDecisionPolicy`
- `RejectReportBuilder`
- `RetryPolicy`

`WorkflowEngine` 根据 `GateDecision` 做流程控制：`Passed` 进入下一步，`Rejected` 按策略重试或停止，`Failed` 生成 FailureReport，`NeedsHumanApproval` 生成 HumanApprovalRequest 并暂停。暂停状态由 `IWorkflowApprovalStore` 保存；宿主必须显式提交批准、拒绝或退回决定，批准才会恢复剩余步骤，拒绝和退回会产生 FailureReport 并阻断下游。

专项门禁的扩展方向为 `GeometryQualityGate`、`APIEvidenceQualityGate`、`AssemblyQualityGate`、`DrawingQualityGate` 与 `ManufacturabilityQualityGate`。当前几何、API evidence 与工程图已有检查继续复用；预留分类与组合合同不表示装配和制造性专项算法已存在，实际新增组件与接线以阶段页和源码为准。

## Interfaces

`Interfaces` 是平台入口层：

- `CliHost`：运行 self-check。
- `ApiHost`：预留 API Host。
- `AgentGatewayHost`：对外暴露 Public Agent Directory 和 Agent Message Endpoint。

Gateway 只暴露 Public Agent，当前只有 `chief-engineer`。Runtime、Internal Agent、Worker 和 QualityGate 的边界不会因为外部入口变化而改变。

## 执行步骤、验证标准与兼容原因

先按需求选择工程 Skill 和受证能力，再提交结构化计划给平台工作流，执行后统一经过 Validator、Reviewer、QualityGate 与发布包检查。修改后运行完整 build、test 和定向 self-check；接口替换、模型供应方替身、执行权限拒绝与已有审批/重试行为分别验证，真实 CAD 能力仍须同次真机验收证据。

正式名称统一为 `AI Mechanical Engineer Platform`，但底层 `AI_Mechanical_Engineering_Agent_Platform` 目录、解决方案、程序集、命名空间、反射加载键和证据路径保留，以避免无关兼容风险。历史阶段与审查中的名称不做全局替换。

常见失败包括模型配置不完整、计划无效、接口语义漂移和 API evidence 与物理产物不匹配。应保留直接原因并按阶段或模块修复手册定位；禁止绕过 Worker、弱化 QualityGate、修改旧 evidence 或将自检通过等同于真实 CAD 通过。

## V1.8 参数化零件族架构

### 目标与适用范围

V1.8 将围绕 `plate_basic_4holes` 建立的单一零件路径抽象为通用 `CADModelSpec` 和可注册零件族。本轮注册 `plate_basic_4holes`、`flange_basic`、`shaft_basic`，不引入装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。

### 输入与输出边界

`CADModelSpec` 使用以下通用字段：

- `part_type`
- `dimensions`
- `features`
- `material`
- `output_requirements`
- `drawing_requirements`
- `execution_options`

该结构是领域输入，不携带任何组件对象。后续输出依次是已校验的构建计划、执行结果、产物校验报告、工程复审报告、质量门禁裁决和发布包清单。

### Registry 与零件族责任

`PartTypeRegistry` 以 `part_type` 映射独立 `IPartFamilyDefinition`，`PartFamilyBuilderRegistry` 以同一稳定键映射 `IPartFamilyBuilder`。定义负责 Schema、参数 Validator 和 BuildPlan 生成；Builder 负责把已校验计划转为零件族 dry-run 产物，并声明该族的真实执行支持与 API evidence。已验证的 plate 真实操作仍由 `RealSolidWorksWorker` 和现有 plate Builder 执行。公共 Router、Skill 和 Worker 不包含大型 `switch(part_type)`。

| 定义 | 参数 | 执行边界 |
|---|---|---|
| `PlateBasic4HolesDefinition` | 长、宽、厚、四孔数量、孔径和孔位 | 复用已验证 plate Builder，保持真实能力与回归。 |
| `FlangeBasicDefinition` | 外径、内径、厚度、螺栓孔数、螺栓孔径、分布圆径 | V1.8 保证 dry-run；真实入口需独立 flange smoke。 |
| `ShaftBasicDefinition` | 直径、长度、可选台阶直径/长度列表 | V1.8 保证 dry-run；真实入口需旋转专用证据。 |

### 执行步骤

```text
结构化输入
→ CADModelSpec
→ PartTypeRegistry
→ 参数 Validator
→ BuildPlan
→ Router
→ Worker
  → PartFamilyBuilderRegistry
  → 对应 PartFamilyBuilder
→ ArtifactValidator
→ Drawing
→ QualityGate
→ ReleasePackage
```

上述图中的 Worker 只能由 `ChiefEngineerOrchestrator` 经 `WorkflowEngine` 和 Router 调用，Worker 在内部解析对应 Builder，执行后仍必须经过产物 Validator、Reviewer 和 QualityGate。V1.8 的新 `PartFamilyBuilderRegistry` 承担三族 dry-run 分派；plate 真实路径继续使用已经验收的 `ISolidWorksPlateBuilder` 适配器，flange / shaft 在独立 smoke 通过前保持 fail-closed。默认 self-check 使用 dry-run 和可注入替身验证链路，不启动 SolidWorks。

### 失败、验证与禁止事项

`unsupported_part_type`、`missing_required_parameter`、`invalid_parameter_value` 必须在 Worker 之前返回。Registry、计划、Builder 和执行失败使用 `part_family_definition_missing`、`build_plan_generation_failed`、`part_family_builder_missing`、`flange_build_failed`、`shaft_build_failed`、`artifact_validation_failed`。验证必须包含三族注册、前置拒绝、plate 回归、flange / shaft dry-run、无大型 switch 与默认真实 CAD 关闭。

禁止把 dry-run 、文件存在或候选 API 解释为真实验收成功；禁止为新增零件族破坏 Gateway、Agent、Worker 和 QualityGate 边界。

## V1.9 Phase 1 多零件族真实构建架构

### 目标与范围

V1.9 Phase 1 在 V1.8 Registry 架构上为 `flange_basic` 和 `shaft_basic` 建立真实 SolidWorks build-only 主工作流程。V2.0 输入只需结构化 `CADModelSpec`，运行权限由统一默认策略决定；输出仍是真实 SLDPRT、STEP、执行报告、质量裁决和 build-only 发布包。

`flange_basic` 和 `shaft_basic` 不进入工程图、尺寸、标题栏或 PDF 链路。`plate_basic_4holes` 仍执行完整工程图包回归，不被降级为 build-only。

### 完整调用链

```text
结构化输入
→ Gateway / chief-engineer
→ ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilderRegistry
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ build-only ReleasePackage
```

Registry 负责零件族和 Builder 映射；`RealSolidWorksWorker` 负责统一预检、会话、真实执行、保存/导出和报告；Validator、Reviewer 和 QualityGate 负责防止“API 返回非空”或“文件存在”被误解为可交付。最终验收不接受直接 Builder 或 SmokeRunner 路径。

### 安全与串行边界

本地交互式真实执行不再要求请求授权或 `LocalDevelopmentProfile`；可选本地配置只提供模板、可见性和超时。self-check、CI、单元测试和 dry-run 不连接 COM。所有真实 SolidWorks 任务在全局范围串行。

### 发布包与失败语义

发布包根目录统一为 `output/solidworks/e2e/<part_type>/<timestamp>/`。每个 build-only 包必须包含零件、STEP、`build_report.json`、`e2e_execution_report.json` 和 `release_manifest.json`。失败语义必须精确到法兰轮廓/拉伸/内孔/螺栓孔、轴轮廓/旋转/台阶、保存、STEP 导出、产物校验或质量门禁拒绝。

V1.9 Phase 2 已完成两族独立 diagnostic、视觉复核和 CLI 真实主流程回填。`flange_basic` 的最终目录为 `output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/`，`shaft_basic` 的最终目录为 `output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/`；两者均为 `Passed`、`Deliverable`、QualityGate `Passed`。diagnostic 的 body count 与 theoretical volume 自动核验仍为非阻断 Improvement。

### 验证与禁止事项

验证覆盖两族真实 Builder、两族主工作流程、V2.0 默认执行策略、API evidence、ArtifactValidator、plate 完整包回归、无大型类型 switch 和全族 Registry 分发。禁止 flange / shaft 自动工程图，禁止并发 COM，禁止跳过平台边界。

## V2.0-A 通用 CAD 描述架构

### 目标与唯一编译链

V2.0-A 在零件族参数校验和 Worker 之间建立 CAD 系统中立的通用描述层。完整的通用链为：

```text
canonical CADModelSpec
→ SketchDefinition / FeatureDefinition
→ FeatureGraph.ValidateAndSort
→ BuildPlanCompiler
→ SolidWorksBuildPlan
→ BuildPlan Validator / Reviewer
→ dry-run Worker
```

`CADModelSpec` 的核心字段是 `model_id`、`model_type`、`unit`、`parameters`、`reference_geometry`、`sketches`、`features`、`material`、`output_requirements`、`drawing_requirements` 和 `execution_options`。旧 `id`、`part_type`、`dimensions` 与对象形式 `features` 只在输入边界兼容，不能成为另一套计划来源。

### 草图与特征图

`SketchDefinition` 通过 `sketch_id`、`reference_plane`、`entities`、`constraints`、`dimensions` 和 `execution_order` 描述草图。实体与约束使用稳定标识互相引用；构造中心线编译为独立 `CreateCenterLine` operation。

`FeatureDefinition` 通过 `feature_id`、`feature_type`、参数、依赖、草图引用、特征引用、执行顺序和目标引用组成有向图。十类受支持特征为凸台拉伸、拉伸切除、旋转凸台、旋转切除、圆角、倒角、孔、线性阵列、圆周阵列和镜像。

`FeatureGraph` 必须在 Worker 前拒绝缺失依赖、依赖环和非法显式顺序。有效图使用稳定拓扑顺序；`BuildPlanCompiler` 按该顺序生成草图、中心线、特征、保存与 STEP 导出 operation。详细 Schema、映射和 failure_stage 见 `docs/v2_0_a_generic_cad_model_spec.md`。

### 三族迁移与真实执行边界

`plate_basic_4holes`、`flange_basic`、`shaft_basic` 都先由族 Validator 校验参数，再由 `PartFamilyGenericModelFactory` 生成 canonical 草图和特征图，最后统一交给 `BuildPlanCompiler`。各族不得再维护一套手写 BuildPlan operation。

V1.9 三族专用真实 Builder、Registry 分发和真实验收证据保持有效。它们不等于通用 Feature Handler：V2.0-A 的任意新图、任意十类特征组合只允许编译和 dry-run，不能声称已真实执行。通用特征到真实 COM 的执行延期到 V2.0-B。

### 验证与禁止事项

验证必须覆盖 canonical JSON 往返、草图实体与约束、特征图缺失依赖/环/顺序、BuildPlan 编译、三族 FeatureGraph 迁移、Worker 前拒绝和无零件族字符串分支。self-check、单元测试与 dry-run 不连接 COM。

禁止绕过 `FeatureGraph` 直接从族参数写计划，禁止把 operation 名称解释为真实 API 支持，禁止在本阶段引入装配体、BOM、批量队列或通用真实特征执行。本阶段完成后停在 V2.0-A，不进入 V2.0-B。
