# 架构说明

`AI_Mechanical_Engineering_Agent_Platform` 是面向机械工程自动化的长期可托管多 Agent 平台。当前重点是平台边界、运行时隔离、内部协作、质量门禁和可审计性。真实 SolidWorks 能力已经接入受控主工作流程，但默认仍关闭，不允许绕过 Worker、Validator、Reviewer 或 QualityGate。

## PlatformCore

`PlatformCore` 提供与具体 Agent Runtime 和 CAD 工具无关的平台能力：

- `TaskSystem`：任务生命周期和状态。
- `WorkflowEngine`：顺序工作流、步骤结果、Retry、FailureReport、HumanApprovalRequest。
- `AgentRegistry`、`SkillRegistry`、`ModuleRegistry`、`WorkerRegistry`：内存注册与查找。
- `ModuleManifestLoader`：读取 `module.yaml`，并记录 fallback 来源。
- `InternalAgentRouter`：只允许平台内部调用 Internal Agent，并记录审计日志。
- `ContextManager`：创建工作流上下文。
- `PermissionManager`：处理 Agent 可见性和 Gateway 暴露规则。
- `EventBus`：记录系统事件。
- `AuditLog`：记录任务、Agent、Skill、Worker 和 Gate 的执行日志。

## Contracts

`AgentContracts`、`ModuleContracts`、`SkillContracts` 和 `WorkerContracts` 定义平台稳定边界。

Agent 负责判断、协调和结构化输出。Skill 负责结构化转换和辅助能力。Worker 负责外部系统执行。Module 是完整能力板块，不是散乱脚本目录。

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

## AgentRuntime.Microsoft

`AgentRuntime.Microsoft` 是唯一允许引用 Microsoft Agent Framework 相关包的项目。当前引用 `Microsoft.Agents.AI`，并默认使用 `MockRuntime`。

业务模块、Worker、`PlatformCore` 和 Contracts 不依赖 Microsoft Runtime API。真实 Runtime 只允许包装 `chief-engineer`，并且必须把模型输出转换为平台自己的 `AgentOutput`。

模型输出只提供任务理解和协作建议。最终内部调度仍由 `ChiefEngineerOrchestrator`、`SequentialWorkflowEngine`、Internal Agent workflow steps 和 `QualityGate` 控制。Internal Agents 在当前阶段继续使用 Module Agent 或 Mock Agent。

## Modules

每个 Module 都是完整能力板块，包含 agents、skills、workers、validators、reviewers、schemas 和 tests。

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

Worker 是未来调用 SolidWorks、AutoCAD、API、SDK、COM 或 MCP 工业软件桥接的执行层。当前只注册 Fake Worker：

- `FakeSolidWorksWorker`
- `FakeAutoCADWorker`

Agent 不允许直接调用 CAD API、COM 对象或外部进程。Agent 只能生成结构化计划并通过平台边界交给 Worker。

V0.9-B 中，`CADModeling` 模块新增 SolidWorks dry-run skeleton。`SolidWorksBuildPlan` 是从 `CADModelSpec` 到 `SolidWorksWorkerRequest` 的中间层，`FakeSolidWorksWorker` 只生成文本形式的模拟产物和 `build_report.json`。`allow_real_cad_execution` 是未来真实 CAD 执行的安全开关，默认必须为 `false`。当前不得直接复用外部 Python COM 脚本绕过平台，也不得让 Agent、Gateway 或 LLM 直接调用 SolidWorks Worker。

V1.0-A 中，SolidWorks 能力新增真实执行前安全边界：`SolidWorksRuntimeOptions`、`SolidWorksPreflightReport`、`SolidWorksEnvironmentValidator`、`SolidWorksSessionManager` 和 `RealSolidWorksWorker` skeleton。默认 self-check 不连接 SolidWorks，不调用 COM，也不要求 CI 或开发机安装 SolidWorks。真实连接 smoke test 只有在 `SW_ENABLE_REAL_EXECUTION=true` 且 `SW_REAL_SMOKE_TEST=true` 时才允许尝试；严格失败模式需要额外设置 `SW_STRICT_REAL_SMOKE_TEST=true`。

