# V2.3 工程规划闭环

## 目标与适用范围

本阶段建立首个 `自然语言需求 → Engineering Agent → EngineeringPlan → CADModelSpec / FeatureGraph → Verified Worker → SolidWorks → QualityGate` 闭环。模型负责理解、规划和建议，平台负责严格校验、模板映射、受证执行和真实验收。

首个档案选择 [V2.2-D](v2_2_d_shaft_jacket_real_baseline.md) 已具备真实 evidence 的 `jacket_basic`：外径140 mm、内径120 mm、轴向长度180 mm，单实体同轴直筒，两端开口，输出 `SLDPRT` 和 `STEP`。壁厚由明确尺寸得到10 mm；尺寸、操作计划和证据仍受原准入策略约束。本阶段只验证几何，不声称材料、压力、承载或制造适用性已验收。

复用既有 `ModelRuntime` / `IModelProvider`、`CADModelSpec`、`FeatureGraph`、`SolidWorksBuildPlan` 和顺序执行引擎，不另建 CAD Schema，不扩展产品运行时多 Agent、并行或通用工作流语言，不修改来源绑定的 Handler、Adapter、旧 evidence 或能力基线。

## 输入与输出

输入为不可替换的原始自然语言和模型响应。公开 `chief-engineer` 仅在显式 `engineering_planning=true` 时进入此专用流程；没有该标志的兼容路线保留。CLI 以 `run-engineering-plan --input <需求文本>` 设置该标志，`--response <响应JSON>` 显式选择夹具重放，`--dry-run` 只验证模拟流程。

输出按实际完成程度产生：`model_response.json`、`model_invocation_report.json`、`engineering_plan.json`、`mapped_cad_model_spec.json`、`engineering_build_plan.json` 和 `engineering_execution_report.json`；调用报告记录本次用途、来源、原文、任务和调用次数。计划通过且真实执行成功后，另有原 CAD 主流程的 `SLDPRT`、有效 `STEP`、构建、几何、E2E、发布清单及包质量报告。失败计划不会得到 CAD 执行权限。

运行目录为 `output/solidworks/engineering/<run_id>/`。响应来源以 `plan_source=live` 或 `replay` 记录；`dry_run`、`real_cad_executed`、`final_status`、`deliverable_status`、`quality_gate` 和 E2E 路径分别留证。重放成功只证明给定响应经原模型合同后的校验、映射与执行，不证明真实模型能稳定理解自然语言。

## 新增 EngineeringPlan 合同

合同定义位于 [EngineeringPlan.cs](../src/DomainSchemas/EngineeringPlan.cs)。顶层只允许以下八个字段，全部必须存在；`cad_model_spec` 直接使用既有 `CADModelSpec` 类型，只开放当前档案所需的参数投影。

| 字段 | 类型与含义 | 当前准入规则 |
|---|---|---|
| `schema_version` | 字符串，合同版本 | 必须为 `2.3` |
| `requirement` | 字符串，模型所理解的原需求 | 必须逐字符等于输入，包括换行；不能改写后绑定 |
| `decision` | 字符串，模型规划决定 | 提示要求 `ready` 或 `reject`；平台只接纳 `ready` |
| `rationale` | 字符串，工程依据 | 必须非空，提示要求中文；不能构成 API evidence 或执行授权 |
| `cad_model_spec` | 既有 `CADModelSpec` 或 `null` | `ready` 计划必须提供下表中的受限参数投影 |
| `assumptions` | 字符串数组 | 有任何假设即拒绝，不能用默认样例值补缺参 |
| `missing_parameters` | 字符串数组 | 有任何缺参即拒绝；平台另行核对原文，不能只信模型自报 |
| `risks` | 字符串数组 | 有任何风险即拒绝；重新澄清并规划后再提交 |

| `cad_model_spec` 字段 | 当前要求 |
|---|---|
| `model_id` | 1至80字符，仅字母、数字、下划线和连字符；平台映射后使用任务派生标识 |
| `model_type` | 精确为 `jacket_basic` |
| `unit` | 精确为 `mm` |
| `parameters` | 仅 `outer_diameter_mm`、`inner_diameter_mm`、`length_mm` 三项非空字符串；数值必须依次为140、120、180且与原文一致 |
| `material` | 空字符串；本阶段没有材料属性验收 |
| `output_requirements` | 仅且完整包含 `SLDPRT`、`STEP` |

