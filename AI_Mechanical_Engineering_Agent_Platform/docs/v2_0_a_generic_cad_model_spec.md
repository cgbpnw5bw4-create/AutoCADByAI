# V2.0-A 通用 CADModelSpec 与 FeatureGraph

## 目标

V2.0-A 把参数化零件描述统一到与具体 CAD 系统解耦的 `CADModelSpec`，由 `SketchDefinition` 描述草图，由 `FeatureDefinition` 描述特征节点，再由 `FeatureGraph` 校验依赖并交给 `BuildPlanCompiler` 生成确定性的 `SolidWorksBuildPlan`。

本阶段的唯一通用执行范围是：

```text
CADModelSpec Schema
→ SketchDefinition / FeatureDefinition
→ FeatureGraph 校验与拓扑排序
→ BuildPlanCompiler
→ SolidWorksBuildPlan
→ Validator / Reviewer
→ dry-run
```

`FeatureGraph` 是 Schema 到 BuildPlan 和 dry-run 的唯一特征来源。V1.9 已验收的三个固定零件族真实 Builder 保持可用，但 V2.0-A 不实现通用 Feature Handler，也不把任意 `CADModelSpec` 或任意特征图转换为真实 COM 操作。通用特征到真实 SolidWorks API 的执行延期到 V2.0-B。

## 适用范围

本规范适用于：

- `plate_basic_4holes`、`flange_basic`、`shaft_basic` 三个零件族的通用 Schema 迁移。
- JSON 序列化与 V1.7/V1.8 输入兼容。
- 草图实体、约束、尺寸和构造中心线的结构化表达。
- 十类特征定义、特征依赖校验、稳定拓扑排序和 BuildPlan 编译。
- 默认 self-check、单元测试和 dry-run 的无 COM 验证。

本阶段不包含装配体、BOM、批量队列、任意零件族真实建模、通用特征 COM Handler 或 V2.0-B。

## canonical CADModelSpec

### 核心字段

| JSON 字段 | 类型 | 语义与要求 |
|---|---|---|
| `model_id` | 字符串 | 模型稳定标识，不能为空；也用于安全输出文件名。 |
| `model_type` | 字符串 | 已注册零件族或模型类型；三个迁移族分别为 `plate_basic_4holes`、`flange_basic`、`shaft_basic`。 |
| `unit` | 字符串 | 参数单位；当前 Validator 接受 `mm`、`cm`、`m`、`in`。 |
| `parameters` | 字典 | 通用参数源；值保持为字符串，由 Validator 按 Schema 解析，编译器不得擅自改义。 |
| `reference_geometry` | 字典 | 稳定引用名称到基准面、基准轴或面的映射。 |
| `sketches` | `SketchDefinition[]` | 草图集合；`sketch_id` 必须非空且唯一。 |
| `features` | `FeatureDefinition[]` | 特征图节点集合；至少包含一个节点。 |
| `material` | 字符串 | 当前材料标识；不在 V2.0-A 内引入材料库求解。 |
| `output_requirements` | 字符串数组 | 需要的产物与报告；为空时编译器采用 SLDPRT、STEP 和构建报告。 |
| `drawing_requirements` | 字典 | 工程图要求的结构化占位；本阶段不据此生成新的真实工程图能力。 |
| `execution_options` | 字典 | dry-run、输出目录等执行选项；真实 Worker 选择仍由 V2.0 统一运行策略控制。 |

`title`、`description` 和顶层 `constraints` 是可选说明元数据。`feature_options` 只为旧参数化输入保留，不替代数组形式的 canonical `features`。

### 兼容规则

反序列化器继续接受旧字段 `id`、`part_type`、`dimensions`、`materials`，以及对象形式的旧 `features` 参数字典。读取后必须归一化到 `model_id`、`model_type`、`parameters`、`material` 和 `feature_options`；新的特征图必须使用数组形式的 `features`。

兼容入口不能改变以下约束：