真实 CAD Worker 的权限由三层共同约束：请求中的 `AllowRealCadExecution`、请求中的 `DryRun`、以及环境变量 `SW_ENABLE_REAL_EXECUTION`。三者不同时满足时，只能进入 `RealPreflightOnly`，不得连接 COM。V1.0-A 不实现 `RealBuild`，也不生成真实 `.SLDPRT`、`.STEP` 或工程图。

V1.0-B 在上述边界内新增第一个受控真实构建场景：`RealBuildPlateBasic4Holes`。该场景只接受 `BuildPlan.PartType = plate_basic_4holes`，用于生成 160 x 80 x 12 mm 板件、四个直径 10 mm 通孔，并输出真实 `.SLDPRT`、`.STEP` 和 `build_report.json`。默认 self-check 不调用该能力；只有同时设置 `SW_ENABLE_REAL_EXECUTION=true` 和 `SW_REAL_BUILD_SMOKE_TEST=true` 时才允许尝试真实建模，严格失败模式还需要 `SW_STRICT_REAL_BUILD_TEST=true`。

真实构建链路必须保持为 `SolidWorksBuildPlan` → `SolidWorksWorkerRequest` → `RealSolidWorksWorker` → `SolidWorksArtifactValidator` → `SolidWorksBuildPlanReviewer` → `QualityGate`。它不改变 Agent 可见性，也不允许 Gateway、LLM 或 Agent 直接持有 COM 对象或直接调用 Worker。

## QualityGate

`QualityGate` 负责校验、复审、打回、失败报告和人工审批挂起。它包含：

- Validator
- Reviewer
- Gatekeeper
- `GateDecisionPolicy`
- `RejectReportBuilder`
- `RetryPolicy`

`WorkflowEngine` 根据 `GateDecision` 做流程控制：`Passed` 进入下一步，`Rejected` 按策略重试或停止，`Failed` 生成 FailureReport，`NeedsHumanApproval` 生成 HumanApprovalRequest 并暂停。

## Interfaces

`Interfaces` 是平台入口层：

- `CliHost`：运行 self-check。
- `ApiHost`：预留 API Host。
- `AgentGatewayHost`：对外暴露 Public Agent Directory 和 Agent Message Endpoint。

Gateway 只暴露 Public Agent，当前只有 `chief-engineer`。Runtime、Internal Agent、Worker 和 QualityGate 的边界不会因为外部入口变化而改变。

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

V1.9 Phase 1 在 V1.8 Registry 架构上为 `flange_basic` 和 `shaft_basic` 建立真实 SolidWorks build-only 主工作流程。输入是结构化 `CADModelSpec` 和三层授权，输出是真实 SLDPRT、STEP、执行报告、质量裁决和 build-only 发布包。

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

真实执行必须同时满足请求授权、`LocalDevelopmentProfile` 本地授权和环境授权。默认 self-check 不使用任何授权，不连接 COM。所有真实 SolidWorks 任务在全局范围串行；阶段验收按 `flange_basic` 再 `shaft_basic` 执行。

### 发布包与失败语义

发布包根目录统一为 `output/solidworks/e2e/<part_type>/<timestamp>/`。每个 build-only 包必须包含零件、STEP、`build_report.json`、`e2e_execution_report.json` 和 `release_manifest.json`。失败语义必须精确到法兰轮廓/拉伸/内孔/螺栓孔、轴轮廓/旋转/台阶、保存、STEP 导出、产物校验或质量门禁拒绝。

V1.9 Phase 2 已完成两族独立 diagnostic、视觉复核和 CLI 真实主流程回填。`flange_basic` 的最终目录为 `output/solidworks/e2e/flange_basic/cad-e2e-20260720_085451_612-f303b15a20be4b1987a53007bb819ea6/`，`shaft_basic` 的最终目录为 `output/solidworks/e2e/shaft_basic/cad-e2e-20260720_085555_295-33293160545047a7845a938319737a44/`；两者均为 `Passed`、`Deliverable`、QualityGate `Passed`。diagnostic 的 body count 与 theoretical volume 自动核验仍为非阻断 Improvement。

### 验证与禁止事项

验证覆盖两族真实 Builder、两族主工作流程、默认关闭、API evidence、ArtifactValidator、plate 完整包回归、无大型类型 switch 和全族 Registry 分发。禁止 flange / shaft 自动工程图，禁止并发 COM，禁止跳过平台边界，禁止进入 V2.0。