严格解析器先检查原始 JSON，再使用既有 CAD 转换器，避免未知字段、别名或缺省值被兼容反序列化静默接受。顶层和 CAD 对象未知或重复字段、必要字段缺失、参数重复、类型错误、空响应、超过65536字符或深度超过20均拒绝；假设、缺参、风险数组最多32项且每项必须为非空字符串。模型不得输出草图、图、API、执行选项或附加 CAD 字段。

保存的 `engineering_plan.json` 使用同一受限投影序列化，重新读取仍能通过严格解析；映射后的完整 CAD 合同另存为 `mapped_cad_model_spec.json`，不能把它误作未经校验的模型原响应。

参数字符串先解析为有限数值并逐项核对原文与受证尺寸，再规范化到现有 evidence 使用的 `140`、`120`、`180` 文本。例如模型给出 `140.0` 或 `1.8e2`，仅在数值分别与原文140和180完全一致时才能映射；不能据此补缺参、改变单位、改变尺寸或更新旧证据绑定。

原需求必须逐项明确三个尺寸和各自的 `mm` / 毫米单位，包含夹套及 `SLDPRT` / `STEP`。平台检查每项在原文中唯一出现且与计划一致；除当前支持的几何描述、尺寸和输出词外，剩余无法校验的要求拒绝。例如附加螺纹、材料、压力要求不能被模型删去后默默建模。当前支持受限语法，不能把这个档案称为任意自然语言理解能力。

## 端到端执行路径

1. CLI 或公开 `chief-engineer` 接收自然语言，以 `engineering_planning=true` 明确选择工程规划路线。与另一份 `cad_model_spec_json`、参数更新或操作覆盖并存的输入拒绝，不能拼接两条执行来源。
2. `EngineeringPlanningStep` 在既有顺序工作流中通过 `ModelRuntime` / `IModelProvider` 发起 `EngineeringModelPurpose.DesignPlanning` 请求。提示只允许待审查合同，不暴露工具或 SolidWorks API；没有可用 Provider 时返回 `engineering_model_not_configured`。
3. [EngineeringPlanParser](../src/PlatformCore/EngineeringPlanParser.cs) 严格校验 JSON 结构；[EngineeringPlanValidator](../src/PlatformCore/EngineeringPlanValidator.cs) 校验原文绑定、决定、缺参/假设/风险、尺寸单位、输出与受证档案范围，再调用现有 `CADModelSpecValidator`。
4. 平台调用现有 `PartFamilyGenericModelFactory.CreateJacketBasic` 生成草图和 `FeatureGraph`，保留原 `BuildPlanCompiler` 路线生成 `SolidWorksBuildPlan`，再由 `SolidWorksBuildPlanValidator` 与 `SolidWorksBuildPlanReviewer` 检查。模型不指定节点、选择标记、API 实参或执行选项。
5. 计划经已有 `DefaultGatekeeper` / `GateDecisionPolicy` 校验通过后才能交接 `ToCadContext`，进入原 `SolidWorksMainWorkflowRunner`。失败计划终止该路线，不回退默认零件，也不通过 HumanApproval 放宽结构、工程规则或 evidence。
6. 原 `Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter` 验证来源、输入、执行计划和版本后执行；原 ArtifactValidator、几何验收、Reviewer、QualityGate、发布清单和包质量链继续决定可交付性。模型响应或计划门禁通过都不能替代这些结果。
7. 执行代理独立重开同次 `SLDPRT` 和 `STEP`，核对单实体、140/120/180 mm尺寸、同轴、两端开口、包络和独立体积，复核发布包中的对应文件。只有原质量链与真实产物复验均通过，才登记真实 CAD 验收通过。

## 使用的测试用例与执行命令

自然语言样例为 [v2_3_jacket_requirement.txt](../examples/v2_3_jacket_requirement.txt)：

```text
建模圆筒夹套，外径140 mm，内径120 mm，轴向长度180 mm。输出SLDPRT和STEP。
```

对应固定响应见 [v2_3_jacket_plan_response.json](../examples/v2_3_jacket_plan_response.json)，用于可复现合同和真实 CAD 执行验收。测试替身及固定响应均通过既有 `IModelProvider` 合同；它们不能作为实时模型稳定性的替代证据。

新增行为回归集中在 [V23EngineeringPlanningTests.cs](../tests/PlatformSelfCheck.Tests/V23EngineeringPlanningTests.cs)，共68项专项，覆盖确定性模板映射、计划产物重新严格解析、数字文本规范化、非法结构/参数、原文缺参与附加要求、缺 Provider、公开入口阻断、上下文冲突以及原 CAD 质量门禁保留。实际完整测试计数列于下表，不用测试文件存在代替执行结果。

