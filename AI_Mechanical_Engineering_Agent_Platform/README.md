# AI Mechanical Engineer Platform

当前开发入口为 [V2.2-D 轴类与夹套真实能力基线](docs/v2_2_d_shaft_jacket_real_baseline.md)。本轮限定真实轴类/夹套、两格式独立几何验收及证据复验，保留平台架构和历史记录。下述 V2.2-C 统计属于上一阶段。 V2.2-D 完整测试783/783通过，全局 self-check 恢复为 Passed；真实能力限定于本阶段三组受证轴类/夹套输入。


## 目标与适用范围

本项目面向机械设计、CAD 自动化和后续整机设计。核心分工是：**强模型负责工程理解、规划和决策；平台负责工程约束、确定性执行、真实 CAD/API 能力和结果验收。**

架构逐步收敛为少量强 Agent 与多个确定性 Worker。当前保留已验证的 SolidWorks 主流程、特征注册、受证 Handler、Adapter 和质量链；通用装配、并行工作流及制造性专项验收仍需逐项开发和验证。

默认工程入口先做确定性规划输入校验，具体 CAD 计划继续由原 Skill/Validator 生成和校验。新默认没有调用四个模拟占位角色，也不把输入校验声称为已生成工程计划；旧四角色流程仍可显式选择。

本轮没有新增自然语言自动转换为正式工程计划的完整闭环，模型建议仍须落入既有结构化计划并接受平台校验。

## 输入输出与核心架构

输入为工程需求、约束和结构化 `CADModelSpec`；输出为工程计划、CAD 产物、执行报告、质量裁决及来源明确的验收证据。

```text
Frontier Model / Future AGI
→ Engineering Agent
→ Engineering Plan
→ Deterministic Workers
→ Verified CAD/API Layer
→ SolidWorks
→ QualityGate
```

这是职责方向，不表示未来通用智能、任意 CAD 图、整机设计或全部专项门禁已实现。现有 `CADModelSpec`、`FeatureGraph` 与 `SolidWorksBuildPlan` 承载结构化建模计划；正式执行须经过 `WorkflowEngine`，遵守以下能力准入顺序：

```text
Feature Registry → Verified Handler → SolidWorks API Evidence → Worker → SolidWorks Adapter
```

该顺序表示能力发现、验证、证据授权和受控执行责任，具体复核仍由既有执行管线完成。模型生成的 API 名称、参数、计划和恢复建议均不能替代 API evidence；Worker 输出必须经过 Validator、Reviewer 与 QualityGate。

## 执行步骤

1. 先读 [项目入口](docs/index.md)、[执行规范](docs/project_execution_standard.md)、[Codex 执行协议](docs/codex_execution_protocol.md) 和 [版本索引](docs/version_stage_index.md)。
2. 核对 [当前架构](docs/architecture.md)、[Agent 职责](docs/agent_standard.md) 与 [质量门禁](docs/quality_gate.md)。当前 CAD 恢复与验证见 [V2.2-D 阶段页](docs/v2_2_d_shaft_jacket_real_baseline.md)，架构改动保留于 [V2.2-B 阶段页](docs/v2_2_b_frontier_model_architecture.md)。
3. 涉及真实 CAD 时读取对应模块和 Worker 的 `api_evidence.md`、`failure_repair.md`、最新诊断与验收报告，再进入受控主流程。
4. 在本目录运行构建、完整测试和定向自检：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check --output "output/validation/v2_2_d_shaft_jacket/manual_self_check_$(Get-Date -Format yyyyMMdd_HHmmssfff)"
```

自检、单元测试、CI 与 `dry_run=true` 不得启动 SolidWorks。本地交互式真实主流程遵循现有默认启用策略及失败关闭条件。每次验证使用新的输出目录，保留此前报告；当前实际结果见 V2.2-D 阶段页，上述命令按时间生成新的自检目录。

## 名称与兼容范围

正式展示名称统一为 `AI Mechanical Engineer Platform`。底层 `AI_Mechanical_Engineering_Agent_Platform` 目录与 `.sln`、稳定程序集和命名空间保持原名，避免破坏构建入口、反射加载、工具路径、源码指纹与历史证据绑定。`AgentRuntime.Microsoft` 保留为兼容适配项目名，模型接入职责逐步由中立 `ModelRuntime` / `IModelProvider` 承载。

历史阶段文档、审查记录和 API evidence 保留当时名称及路径，不因品牌统一而重写历史结论。

## 验证标准、常见失败与禁止事项

阶段回归、完整测试、全局 self-check 与真实 CAD 验收分别报告。测试通过或模型自述不能授权未验证 API，文件存在或 COM 返回成功不能代替同次几何与发布包验收。最新统计和未解决失败以当前阶段页的实际日志为准。

2026-09-14 的 V2.2-C 最终构建为 0 警告、0 错误，完整测试 684/684 通过，失败与跳过均为 0，原 8 项证据失败全部消除。四方向与七组生产候选完成两格式独立几何验收，参数更新正式主流程为 `Passed` / `Deliverable`，发布件重开核对通过；见 [验证汇总](output/validation/v2_2_c_cad_baseline/verification_summary_20260914.json) 与 [实物复核](output/validation/v2_2_c_cad_baseline/main_release_geometry_unique_20260914/verification.json)。

同次全局 self-check 仍为 `Failed`：夹套生产证据与真实执行未激活，轴类真实工作流自检亦未通过。阶段恢复不等于所有平台能力已通过。2026-09-24 保留文件与源码指纹复核通过，重新构建 0 警告、0 错误，完整测试仍为 684/684 通过；本次 self-check 退出码 2，恢复相关门禁与中文检查通过，全局仍保留上述既有缺口。见 [本次验证汇总](output/validation/v2_2_c_cad_baseline/verification_summary_20260924.json)。

遇到证据长度、hash、源码或版本不匹配时，保留失败并按取证协议核查或重新采集。禁止绕过 Worker、直接执行模型生成的 API 调用、改写旧 evidence、降低能力基线或修改只读 `reviewrep`。