- 未注册类型仍返回 `unsupported_part_type`，不能回退为 plate。
- 旧 `dimensions` 只能补充尚未出现在 `parameters` 中的键。
- 对象形式的旧 `features` 只能进入 `feature_options`，不能伪装为 `FeatureDefinition`。
- 一旦三族工厂生成 canonical `sketches` 与 `features`，BuildPlan 只能由这些结构化定义编译。

### 最小示例

```json
{
  "model_id": "plate-demo",
  "model_type": "plate_basic_4holes",
  "unit": "mm",
  "parameters": {
    "length_mm": "160",
    "width_mm": "80",
    "thickness_mm": "12",
    "hole_diameter_mm": "10",
    "hole_count": "4"
  },
  "reference_geometry": {
    "top_plane": "TopPlane",
    "top_face": "TopFace"
  },
  "sketches": [
    {
      "sketch_id": "plate_base_sketch",
      "reference_plane": "TopPlane",
      "entities": [
        {
          "entity_id": "plate_base_rectangle",
          "entity_type": "rectangle",
          "parameters": {
            "length_mm": "160",
            "width_mm": "80"
          },
          "construction": false,
          "execution_order": 1
        }
      ],
      "constraints": [
        {
          "constraint_id": "plate_length",
          "constraint_type": "dimensional",
          "entity_ids": ["plate_base_rectangle"],
          "value": "160",
          "parameters": {
            "parameter": "length_mm"
          }
        }
      ],
      "dimensions": {
        "length_mm": "160",
        "width_mm": "80"
      },
      "execution_order": 1
    }
  ],
  "features": [
    {
      "feature_id": "plate_base_extrude",
      "feature_type": "extrude_boss",
      "parameters": {
        "depth_mm": "12"
      },
      "dependencies": [],
      "referenced_sketches": ["plate_base_sketch"],
      "referenced_features": [],
      "execution_order": 1,
      "target_reference": null
    }
  ],
  "material": "Q235",
  "output_requirements": ["SLDPRT", "STEP", "build_report.json"],
  "drawing_requirements": {},
  "execution_options": {
    "dry_run": "true"
  }
}
```

## SketchDefinition

### 草图字段

`SketchDefinition` 包含：

- `sketch_id`：草图稳定标识。
- `reference_plane`：草图基准引用，不能为空。
- `entities`：`SketchEntity` 数组。
- `constraints`：`SketchConstraint` 数组。
- `dimensions`：草图级尺寸参数字典。
- `execution_order`：可选正整数，仅用于确定稳定顺序。

`SketchEntity` 包含 `entity_id`、`entity_type`、`parameters`、`construction` 和 `execution_order`。当前支持八类实体：

```text
line
rectangle
circle
arc
slot
point
centerline
construction_centerline
```

`construction_centerline`，或 `construction=true` 的 `centerline` / `line`，由编译器识别为构造中心线并生成独立 `CreateCenterLine` 操作。

### 草图约束

`SketchConstraint` 包含 `constraint_id`、`constraint_type`、`entity_ids`、可选 `value` 和 `parameters`。当前支持：

```text
coincident
horizontal
vertical
parallel
perpendicular
tangent
concentric
equal
distance
diameter
radius
midpoint
symmetric
fixed
dimensional
```

约束引用的每个 `entity_id` 必须存在于同一草图。V2.0-A 只验证并编译约束描述，不声称十五类约束已经具备完整真实 COM Handler。

## FeatureDefinition

### 字段

每个 `FeatureDefinition` 包含：

- `feature_id`：非空且全图唯一的稳定节点标识。
- `feature_type`：十类受支持特征之一。
- `parameters`：特征参数字典。
- `dependencies`：显式上游特征标识。
- `referenced_sketches`：引用的草图标识。
- `referenced_features`：额外引用的特征标识。
- `execution_order`：可选正整数，必须唯一且与依赖方向一致。
- `target_reference`：可选目标面、轴或其他稳定引用。

`dependencies` 与 `referenced_features` 合并、去重后形成图依赖，不能引用不存在的特征。

### 十类特征

