# 需求理解模块执行流程

## 目标与适用范围

本模块通过公开 `chief-engineer` 组织需求分析和工程规划。[V2.3 阶段](../../../docs/v2_3_engineering_planning_loop.md) 增加显式工程计划专用路线，复用中立模型合同和原 CAD 执行链。[V2.2-A 阶段](../../../docs/v2_2_a_task_approval_lifecycle.md) 的内部协作、任务审批恢复和业务收尾继续兼容；两条路线按显式输入选择，不新增产品运行时多 Agent 或 CAD API。

## 输入与输出

输入为不可替换的原任务 `AgentContext`；V2.3 用 `engineering_planning=true` 明确请求模型规划，不允许另一份 CAD 输入或操作覆盖。兼容审批恢复另需绑定当前 `workflow_id`、`approval_request_id`、`step_id` 的明确决定。宿主负责校验任务访问令牌，提交者字段仅作审计。输出为 `AgentOutput`、工程计划及映射产物或协作报告、有效步骤结果、待审批请求或失败信息，随后由任务服务更新状态并执行宿主 `QualityGate`。

## V2.3 工程规划执行步骤

1. `ChiefEngineerOrchestrator` 对显式工程规划请求创建 [EngineeringPlanningStep](../../PlatformCore/EngineeringPlanningStep.cs)，在原顺序工作流中通过 `ModelRuntime` / `IModelProvider` 进行 `DesignPlanning`，只取得待审查 JSON，不调用 Worker 或 CAD API。
2. [EngineeringPlanParser](../../PlatformCore/EngineeringPlanParser.cs) 对原始 JSON 严格校验字段、重复项、类型、深度和长度，再用既有转换器形成 [EngineeringPlan](../../DomainSchemas/EngineeringPlan.cs)。计划只开放现有 `CADModelSpec` 的受限参数投影，不接受模型图、执行选项或 API 建议。
3. [EngineeringPlanValidator](../../PlatformCore/EngineeringPlanValidator.cs) 检查原文逐字符绑定、决定、假设/缺参/风险、三项尺寸及各自单位、输出和原文附加要求。本阶段只支持外径140 mm、内径120 mm、长180 mm的 `jacket_basic` 纯几何档案，任何未能校验的要求拒绝。
4. 校验通过后将数值与原文完全一致的参数字符串规范化为原受证 `140`、`120`、`180` 文本，由现有夹套模板生成 `CADModelSpec` 和 `FeatureGraph`，经原编译器生成 `SolidWorksBuildPlan`，再通过原计划 Validator、Reviewer 和计划 `QualityGate`。规范化不能补缺参或改变尺寸；只有通过才创建 CAD 上下文，进入原 `SolidWorksMainWorkflowRunner`。
5. 原 Verified Handler、API Evidence、Worker、Adapter、ArtifactValidator、Reviewer、QualityGate 与发布链继续执行。计划通过不是 CAD 通过，最终真实交付还须同次双格式独立重开验收。

缺少 Provider、缺参、风险、假设、未知/重复字段、原文不匹配及附加要求均失败关闭；本阶段拒绝这些输入，不以 HumanApproval 绕过。命令与实际验证见阶段页；`--response` 是明确响应夹具重放，不能声称实时模型稳定性已验证。几何档案不包括材料、压力、承载或制造适用性验收。

## V2.2-A 兼容审批执行步骤

1. [ChiefEngineerAgent](agents/ChiefEngineerAgent.cs) 将首次请求交给 [ChiefEngineerOrchestrator](agents/ChiefEngineerOrchestrator.cs)，先拒绝无效显式 CAD 输入，再通过 `SequentialWorkflowEngine` 调用内部 Agent。
2. 等待人工审批时保存原任务上下文。恢复校验对应工作流，调用审批引擎原子匹配并消费具体审批；错误身份不执行下游。
3. 首次执行和恢复均调用 `CompleteWorkflowAsync`，生成累计协作报告，按实际审批裁决映射有效步骤输出。仅在内部流程通过且满足原有路由条件时进入既有 CAD 主流程，仍须遵守真实执行门禁。
4. 宿主任务服务读取最终 Agent 输出，经 `QualityGate` 更新任务状态。Microsoft Runtime 恢复复用该任务已保留的 advisory，不重新调用 LLM。

## 验证标准

按 [审查清单](review_checklist.md) 验证连续审批、错误身份、拒绝、后续失败、收尾路由与门禁，不启动 COM。构建、完整测试及自检由执行代理统一运行，实际结果登记阶段页；单进程内存状态不能作为持久恢复证据。

V2.3 另须验证模型合同调用、有效计划映射、非法计划零下游执行、显式公开路线、缺模型不回退及原 CAD 门禁保留；单元测试与 self-check 使用替身或 dry-run，不启动 COM。真实 CAD 由执行代理按阶段页单独运行和独立验收，实际模型稳定性必须使用真实 Provider 另行验证。五项 `engineering_planning_checks` 不代替完整测试、全局自检或真实产物证据。

## 常见失败与禁止事项

出现上下文丢失、重复等待或最终状态不一致时，保留审计并按 [失败修复](failure_repair.md) 定位。禁止以引擎批准结果直接替代最终 Agent 输出，禁止重跑已完成步骤、跳过原业务收尾、直接调用 Worker、降低既有能力基线或把平台通过解释为真实 CAD 可交付。

工程规划失败须保留具体原因与已生成原响应，禁止套用夹套样例默认值、无声删除原要求、把模型 JSON 当作执行证据、改旧 evidence 或绕过严格解析。未知尺寸或特征需要新的明确范围和 evidence，不能从当前三个受证尺寸外推。
