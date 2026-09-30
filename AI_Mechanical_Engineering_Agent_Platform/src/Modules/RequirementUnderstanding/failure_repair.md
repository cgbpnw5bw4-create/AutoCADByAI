# 需求理解模块失败修复

## 目标与适用范围

本手册用于 [V2.3 工程规划闭环](../../../docs/v2_3_engineering_planning_loop.md) 的模型响应、计划校验、映射与交接失败，以及 [V2.2-A 任务审批闭环](../../../docs/v2_2_a_task_approval_lifecycle.md) 的首次执行、等待、恢复及收尾失败。目标是保留原需求、任务身份和证据，按直接原因做最小修复。

## 输入与输出

输入为原请求、任务状态、当前审批身份、`failure_stage` 或 `failure_reason`、协作报告和审计；令牌仅用于授权查询，不写入诊断说明。输出必须包含失败阶段、直接原因、证据路径、修复策略和下一步验证命令。

V2.3 还需保留实际 `plan_source`、原响应、工程计划、校验问题及已有映射产物。不能把模型配置、凭据或提供商私有错误原文写入业务报告；结构错误响应仍作为诊断输入，不执行其中的命令、API 或修改建议。

## V2.3 工程规划失败处理

| 失败或现象 | 查证与最小修复 |
|---|---|
| `engineering_model_not_configured` | 核对现有模型配置入口及 `IModelProvider`；保留缺配置拒绝，不回退默认 CAD，不将 `--response` 重放记作实时模型 |
| `engineering_plan_generation_failed` | 根据异常类型检查 Provider、响应读取或新产物目录；禁止泄露凭据或重用旧产物冒充成功 |
| `engineering_plan_structure_invalid` | 对比原响应与八个顶层字段和六个 CAD 字段，检查未知/重复字段、缺字段、类型、深度及长度；修正输出合同后重新规划 |
| `engineering_plan_source_invalid` | 核对 `schema_version=2.3`、非空依据及原文逐字符绑定，包括换行；不能改原文或报告消除失配 |
| `engineering_plan_not_ready` 或 `engineering_plan_uncertain` | 解决原需求中的缺参、假设或风险，重新生成计划；不能把数组清空或改 `decision` 后批准 |
| `engineering_plan_model_missing` | 检查拒绝决定与 CAD 投影是否一致；不能用受证样例填充缺模型 |
| `engineering_plan_requirement_mismatch` 或 `engineering_plan_requirement_incomplete` | 检查三项尺寸在原文是否唯一明确、含各自毫米单位，并明确夹套及两种输出；需修订需求后重新规划 |
| `engineering_plan_requirement_unsupported` | 查原文剩余未能校验的附加要求；不能静默删除螺纹、压力、材料或其他要求 |
| `engineering_plan_evidence_scope_mismatch`、`engineering_plan_profile_unsupported`、`engineering_plan_scope_unsupported` 或 `engineering_plan_output_unsupported` | 对比当前140/120/180 mm纯几何档案与输出；未经新证据不扩展尺寸、图、执行选项或验收声明 |
| `engineering_plan_input_conflict` | 查显式 CAD JSON、参数更新、操作覆盖及共享状态；保留唯一来源后重提，不能让旧输入替换已审计划 |
| `engineering_plan_mapping_failed` 或原计划校验问题 | 核对模板生成图与现有编译器、Validator、Reviewer 的实际问题；不能另建 CAD Schema 或绕过原图校验 |
| 计划通过但 Worker / evidence / 几何 / QualityGate 失败 | 转到 CAD 模块及 Worker 的 `failure_repair.md`，保留原阶段与同次产物，按现有隔离取证流程处理 |

每个拒绝反例须验证下游 Worker 未执行；模型输出被拒绝后重试或审批不能带着旧 CAD 上下文继续。真实 CAD 失败不得在规划层重写成通过。

## V2.2-A 兼容审批修复步骤

| 失败或现象 | 查证与修复 |
|---|---|
| `chief_approval_context_not_pending` | 核对原任务与 `internal-collaboration-{taskId}`，检查是否已终止或进程已重启；不得创建空上下文冒充恢复 |
| `workflow_approval_identity_mismatch` 或 `workflow_approval_not_pending_or_mismatched` | 对比本次等待的三个标识；保留当前有效审批，拒绝旧提交，不自动改身份后批准 |
| `task_approval_in_progress` 或 `task_approval_not_pending` | 凭任务令牌查询现状；已消费审批不能重放，执行中等待其真实结果 |
| `runtime_approval_advisory_missing` | 检查同任务 advisory 是否保留及 Runtime 模式；不得为恢复而重新调用 LLM 生成替代内容 |
| 恢复后再次异常等待、假成功或遗漏收尾 | 核对 `CompleteWorkflowAsync`、有效审批结果映射、原 CAD 路由条件及宿主 `QualityGate`，保留前后结果定位最小分支 |
| `task_execution_exception` 或 `task_execution_cancelled` | 保留异常类型、任务终态和审计；HTTP 断开不等于已接受动作取消，不能据此重复执行 |

## 验证标准

修复后在不依赖 COM 的夹具中重现原反例，检查审批项是否被正确消费、下游执行次数、累计报告、任务终态和质量门禁。执行代理从项目根目录运行 `dotnet test`，再运行 `dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/v2_2_a_task_approval`，在阶段页记录实际结果；命令未完成时不得写通过。

V2.3 使用当前阶段的新输出目录 `output/validation/v2_3_engineering_planning`，先复现结构、缺参、原文失配、未知要求和缺 Provider 拒绝，再运行完整 build、test、self-check。修复涉及实际执行结果时，由执行代理再次运行正式工程入口及同次 `SLDPRT` / `STEP` 独立重开；纯平台夹具通过不能代替实测。所有计数、退出码、路径与未完成项回填 V2.3 阶段页。

## 常见失败与禁止事项

常见误修是把中间批准映射成整个任务完成、丢弃上下文后重建任务或将历史审批改成当前身份。禁止这些做法；禁止输出访问令牌、改旧 evidence、降低既有能力基线。遇到真实 CAD 证据失败，转到对应 Worker 手册，保留本轮平台结果与 CAD 失败的区别。

工程规划另禁止默认补尺寸、重写原文、删除风险、借 HumanApproval 放行无效计划、以模型 API 建议代替 evidence 或将重放冒充实时模型稳定性。保持来源绑定 Handler、Adapter、历史审查与能力基线原样。