| `feature_type` | BuildPlan operation | V2.0-A 语义 |
|---|---|---|
| `extrude_boss` | `ExtrudeBoss` | 描述凸台拉伸。 |
| `extrude_cut` | `CutExtrude` | 描述拉伸切除。 |
| `revolve_boss` | `RevolveBoss` | 描述旋转凸台。 |
| `revolve_cut` | `RevolveCut` | 描述旋转切除。 |
| `fillet` | `AddFillet` | 描述圆角。 |
| `chamfer` | `AddChamfer` | 描述倒角。 |
| `hole` | `AddHoleWizardHole` | 描述孔特征，不代表真实 Hole Wizard 已通用化。 |
| `linear_pattern` | `LinearPattern` | 描述线性阵列。 |
| `circular_pattern` | `CircularPattern` | 描述圆周阵列。 |
| `mirror` | `MirrorFeature` | 描述镜像特征。 |

此映射只产生 BuildPlan operation。除三个 V1.9 固定零件族已有的专用真实 Builder 外，不得把映射表解释为十类特征已经可以真实执行。

## FeatureGraph

### 缺失依赖

节点依赖或引用的特征不存在时返回 `feature_dependency_missing`。特征引用的草图不存在，或草图缺少 `reference_plane` 时返回 `sketch_reference_missing`。两类失败都必须在 Worker 调度前结束。

### 环检测

`FeatureGraph` 使用拓扑排序检测环。无法消费全部节点时返回 `feature_dependency_cycle`，并在 issues 中列出仍有入度的节点。禁止通过删除依赖、使用输入数组顺序或进入 Worker 来掩盖环。

### 稳定顺序

可执行节点按以下优先级稳定选择：

1. `execution_order`，未指定者排在显式顺序之后。
2. 原始输入索引。
3. `feature_id` 的不区分大小写顺序。

显式 `execution_order` 必须为正整数、全图唯一，并且每个依赖的顺序必须小于下游节点；否则返回 `invalid_feature_order`。拓扑顺序是 BuildPlan 特征操作的唯一顺序来源。

## BuildPlanCompiler

### 执行步骤

1. 检查 `model_id`、`model_type`、`unit` 和非空 `features`。
2. 检查 `sketch_id`、草图顺序、基准引用、实体类型、约束类型和约束实体引用。
3. 运行 `FeatureGraph.ValidateAndSort()`。
4. 检查十类特征映射和 `referenced_sketches`。
5. 首次需要草图时生成 `CreateSketch`；构造中心线另生成 `CreateCenterLine`。
6. 按拓扑结果生成特征 operation，并把草图、特征依赖转换为 operation 依赖。
7. 对终止几何 operation 生成 `SavePart`，其后生成 `ExportStep`。
8. 返回 `SolidWorksBuildPlan`、预期产物、材料、输出要求、工程图要求和执行选项。

编译器是描述性纯转换，不连接 SolidWorks。任何编译失败都不能进入 Fake 或 Real Worker。

## 三个零件族迁移

| 零件族 | canonical 草图 | canonical 特征图 |
|---|---|---|
| `plate_basic_4holes` | `plate_base_sketch`、`plate_hole_sketch` | `plate_base_extrude` → `plate_hole_cut` |
| `flange_basic` | `flange_outer_sketch`、`flange_inner_sketch`、`flange_bolt_sketch` | `flange_body_extrude` → `flange_inner_cut` → `flange_bolt_holes` |
| `shaft_basic` | `shaft_profile_sketch`，含半截面与构造中心线 `shaft_axis` | `shaft_revolve` |

每个零件族仍先运行自己的参数 Validator。通过后，`PartFamilyGenericModelFactory` 把族参数迁移为 canonical 草图和特征图；`IPartFamilyDefinition.GenerateBuildPlan` 统一调用 `BuildPlanCompiler`。不允许定义再维护第二套手写 BuildPlan operation。

三个 V1.9 `IPartFamilyBuilder` 和真实 SolidWorks 证据保持有效，只服务于已经验收的固定族语义。V2.0-A 的任意新草图、任意特征组合和十类通用特征仍只能到 dry-run，不能声称已经真实执行。

