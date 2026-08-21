# V2.0-C Feature Adapter 真实执行边界

## 阶段结论

V2.0-C 在 V2.0-B 的纯逻辑 Feature Handler 之后增加 `ISolidWorksFeatureAdapter`，把 COM 访问集中到 `RealSolidWorksFeatureAdapter`，并由 `RealSolidWorksWorker` 统一管理会话、顺序、报告和产物。Handler 只能校验参数、生成受控命令和解释 Adapter 结果，严禁直接访问 SolidWorks COM。

本阶段选定的最小诊断候选仅包括：

- 草图：line、rectangle、circle。
- 基体拉伸：blind extrude。
- 拉伸切除：blind cut。
- 简单孔：创建圆草图，再执行 blind `FeatureCut4`。

孔策略不得称为 `SimpleHole2` 或 Hole Wizard，也不得调用 `SimpleHole2`、`HoleWizard5` 等未选定 API。专用 diagnostic 实际运行前，四类能力的证据状态只能是 `diagnostic_candidate` 或 `unverified`；2026-07-30 新诊断通过后，仅 SolidWorks `33.5.0`、Handler/Adapter `2.0-c.2`、复合源码修订 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918` 和下述精确参数轮廓被提升为 `verified`。

## 适用范围

输入是经过 `CADModelSpecValidator`、FeatureGraph 校验、BuildPlan 编译、Handler Registry 解析和 Handler 参数校验的通用特征计划。输出分为两种：

1. 默认无 COM 验证结果：用于 build、test、self-check 和未授权环境。
2. 显式专用 diagnostic：用于采集真实 Adapter 的 API 返回、特征结果、保存、STEP 导出和产物证据。

最终可交付验收不从 diagnostic 入口得出，只能从 CLI `run-cad-workflow` 运行完整主工作流程。

本阶段不包含 revolve、fillet、chamfer、pattern、mirror、装配体、BOM、队列或 V2.0-D。

## 分层职责

| 层 | 允许职责 | 禁止职责 |
|---|---|---|
| Feature Handler | 类型匹配、Schema、参数校验、纯逻辑 BuildPlan/命令、结果报告映射 | 引用 SolidWorks Interop、持有 COM 对象、创建会话、保存文件 |
| `ISolidWorksFeatureAdapter` | 定义草图、拉伸、切除、孔组合、结果校验所需的可注入接口 | 决定工作流程、QualityGate 或发布语义 |
| `RealSolidWorksFeatureAdapter` | 选择基准、调用真实 SolidWorks API、返回可审计结果 | 猜测参数、绕过证据状态、直接宣布可交付 |
| `RealSolidWorksWorker` | 会话与安全检查、依赖顺序、Adapter 生命周期、保存/导出、报告聚合 | 写入零件族/特征大型 switch、接受空产物成功 |
| Validator / Reviewer / QualityGate | 校验产物、审查工程合理性、作最终门禁裁决 | 用 diagnostic 结果替代主流程证据 |

实现依赖方向必须保持：

```text
纯逻辑 Feature Handler
→ ISolidWorksFeatureAdapter
→ RealSolidWorksFeatureAdapter（唯一真实 COM Adapter）
→ RealSolidWorksWorker 统一编排和聚合
```

`RealSolidWorksWorker` 拥有真实 Adapter 和会话边界；上述箭头表示 Handler 产生的受控意图只能经接口到真实 Adapter，再由 Worker 纳入同次执行结果，不表示 Handler 可以自行创建 Adapter 或会话。

## 最小候选参数轮廓

### Sketch

- line：明确起点、终点和单位换算。
- rectangle：明确两个对角点或中心/角点语义；Adapter 必须选定一种稳定合同。
- circle：明确圆心、半径或圆周点语义。
- 只允许已验证的标准基准引用；任意面引用不在最小候选中。

### Blind Extrude

- 只接受有限正数深度。
- 只执行 blind end condition。
- `mid_plane`、thin、draft、复杂 feature scope 不在 V2.0-C 候选中。

### Blind Cut

- 只接受有限正数深度。
- 只执行 blind `FeatureCut4`。
- `through_all`、normal cut、thin、多实体 scope 不在 V2.0-C 候选中。

### Hole

```text
孔位置与直径
→ 创建独立圆草图
→ 保持切割草图活动或精确选择
→ blind FeatureCut4
→ 校验返回 Feature、重建与孔结果
```

该策略对应内部 operation `CreateSimpleHole`，但仍是组合执行，不是 SolidWorks API `SimpleHole2`，也不是 Hole Wizard。深度必须由输入或受控 BuildPlan 明确提供，不能以不可审计的超深默认值冒充贯穿。

## 证据状态与生产门禁

| 状态 | 含义 | 允许动作 |
|---|---|---|
| `unverified` | 尚无绑定当前 Adapter/版本/参数轮廓的真实诊断 | 纯逻辑校验、测试和文档；生产阻断 |
| `diagnostic_candidate` | 官方 API 与本地受限证据足以设计专用 diagnostic | 仅显式运行专用 diagnostic；生产仍阻断 |
| `verified` | 专用 diagnostic 对同版本、同参数轮廓产生可重复证据，并完成审查 | 可进入 `run-cad-workflow` 最终验收；不等于已可交付 |

任一所需特征不是 `verified` 时，真实生产路径返回 `feature_api_unverified`。V1.9 零件族专用 Builder 或 V2.0-B 候选 API 不能直接提升 Adapter 状态。

## 专用 diagnostic

### 前置条件

- 必须显式选择 diagnostic 入口和最小参数轮廓。
- CI、单元测试、self-check、dry-run 和 `SW_DISABLE_REAL_EXECUTION=true` 时不得连接 COM。
- 所需 Handler、Adapter、API evidence 和输出目录必须齐全。
- 真实执行全局串行，不读取历史 latest。

### 执行步骤

```text
受控 FeatureGraph / BuildPlan
→ Handler Registry 与纯逻辑参数校验
→ evidence 状态检查
→ ISolidWorksFeatureAdapter
→ RealSolidWorksFeatureAdapter
→ 逐特征结果校验
→ 保存 SLDPRT
→ 导出 STEP
→ 产物校验
→ feature_execution_report.json
```

### 输出结构

```text
output/solidworks/features/<timestamp>/
├── model.SLDPRT
├── model.STEP
└── feature_execution_report.json
```

报告必须记录运行标识、源码修订、SolidWorks 版本、Adapter/Handler 版本、证据状态、特征顺序、输入参数和单位、每步 API 返回、重建结果、`failure_stage`、产物绝对路径、大小及最终 diagnostic 状态。

特征错误读取优先使用 `GetErrorCode2(out bool IsWarning)`；仅在晚绑定运行库不能封送布尔引用参数时，回退到 SolidWorks 官方仍保留的 `GetErrorCode()` 兼容成员。两种读取都失败时按 `feature_result_invalid` 关闭流程。

### 验证标准

- `model.SLDPRT` 和 `model.STEP` 均来自当次运行、存在且非空。
- 所有预期特征都有有效结果，不接受仅 COM 调用无异常。
- 报告、产物和运行标识一致；任一缺失都不能标记 diagnostic 通过。
- diagnostic 通过只能支持证据评审和状态回填，不能生成 ReleasePackage 或宣称主流程已通过。

## 2026-07-30 最终诊断证据回填

### 作废的旧 run

`output/solidworks/features/20260730_073759_9143941/feature_execution_report.json` 曾记录 Cut/Hole 的非空 Feature 对象和成功重建，但人工几何复核发现模型没有对应孔。该 run 是 `feature_result_invalid` / 防假成功的反例，不得继续作为 Cut 或 Hole 的 `verified` evidence，也不得用其非空 SLDPRT/STEP 证明几何正确。

### 采用的新 run

权威 diagnostic 为 `output/solidworks/features/20260730_085830_6592380/feature_execution_report.json`：

| 证据项 | 结果 |
|---|---|
| SolidWorks 版本 | `33.5.0` |
| diagnostic 状态 | `CandidatePassed` |
| 主流程 / QualityGate / 交付 | `main_workflow_accepted=false`、`quality_gate_passed=false`、`NotDeliverable` |
| Handler / Adapter 版本 | `2.0-c.2` |
| 复合源码修订 | `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918` |
| SLDPRT | 73416 bytes；SHA256 `E411188A101E49EFB1BD835E9BEF3A16EA0EE3BB124A873FFB96EDC5E72012E3` |
| STEP | 26403 bytes；SHA256 `EF532158373D512CF31A76FE930CD90FBF21A913E52608CF9A04805F0590CE06` |
| 特征结果 | 7 个特征报告均为有效结果、成功重建且几何变化已校验 |
| 诊断审查边界 | 诊断预览审查仍要求人工复核；最终 100 分、`pass` 结论以同次 E2E 审查报告为准 |

报告中的节点状态仍是运行时的 `diagnostic_candidate`。诊断完成后，结合体积变化、产物哈希、特征树、规则审查和人工视觉复核，才将以下精确 evidence profile 回填为 `verified`：

| Handler | 精确 `ParameterProfile` | 几何证据 |
|---|---|---|
| Sketch | `standard_plane_top;line+center_rectangle+circle;empty_constraints;empty_dimensions;millimetres` | TopPlane 上的 line、center rectangle、circle 均创建、重建并被后续特征消费。 |
| Boss | `blind;single_end;positive_depth_mm;no_draft;no_thin;merge_result` | 体积从 `0` 增至 `5.9999999999999995E-05` m³。 |
| Cut | `blind;single_end;positive_depth_mm;through_all_false;no_thin;single_body_scope` | 体积从 `5.9999999999999995E-05` 降至 `5.9214601836602546E-05` m³。 |
| Hole | `simple_circular_cut_blind;diameter_matches_single_circle;positive_depth_mm;no_wizard` | 体积从 `5.9214601836602546E-05` 降至 `5.84292036732051E-05` m³。 |

`through_all`、`mid_plane`、原生 `SimpleHole2` / Hole Wizard、任意面引用和其他未列参数轮廓仍为 `unverified`，必须阻断。该 run 自身保持 diagnostic `CandidatePassed` / `NotDeliverable`；它只提供 evidence，不承担最终交付判定。最终主流程验收已经由下述独立 E2E run 完成。

## COM 前精确 profile 门禁

`FeatureHandlerRegistry` 在任何 `ConnectAsync` 前对整张特征图调用各 Handler 的 `ValidateEvidenceForRealExecution`。基础实现先执行参数 `Validate`，再检查 `ApiEvidence.AllowsRealExecution`、Handler 版本、复合源码修订、diagnostic 报告绑定及精确 `ParameterProfile`；复合修订覆盖授权、执行、校验和报告边界，当前必须等于 `feature-execution-source-sha256:71753c25d516130de0ee657da22ae7452bb0f2f7c9a6f355f69398464afc2918`。

- Sketch 只接受 `TopPlane`，且 `constraints`、`dimensions` 必须缺省、`null` 或为空集合；非 TopPlane 或非空约束/尺寸返回 `feature_api_unverified`。
- Boss 只接受有限正数 `depth_mm` 和 `direction=blind`；非法深度或其他方向返回 `invalid_feature_parameter`。
- Cut 只接受有限正数 blind `depth_mm`，`through_all` 缺省或明确为 `false`；非法布尔值、`through_all=true` 或非法深度返回 `invalid_feature_parameter`。
- Hole 只接受有限正直径、有限正数 blind 深度、`strategy=simple_circular_cut_blind`、`end_condition=blind`，且 `through_all` 缺省或为 `false`；未授权策略、终止条件或贯穿语义返回 `invalid_feature_parameter`。
- 所有 Handler 拒绝未授权参数键并返回 `invalid_feature_parameter`；这包括不属于各精确 profile 的额外参数。
- Hole 还必须直接依赖其声明的 `sketch_id`，该依赖草图只能含一个圆，且圆的半径/直径换算值必须等于请求的孔直径；依赖、单圆或直径不匹配返回 `feature_api_unverified`。
- evidence 状态、版本、复合修订、diagnostic 或其他精确 profile 未获授权时返回 `feature_api_unverified`。

任一节点失败时，整图预检失败，`RealSolidWorksWorker` 不得连接或调用 COM。连接成功后，Registry 还必须以实际 SolidWorks 版本逐 Handler 复核 evidence；当前只接受 `33.5.0`，不一致时在任何建模 API 调用前以 `feature_api_unverified` 拒绝。

## 2026-07-30 最终主流程验收回填

最终 E2E 目录为：

```text
output/solidworks/e2e/plate_basic_4holes/cad-e2e-20260730_090151_162-1b16c3982731423a8ae9a93f1db2dbbe/
```

同次报告与产物证明完整主流程已经关闭：

| 验收项 | 结果 |
|---|---|
| `e2e_execution_report.json` | `final_status=Passed`、`quality_gate_decision=Passed` |
| `package_quality_report.json` | `all_source_reports_passed=true`、`deliverable_status=Deliverable`、`final_status=Passed` |
| SLDPRT | 73830 bytes；SHA256 `045A5CF2C445F66B1CE2502065F84805A40CF217600DAD213C68D9EEFBA78612` |
| STEP | 26399 bytes；SHA256 `DCAB84553885D804AB961E4BBABB691A7A47C6962D030781211B7B777E1EF7BB` |
| Feature 执行 | 预期 7、执行 7；`all_features_executed`、`all_result_objects_validated`、`all_rebuilds_passed`、`all_geometry_changes_validated`、`artifacts_validated` 均为 `true` |
| 体积证据 | Boss、Cut、Hole 的三段体积与权威 diagnostic 完全一致 |
| 产物审查 | `review_active_source/feature_pipeline_plate_review_report.json` 为 100 分、`pass`；特征树为一个 `Extrusion` 加两个 `ICE`；人工确认两个孔 |

因此，四个精确 profile 已经真实经过 `ChiefEngineerOrchestrator → WorkflowEngine → Router → RealSolidWorksWorker → ArtifactValidator → Reviewer → QualityGate → ReleasePackage`，最终结论为 `Passed` / `Deliverable`。这不授权 `through_all`、`mid_plane`、任意面或原生孔 API，也不进入 V2.0-D。

## 最终主工作流程

证据经审查提升为 `verified` 后，仍必须从以下入口运行：

```text
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow <request.json>
```

最终链路必须保持：

```text
ChiefEngineerOrchestrator
→ WorkflowEngine
→ Router
→ RealSolidWorksWorker
→ Handler / Adapter
→ ArtifactValidator
→ Reviewer
→ QualityGate
→ ReleasePackage
```

只有同次主流程的 E2E 报告、真实产物、Validator、Reviewer、QualityGate 和发布包全部通过，才能声明可交付。直接调用 Adapter、Handler、Builder 或 diagnostic 都不能替代该结论。

## failure_stage

| `failure_stage` | 含义 |
|---|---|
| `feature_adapter_missing` | Worker 无法解析真实 Adapter。 |
| `sketch_execution_failed` | 草图进入、引用选择或草图事务失败。 |
| `sketch_geometry_create_failed` | line、rectangle 或 circle 几何创建失败。 |
| `extrude_execution_failed` | blind extrude 调用、返回或重建失败。 |
| `cut_execution_failed` | blind cut 调用、返回或重建失败。 |
| `hole_execution_failed` | 圆草图加 blind `FeatureCut4` 组合失败。 |
| `feature_result_invalid` | API 调用返回但结果对象、依赖或重建状态不合格。 |
| `feature_artifact_missing` | SLDPRT、STEP 或报告缺失、为空或不属于当次运行。 |
| `feature_api_unverified` | 所需 Adapter API 状态未达到 `verified`。 |

## self-check

```text
feature_adapter_layer_exists
feature_handler_no_direct_com_access
solidworks_feature_adapter_exists
sketch_real_execution_supported
extrude_real_execution_supported
cut_real_execution_supported
hole_real_execution_supported
feature_pipeline_end_to_end_supported
feature_result_validation_supported
feature_fake_success_guard_supported
v2_0_c_documented
markdown_chinese_check_passed
```

self-check 只验证架构、注入边界、纯逻辑 Handler、失败关闭和防假成功行为，不运行专用 diagnostic，不启动 SolidWorks。

其中 `sketch_real_execution_supported`、`extrude_real_execution_supported`、`cut_real_execution_supported`、`hole_real_execution_supported` 表示真实 Adapter 已提供对应受控方法。当前精确诊断轮廓另有 `verified` evidence；这些 self-check 字段本身仍不能扩大证据范围或覆盖 `feature_api_unverified` 生产门禁。

## 禁止事项

- 禁止 Handler 直接引用或调用 SolidWorks COM。
- 禁止未验证 API 进入生产真实执行。
- 禁止用 `SimpleHole2`、Hole Wizard 或名称近似的 API 替代选定孔组合策略。
- 禁止把 diagnostic `Passed`、候选产物或历史文件解释为主流程通过。
- 禁止接受空文件、陈旧文件、无效 Feature 或仅“未抛异常”的假成功。
- 禁止绕过 `run-cad-workflow`、ArtifactValidator、Reviewer 或 QualityGate。
- 禁止以 V2.0-C diagnostic 冒充 V2.0-E 的交付验收；V2.0-E 只能经统一 FeatureGraph、物理 STEP 内容门禁和受绑定候选证据继续执行。
