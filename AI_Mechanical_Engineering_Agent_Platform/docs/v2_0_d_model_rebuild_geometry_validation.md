# V2.0-D 模型重建与几何验证系统

## 阶段目标

V2.0-D 在 V2.0-C 已完成的 Feature Handler 与 Feature Adapter 真实执行边界之上，建立参数驱动的模型重建和真实几何验证闭环。该阶段的通过标准不是 COM 调用未抛异常，也不是 SLDPRT、STEP 或 JSON 文件存在，而是更新后的模型经受控主流程重建后，其真实几何、特征结果、报告语义和 QualityGate 都通过。

本文件定义 V2.0-D 的实现和验收门禁。未完成本文件所列 build、test、self-check 与真实主流程验收前，不得声称 V2.0-D 已关闭，更不得进入 V2.0-E。

## 范围与边界

- 不新增 CAD Feature 类型，不新增零件族，不新增同职责 Codex Agent。
- 不绕过 `FeatureHandlerRegistry`、`IFeatureHandler`、`FeatureExecutionPipeline`、Validator、Reviewer 或 QualityGate。
- `ModelUpdateService` 与 `GeometryValidator` 是纯逻辑层，不得直接读取或写入 COM。
- 新增的 COM 读取只能收敛在独立 `ISolidWorksGeometryReader` / `RealSolidWorksGeometryReader` 边界内；它只读取本次受控会话中的真实模型或本次生成的 SLDPRT，不扫描历史 latest。
- 不使用直接 Builder、Adapter、Handler、SmokeRunner 或 diagnostic 作为最终验收入口。最终验收只能使用 `run-cad-workflow`。
- V2.0-C 已绑定的 Feature 证据源码不能被静默改写后继续使用旧 evidence；若修改受绑定源码，必须按证据流程重新诊断、回填修订并重新进行主流程验收。

## 参数重建闭环

```text
CADModelSpec
  -> ModelUpdateService
  -> BuildPlanCompiler
  -> FeatureExecutionPipeline
  -> RealSolidWorksWorker
  -> SolidWorks Rebuild
  -> GeometryReader
  -> GeometryValidator
  -> Validator / Reviewer / QualityGate
  -> ReleasePackage
```

`ModelUpdateService` 接收基线 `CADModelSpec` 与参数补丁，计算并记录：

- `old_parameters`：基线参数快照；
- `new_parameters`：规范化后的更新参数快照；
- `changed_features`：由 FeatureGraph 直接影响节点及其依赖闭包计算的既有特征标识；
- `rebuild_result`：初始建模、更新建模、最终重建和几何报告的结果摘要。

参数更新必须先反映到既有 `SketchDefinition` 与 `FeatureDefinition` 的受控参数映射，再交给 `BuildPlanCompiler`。仅修改 `CADModelSpec.Parameters` 而没有同步草图或 Feature 参数，属于 `parameter_geometry_mismatch` 风险，不能作为参数驱动重建成功。

`FeatureExecutionPipeline` 必须继续经 `FeatureHandlerRegistry.Resolve()` 和 `IFeatureHandler.ExecuteAsync()` 执行既有 FeatureGraph；它不得创建新的 Feature 类型，也不得为了参数更新回退到零件族专用 Builder。

## GeometryReader 与 GeometryValidator

`RealSolidWorksGeometryReader` 在 Worker 的受控会话内读取真实 SolidWorks 输出并生成纯数据 DTO。`GeometryValidator` 只消费该 DTO、输入参数和 FeatureGraph 期望，不持有 COM 对象。

基础几何至少包括：