## failure_stage

| `failure_stage` | 触发条件 | 修复方向 |
|---|---|---|
| `invalid_cad_model_spec` | 核心字段、特征数组、草图标识或单位非法 | 修正 Schema；不得补默认几何。 |
| `feature_id_missing` | 特征缺少稳定标识 | 补齐非空 `feature_id`。 |
| `duplicate_feature_id` | 多个节点使用同一标识 | 为每个节点分配唯一标识并修正依赖。 |
| `sketch_reference_missing` | 草图基准为空或特征引用未知草图 | 修正 `reference_plane` 或 `referenced_sketches`。 |
| `feature_dependency_missing` | 节点引用未知上游特征 | 补齐节点或修正依赖标识。 |
| `feature_dependency_cycle` | 特征依赖构成有向环 | 重新设计依赖；不得强制排序。 |
| `unsupported_sketch_entity` | 实体未命名或类型不受支持 | 使用受支持实体或延期扩展 Schema。 |
| `unsupported_constraint` | 约束类型不受支持或引用未知实体 | 修正约束及实体引用。 |
| `unsupported_feature_type` | `feature_type` 不在十类映射中 | 使用受支持类型或延期到后续版本。 |
| `invalid_feature_parameter` | 特征参数无法通过语义校验 | 修正参数、单位和引用，不在 Handler 内猜值。 |
| `invalid_feature_order` | 顺序非正、重复或早于依赖 | 修正 `execution_order` 或删除多余显式顺序。 |
| `build_plan_compile_failed` | 已校验结构在编译时仍无法生成计划 | 保留原 issues，修复编译边界并重跑 dry-run。 |

零件族参数阶段仍可返回 `unsupported_part_type`、`missing_required_parameter` 和 `invalid_parameter_value`。这些失败与上述图失败都必须发生在真实 Worker 之前。

## 自检字段

```text
generic_cad_model_spec_v2_supported
sketch_definition_supported
sketch_constraints_supported
feature_definition_supported
feature_graph_supported
feature_graph_cycle_detected
missing_feature_dependency_rejected
build_plan_compiler_supported
plate_uses_generic_feature_graph
flange_uses_generic_feature_graph
shaft_uses_generic_feature_graph
no_part_specific_logic_in_real_worker
v2_0_a_documented
markdown_chinese_check_passed
```

## 验证标准

必须运行项目规定的 build、test 和 self-check。默认 self-check、单元测试与 dry-run 均不得连接 COM 或启动 SolidWorks。

通过标准：

- canonical Schema 可以 JSON 往返，旧字段可以受控读取。
- 草图实体与约束检查通过，非法引用在 Worker 前拒绝。
- 有效特征图产生稳定拓扑顺序。
- 缺失依赖、环和非法顺序返回精确 `failure_stage`。
- 三个零件族都从 canonical FeatureGraph 编译 BuildPlan，并通过 dry-run。
- `RealSolidWorksWorker` 只经 Registry 解析专用 Builder，不包含三个零件族字符串分支。
- 上述自检字段全部为 `true`。

本次文档任务不运行真实 CAD。Markdown 中文检查应由只读等价扫描或父流程统一 self-check 完成，不能为验证文档而启动 SolidWorks。

## 禁止事项

- 禁止从族参数直接手写第二套 BuildPlan operation，绕过 `FeatureGraph`。
- 禁止把 BuildPlan operation 名称当作真实 COM Handler 已实现。
- 禁止把 V1.9 专用真实 Builder 泛化为任意 `CADModelSpec` 真实执行。
- 禁止在图失败后调用 Fake 或 Real Worker。
- 禁止用大型 `switch(model_type)` 或 `switch(feature_type)` 堆叠零件族执行逻辑。
- 禁止在 V2.0-A 引入装配体、BOM、批量任务队列或通用真实特征执行。
- 本阶段完成后停在 V2.0-A，不进入 V2.0-B。
