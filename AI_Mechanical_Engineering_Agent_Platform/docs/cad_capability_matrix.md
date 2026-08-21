# CAD 能力矩阵

## 目标

记录通用建模内核当前**真实**支持的 Feature 能力边界。本矩阵是判断一个零件族能否进入真实执行的唯一能力依据，不写计划中的能力，只写代码与证据已经支持的能力。

## 适用范围

新增零件族、扩展 FeatureGraph、评估真实执行可行性或审查 `V2.0-E` 之后的建模改动时，必须先读取本矩阵。

## 状态词表

| 状态 | 含义 |
|---|---|
| `Supported` | Handler 已实现，证据状态为 `Verified`，可进入真实执行 |
| `Partial` | Handler 已实现，但仅覆盖窄参数档案，超出档案即 fail-closed |
| `Unverified` | Handler 已实现，但缺少 Feature 级真实执行证据，真实执行被拒绝 |
| `Unsupported` | 尚无 Handler，`BuildPlanCompiler` 阶段即拒绝 |

## 能力矩阵

| Feature | Handler | SolidWorks API 状态 | 验证状态 | 当前限制 |
|---|---|---|---|---|
| `sketch` | `SketchHandler` | `CreateCircle`、`CreateLine`、`CreateCenterRectangle` 已实调 | `Supported` | 仅限标准基准面；仅直线、中心矩形、圆；不支持圆弧、槽、约束与尺寸 |
| `extrude_boss` | `ExtrudeBossHandler` | `FeatureExtrusion2` 已实调 | `Supported` | 仅 blind 单向拉伸；不支持 `mid_plane`、拔模、薄壁与多实体 |
| `extrude_cut` | `ExtrudeCutHandler` | `FeatureCut4` 已实调 | `Supported` | 仅 blind 切除；不支持 `through_all`、反向切除、薄壁与多实体范围 |
| `hole` | `HoleHandler` | `FeatureCut4` 配单圆草图 | `Partial` | 以圆草图加 blind 切除实现；依赖草图恰为一个等径圆；不使用 `SimpleHole2` 与 Hole Wizard |
| `revolve_boss` | `RevolveBossHandler` | `FeatureRevolve2` 已接线但无 Feature 级证据 | `Unverified` | 缺少独立 diagnostic 与几何复核，真实执行被证据策略拒绝；`shaft_basic` 因此 fail-closed |
| `revolve_cut` | 无 | 未接线 | `Unsupported` | 无 Handler，`FeatureHandlerRegistry` 无法解析 |
| `fillet` | 无 | 未接线 | `Unsupported` | 无 Handler；圆角涉及边选择，需要独立选择模型 |
| `chamfer` | 无 | 未接线 | `Unsupported` | 无 Handler；倒角与圆角共用边选择问题 |
| `linear_pattern` | 无 | 未接线 | `Unsupported` | 无 Handler；阵列需要特征引用与方向参考 |
| `circular_pattern` | 无 | 未接线 | `Unsupported` | 无 Handler；四孔目前由四个独立圆草图实现，不走阵列 |
| `mirror` | 无 | 未接线 | `Unsupported` | 无 Handler |

## 零件族能力映射

| 零件族 | 所需 Feature | 可否真实执行 | 原因 |
|---|---|---|---|
| `plate_basic_4holes` | `extrude_boss`、`extrude_cut` | 可以 | 所需 Feature 全部 `Supported` |
| `flange_basic` | `extrude_boss`、`extrude_cut` | 可以 | 所需 Feature 全部 `Supported` |
| `jacket_basic` | `extrude_boss`、`extrude_cut` | 受零件族证据限制 | Feature 层可用，但零件族结构化运行时证据仍为 pending |
| `shaft_basic` | `revolve_boss` | **不可以** | `revolve_boss` 证据状态为 `Unverified`，统一内核 fail-closed |

## 执行步骤

1. 新增或修改 FeatureGraph 前，先在本矩阵确认所需 Feature 的验证状态。
2. 若所需 Feature 为 `Unverified` 或 `Unsupported`，不得声明该零件族可真实执行。
3. 需要把 `Unverified` 升级为 `Supported` 时，必须先在诊断 Runner 中采集 Feature 级证据，再回填 Handler 的 `FeatureApiEvidence`。
4. 每次 Handler 证据状态变化，必须同步更新本矩阵。

## 验证标准

- 本矩阵中的 `验证状态` 必须与 `FeatureHandler` 的 `ApiEvidence.Status` 一致。
- 本矩阵中标记为 `Supported` 的 Feature，必须能通过 `FeatureExecutionEvidencePolicy.ValidateEvidence`。
- self-check 中 `cad_capability_matrix_exists=true`。

## 失败处理

若真实执行返回 `feature_api_unverified`，先在本矩阵定位该 Feature 的验证状态：

- 状态为 `Unverified` 或 `Unsupported`：属预期 fail-closed，不是缺陷，需先补证据。
- 状态为 `Supported` 但仍被拒绝：说明证据链失效，按 `src/Workers/SolidWorks/failure_repair.md` 进入 API Evidence Driven Repair Loop。

## 禁止事项

- 不得在本矩阵中写入尚未接线的计划能力。
- 不得为了让零件族通过而调高验证状态。
- 不得绕过 `FeatureHandler` 直接在零件族或 Worker 中实现 Feature。
- 不得以文件存在、COM 返回非空或独立 diagnostic 作为把状态升为 `Supported` 的依据。