- BoundingBox：六个有限值、min/max 顺序和正跨度；BoundingBox 只用于边界有效性，不能单独证明精确长度。
- 平面四孔板的精确外包络：先读取真实 `IBody2.GetVertices` / `IVertex.GetPoint` 顶点坐标；顶点路径不可用时才回退 `IBody2.GetExtremePoint`。这两条读取都不可用时必须以 `geometry_read_failed` 阻断，不能降级为近似 BoundingBox。
- Solid Body 数量：空、异常或非预期数量必须失败。
- Volume：真实实体体积必须为有限正数，并与期望体积及可用的 MassProperty 交叉核验。
- MassProperty：如 API 可用，记录质量和体积；可用但与实体体积矛盾时必须失败。不可用时必须显式记录 `not_available`，不能伪造数值。

Feature 结果至少包括：

- Sketch 是否存在；
- Extrude、Cut 是否在真实特征树和既有 Handler 结果中存在且无错误；
- Hole 是否存在，并以真实圆柱面或等价真实几何测得孔径；
- `length_mm`、`width_mm`、`thickness_mm` 使用精确实体测量验证；`diameter_mm` / `hole_diameter_mm` 使用实际孔径或轴径验证。

对于当前真实运行的 `GetTypeName2` 结果，`ProfileFeature` 映射为 Sketch、`Extrusion` 映射为 Extrude、`ICE` 映射为 blind Cut。验证器结合这些真实类型、既有 Handler 运行报告和圆柱孔面，而不是依赖本地化树节点名称。

任何新的读取 API 必须具有独立 V2.0-D API evidence、源码修订、失败记录和最小诊断。不能把近似 BoundingBox、Feature 非空返回值或文件大小误写为精确几何证明。

## 四孔板约束

`plate_basic_4holes` 的四孔验收必须证明真实模型中存在四个符合孔径的孔。模型名、`hole_count=4`、Feature 数量、文件存在或单次体积下降都不足以证明四孔。

四孔表达只能复用已经存在的 `sketch`、`extrude_boss`、`extrude_cut`、`hole` 等既有 Feature 类型及其 Handler；不得为凑孔数新增 CAD Feature 类型、零件族或绕开 Handler。若既有 V2.0-C 精确 profile 只授权单圆草图，则不得静默扩大为多圆、pattern、`TopFace`、`through_all`、`mid_plane` 或 Hole Wizard。超出已验证 profile 时必须以 `feature_api_unverified` 阻断，先完成独立 evidence 流程后再进行真实主流程验收。

## 报告契约

每个 V2.0-D 真实执行必须产生并参与同次 ReleasePackage 的源报告。

`geometry_validation_report.json` 至少包含：

- `model_id`
- `input_parameters`
- `measured_geometry`
- `expected_geometry`
- `deviations`
- `passed_checks`
- `failed_checks`
- `failure_stage`
- `final_status`

`rebuild_report.json` 至少包含：

- `model_id`
- `old_parameters`
- `new_parameters`
- `changed_features`
- `rebuild_result`
- 初始与更新 GeometryReport 的同次路径或等价可审计摘要
- `failure_stage`
- `final_status`

两份报告均必须是本次请求生成、非空、可解析，且 `final_status=Passed`、`failure_stage` 为空后，才可使最终包得到 `all_source_reports_passed=true` 和 `deliverable_status=Deliverable`。报告写入、复制、解析或语义验证失败均不得返回假成功。

## 失败阶段与修复边界

| failure_stage | 含义 | 首先处理方式 |
|---|---|---|
| `rebuild_failed` | 最终 SolidWorks Rebuild 未通过 | 读取本次 rebuild 与 Feature 报告，确认失败后停止交付，不回退 Fake。 |
| `geometry_read_failed` | GeometryReader 无法获得真实、完整、可解析的测量结果 | 读取 API evidence 和本次受控会话日志；不在 Validator 或 Handler 中补 COM。 |
| `bounding_box_invalid` | BoundingBox 缺失、非有限、顺序错误或跨度无效 | 核对单位、六值和实体存在性，不能以默认零值通过。 |
| `volume_validation_failed` | Body/Volume/MassProperty 不可用、无效或与期望不一致 | 先检查真实实体和体积来源，再检查期望计算与公差。 |
| `parameter_geometry_mismatch` | 更新参数没有反映到真实长度、孔径或轴径 | 修复 ModelUpdateService 的既有参数映射，不直接改 COM 尺寸。 |
| `feature_missing_after_rebuild` | Sketch、Extrude、Cut 或 Hole 在重建后缺失或无有效结果 | 回到既有 FeatureGraph、Handler 和 evidence，不新增替代 Feature。 |
| `geometry_report_failed` | 几何或重建报告不能写出、解析或纳入发布包 | 先修复报告路径、语义和 QualityGate 接入，不继续调试 CAD API。 |