执行代理从项目根目录统一运行，定向输出使用本轮新目录，不能覆盖历史报告：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test --no-build
dotnet run --no-build --project src/Interfaces/CliHost -- self-check --output output/validation/v2_3_engineering_planning
dotnet run --no-build --project src/Interfaces/CliHost -- run-engineering-plan --input examples/v2_3_jacket_requirement.txt --response examples/v2_3_jacket_plan_response.json --dry-run
dotnet run --no-build --project src/Interfaces/CliHost -- run-engineering-plan --input examples/v2_3_jacket_requirement.txt --response examples/v2_3_jacket_plan_response.json
dotnet run --no-build --project src/Interfaces/CliHost -- run-engineering-plan --input examples/v2_3_jacket_requirement.txt --dry-run
```

最后一条使用实时模型来源并以 dry-run验证规划；当前检查未发现有效 `AI_*` 模型配置，实际返回缺配置拒绝，不能使用重放冒充实时模型运行。补齐真实 Provider 后，须另行多次调用验证模型稳定性，再去掉 `--dry-run` 完成实时规划到真实 CAD 验收。真实 CAD 命令仍须满足本地执行配置和安全开关；单元测试、CI、self-check 不启动 COM。独立重开使用现有只读探针和几何判定，不另造简化验收器。

## 验证标准与实际结果

本阶段新增 `engineering_planning_checks` 字典，保留既有 `schema_version=2.2-a-task-approval` 和旧字段。每项结果必须有实际行为依据，缺失或失败不能合并成通过：

- `engineering_plan_model_contract_executed`：既有模型合同被调用，并可定位计划产物。
- `engineering_plan_maps_existing_cad_schema`：有效计划映射到已有受限夹套图与构建计划。
- `engineering_plan_public_route_dry_run_completed`：公开任务路线完成 dry-run，保持不启动真实 CAD。
- `engineering_plan_invalid_inputs_blocked`：结构异常及不完整原需求被阻断。
- `engineering_plan_missing_runtime_rejected`：缺少 Provider 被阻断，不能回退默认模型。

| 验证项 | 本轮实际结果 | 证据 |
|---|---|---|
| 完整 build | 0警告、0错误 | [最终日志](../output/validation/v2_3_engineering_planning/build_clean_final.log) |
| 完整 test | 865/865通过，0失败、0跳过；含68项 V2.3 专项 | [最终完整测试日志](../output/validation/v2_3_engineering_planning/test_full_final.log)，[TRX记录](../output/validation/v2_3_engineering_planning/tests/v23_full_final.trx) |
| 全局 self-check | `Passed`，中文检查为 `true` | [收尾报告](../output/validation/v2_3_engineering_planning/self_check_closure/reports/platform_self_check_report.json) |
| 五项工程规划行为自检 | 全部 `true` | 同一报告的 `engineering_planning_checks` 五项 |
| 响应重放 dry-run | `Passed`，真实执行为 `false`，交付为 `NotDeliverable` | [同次工程报告](../output/solidworks/engineering/engineering-20260930_052957_295-6bab328a6ee248beb59541429cce8d41/engineering_execution_report.json)，`plan_source=replay` |
| 响应重放真实 CAD | `Passed` / `Deliverable`，真实执行为 `true` | [同次工程报告](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/engineering_execution_report.json)，`plan_source=replay`、模型合同调用1次；原发布清单 `all_source_reports_passed=true` |
| `SLDPRT` / `STEP` 独立重开 | 两者 `validation.is_valid=true`，几何验收通过 | [独立验收](../output/validation/v2_3_engineering_planning/cad_acceptance/independent_geometry_acceptance.json)、[原生探针](../output/validation/v2_3_engineering_planning/cad_acceptance/native_probe.json)、[STEP探针](../output/validation/v2_3_engineering_planning/cad_acceptance/step_probe.json) |
| 实时模型稳定生成计划 | 未完成；缺配置被正确拒绝 | [实时来源拒绝报告](../output/solidworks/engineering/engineering-20260930_053021_847-d0c411b4e4b34f079a7022580a5b946f/engineering_execution_report.json)：`engineering_model_not_configured`，模型调用0次，未执行真实 CAD |
| 历史 evidence 与基线保留 | 383份受保护历史文件全部一致；受证源码与基线无差异 | [保护复核](../output/validation/v2_3_engineering_planning/protected_after.json)，原指纹算法对本轮映射计划的 evidence 前置检查通过 |

验收必须区分五项行为自检、完整测试、全局自检、真实 CAD 和模型稳定性。干运行可以完成模拟闭环，但 `NotDeliverable` 保持；固定响应可以驱动真实 CAD，但不能把规划响应来源写成真实模型。

真实运行使用 `SolidWorks 31.5.0`。本次原生 [jacket_basic.SLDPRT](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/artifacts/jacket_basic.SLDPRT) 为77461字节，[jacket_basic.STEP](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/artifacts/jacket_basic.STEP) 为17191字节；同次 [E2E报告](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/reports/e2e_execution_report.json)、[发布清单](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/release_manifest.json) 和 [包质量报告](../output/solidworks/engineering/engineering-20260930_054001_065-585eca8296a34df58dbba6ee05a12e3a/reports/package_quality_report.json) 均支持本轮质量裁决。

双格式独立重开均确认单实体、外径140 mm、内径120 mm、长180 mm、内外同轴和两端开口。理论体积为735132.6809400115 mm³，原生实测735132.680940013 mm³，STEP实测735132.6809400123 mm³。17份发布文件及复核副本在重开前后摘要完全一致，见 [文件身份复核](../output/validation/v2_3_engineering_planning/cad_acceptance/identity_after.json)。这些几何和身份结果不包括材料与质量属性验收，不能把探针的默认密度或质量值当作材料证据。

工程计划映射后的构建计划与真实执行计划使用原 `PartFamilyProductionEvidencePolicy.ComputePlanFingerprint` 复核，二者指纹完全一致，见 [计划执行身份](../output/validation/v2_3_engineering_planning/cad_acceptance/plan_execution_identity.json)，`passed=true`。本轮不重新实现摘要算法，也不靠替换执行计划放宽受证档案。最终完整记录集中于 [验证汇总](../output/validation/v2_3_engineering_planning/verification_summary.json)。

## 常见失败、拒绝场景与修复步骤

| 场景 | 阻断位置与处理 |
|---|---|
| 模型未配置或 Provider 失败 | `engineering_model_not_configured` 或 `engineering_plan_generation_failed`；核对模型配置并重新规划，不能回退四孔板或重放 |
| 非 JSON、围栏、未知/重复字段、缺字段、错误类型 | `engineering_plan_structure_invalid`；保留原响应，修正提供商输出合同后复验 |
| 原文缺尺寸/单位、重复尺寸、模型填入样例默认值或改变原文 | `engineering_plan_requirement_mismatch`、`engineering_plan_requirement_incomplete` 或 `engineering_plan_source_invalid`；修订需求并重新规划 |
| 假设、缺参、风险或 `decision` 不为 `ready` | `engineering_plan_uncertain` 或 `engineering_plan_not_ready`；拒绝，不用审批绕过 |
| 其他尺寸、族名、输出、材料、任意图或执行选项 | 受证范围、输出或范围检查拒绝；不能以既有 API evidence 外推新档案 |
| 螺纹、压力等当前无法校验的附加要求 | `engineering_plan_requirement_unsupported`；不能静默删除这些要求 |
| 与另一路 CAD 输入或操作覆盖冲突 | `engineering_plan_input_conflict`；保留唯一计划来源后重新提交 |
| 模板映射、编译或现有计划校验失败 | `engineering_plan_mapping_failed` 或原校验问题；读取实际计划及现有模块规则，做最小修复 |
| 原 Worker / evidence / 几何 / QualityGate 失败 | 保留原 `failure_stage` 和证据；按 CAD 模块及 Worker 修复手册隔离验证，不能在工程规划层放行 |

失败记录必须包含失败阶段、直接原因、证据路径、修复策略和下一步验证命令。模块细则见 [需求理解失败修复](../src/Modules/RequirementUnderstanding/failure_repair.md)；API 失败继续已有 Evidence Driven Repair Loop。修复后复现原拒绝反例，再运行必要回归；禁止只改报告、摘要、`size_bytes`、容差或历史 evidence 消除失败。

## 下一步建议与禁止事项

优先补齐实际模型配置，针对同一受证夹套档案多次调用真实 Provider，记录合法表达变体、结构合格率、原文与参数一致性及拒绝率，补齐本轮尚未完成的实时模型稳定性证据。通过后再逐个引入已有受证尺寸档案；每个档案都保留原质量链及同次真实验收。

禁止提前扩展任意自然语言、任意尺寸、产品运行时多 Agent、并行或通用工作流语言；禁止让模型直接访问 SolidWorks API、绕过原 Worker 和门禁、以 HumanApproval 放宽不可验证计划、声称材料/制造验收已完成，或把重放、dry-run和历史 CAD 证据当作当前实时模型与真实交付的完整证明。