## 参数修改验收

`examples/parameter_update_plate.json` 的受控场景为：

1. 基线 `plate_basic_4holes`：160 x 80 x 12 mm。
2. 同次参数更新：200 x 100 x 15 mm；未修改的孔径和孔数也必须在报告中明确保留。
3. 初始与更新均通过 FeatureGraph 编译、FeatureHandler 执行、SolidWorks Rebuild、GeometryValidator、Artifact/Geometry Validator、Reviewer 与 QualityGate。
4. 最终报告必须证明尺寸已经变化、FeatureGraph 未破坏、真实四孔和参数一致性通过。

真实验收命令只能是：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/parameter_update_plate.json
```

最终同次输出至少包含 SLDPRT、STEP、`geometry_validation_report.json` 和 `rebuild_report.json`，并以 E2E 报告、package quality report 和 QualityGate 的 `Passed` / `Deliverable` 结论收尾。

## self-check 与阶段退出

以下字段必须全部为 `true`：

- `model_rebuild_pipeline_exists`
- `parameter_update_supported`
- `solidworks_rebuild_supported`
- `geometry_validator_exists`
- `bounding_box_validation_supported`
- `volume_validation_supported`
- `parameter_geometry_match_supported`
- `rebuild_failure_detected`
- `geometry_report_generated`
- `v2_0_d_documented`
- `markdown_chinese_check_passed`

还必须运行：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

以上命令、真实 `run-cad-workflow` 验收、同次报告和 QualityGate 全部通过前，V2.0-D 不得关闭，也不得进入 V2.0-E。
## V2.0-D 三圆切除证据补充

`plate_basic_4holes` 的前三个孔使用既有 `extrude_cut` Handler；因此 V2.0-D 不能把 V2.0-C 的单圆诊断静默扩展为多圆 profile。`RealSolidWorksWorker` 只会在 `FeatureHandlerRegistry.ValidateForRealExecution()` 已通过之后、建立 SolidWorks COM 会话之前调用 `V20DThreeCircleCutEvidencePolicy`。该纯证据预检要求：

- 仅为 `plate_basic_4holes` 的 `feature_handler_graph` 路径；操作顺序必须仍是 sketch、boss、sketch、cut、sketch、simple-hole，不能新增 Feature 或零件族；
- `cut_profile` 必须恰有三个直径 10 mm 的圆，`hole_profile` 必须恰有第四个直径 10 mm 的圆；四个圆心必须是既有 `ModelUpdateService` 的 20 mm 边距映射；
- 仅接受本轮 `160 x 80 x 12 mm` 初始状态和 `200 x 100 x 15 mm` 更新状态，盲切深度保持为板厚的两倍，`through_all=false`；
- `examples/parameter_update_plate.json`、V2.0-D 受绑定源码和候选诊断报告都必须匹配固定 SHA-256；候选报告还必须证明真实 33.5.0 会话、三圆切除体积变化、第四孔、非空 SLDPRT/STEP 和无 Feature 错误。

本补充绑定的候选诊断是 `output/solidworks/features/20260803_064124_6127412/feature_execution_report.json`。它的状态始终只是 `CandidatePassed` / `NotDeliverable`，不是最终验收；最终交付仍只能由 `run-cad-workflow` 生成同次 GeometryReport、RebuildReport 和 QualityGate 结论。任一 profile、源码、输入或候选诊断不匹配都返回 `feature_api_unverified` 并在 COM 连接前停止。
