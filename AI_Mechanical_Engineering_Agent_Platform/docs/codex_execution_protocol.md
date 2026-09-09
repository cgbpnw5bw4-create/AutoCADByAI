# Codex 执行协议

Codex 不允许在未读取相关说明文件的情况下直接修改核心逻辑。

## 当前阶段与授权范围

本轮执行 [V2.2-A 平台任务生命周期与审批闭环](v2_2_a_task_approval_lifecycle.md)。用户已授权自主选择并开发下一阶段；前轮 `R01`–`R05` 完成后，本轮实施 `R06` 的具体审批身份、原子匹配防重放、任务状态和宿主入口，以及恢复后的原业务收尾与 `QualityGate`。上轮“不进入 V2.1-C”不构成本轮独立平台阶段的重复确认要求；历史 CAD 能力与证据边界继续保留。

## 任务输入必须包含

- 当前版本阶段。
- 本轮任务目标。
- 必须先读取的说明文件。
- 允许修改的目录。
- 禁止修改的目录。
- 不要做的事。
- 成功标准。
- 必须运行的命令。
- self-check 字段。
- 如果失败，应该读取哪个 `failure_repair.md`。
- 如果是 API 失败，必须执行 API Evidence Driven Repair Loop。
- 完成后输出摘要。

## 执行流程

1. 读取 `AGENTS.md` 和 `docs/index.md`。
2. 检查 `docs/codex_agent_registry.md`，确认是否已有 canonical agent 覆盖当前职责。
3. 检查 active `.codex/agents/` 是否只包含 canonical agents，若只是职责扩展，不创建新 Agent。
4. 将新要求优先写入对应 Skill 或 Markdown，例如 `solidworks-api-repair`、`quality-review`、`markdown-docs-standard`、`api_evidence.md`、`review_checklist.md` 或本协议。
5. 读取项目执行规范和当前阶段索引。
6. 读取当前模块文档。
7. 读取最新报告和相关测试。
8. 做最小修改。
9. 更新 self-check 字段和测试。
10. 运行 build、test、self-check。
11. 输出已验证结果和剩余风险。

## 失败流程

出现失败时先定位 `failure_stage`。如果涉及 API，必须查官方资料、本地参考资料和 evidence 报告，再提出候选策略。不能只记录 `Failed` 后停止。

## V2.1-B 执行补充

目标是四类通用孔增强，适用定义、验证、Adapter 与文档，不改变 Handler 架构。输入为当前图、协议、前置审查及 API evidence；输出为标准化请求、失败阶段、几何检查、回归和中文文档。

执行时先读 `docs/v2_1_b_hole_features.md`，确认复杂特征库审查 `HEAD=bbeafc9` 无 Blockers；全部 Improvements 保留于 `docs/technical_debt.md`，不顺带实施。既有六个 canonical agent 足以覆盖职责，不新增 Hole Agent。四个样例默认 dry-run，实际参数与面引用在 Worker 前校验，未验证 API 在连接前拒绝，缺独立几何不得通过 QualityGate。

验证须运行完整 build、test、self-check 并逐项回填十二个孔字段，分开说明总体失败与阶段通过。API 失败按孔手册隔离取证，禁止复制脚本、Handler COM、Cut 冒充攻丝、旧 evidence 换绑或进入 V2.1-C。

## V2.1-B 可靠性补强执行范围（2026-09-09）

目标是在保持现有架构边界与孔能力准入条件的前提下，完成 [本轮架构审查](2026_09_09_architecture_review.md) `R01`–`R05` 的最小修复。适用范围为结构化 CAD 输入、`WorkflowEngine` 重试和人工审批、孔自检及 CLI 输出定向。输入为已确认反例与当前协议；输出为对应行为修复、回归、自检诊断和本次验证记录。

执行时先列问题与触发，再实施输入失败关闭、有效重试上限、预取消审批无损、累计恢复历史、自检异常隔离及 `self-check --output <目录>`；三个并行审查职责的发现由 canonical `docs_writer` 汇总，代码与测试由对应 canonical 职责处理。复用既有六类 Agent，不新增同职责配置。

验收运行 build、完整 test 和定向 self-check，分开记录修复回归、阶段字段、中文检查及全局状态。字段缺失、样例异常和报告未生成都作为验证失败处理，不得合成为通过。常见失败先按报告中的阶段、来源和直接原因定位；API/真实 CAD 问题继续走对应模块 `failure_repair.md` 与 API Evidence Driven Repair Loop。

禁止本轮顺带开放显式孔真实执行、修改绑定 evidence 或只读 `reviewrep`、降低能力基线、改写历史验证结论或进入 V2.1-C。任务生命周期与宿主审批、类型交接、阵列修复重采证据和逐孔 Reader 已列后续顺序，未纳入本轮实现的项目必须保留为待开发。

## V2.2-A 执行范围

目标是完成平台任务生命周期和显式审批闭环，适用 `WorkflowEngine` 合同及审批存储、任务存储、`AgentGatewayHost` 与 `chief-engineer` 原后处理。输入为原请求与上下文、当前具体审批身份和决定；输出为一致状态、查询与提交结果、完整历史和统一质量裁决。

先复用 canonical agents 并读取 [阶段合同](v2_2_a_task_approval_lifecycle.md)，再完成身份与原子消费、任务状态、宿主接口及恢复收尾。代码和行为回归由对应执行职责实施，文档职责只同步本轮入口及阶段页；构建、测试和自检由执行代理统一运行，不以文档草案代替验证结果。

验收必须覆盖错误身份无损拒绝、预取消保护、重复与并发审批最多一次恢复、旧审批不能作用于新等待、批准后失败或再次等待、拒绝和要求修订、任务查询一致性，以及 `chief-engineer` 原后处理和宿主 `QualityGate` 的实际执行。API 合同已在阶段页登记，实际验证结果继续单独回填，不提前宣称通过。

任务查询 `GET /tasks/{taskId}` 与审批提交 `POST /tasks/{taskId}/approvals` 均使用 `X-Task-Access-Token`；令牌仅由首次消息响应提供，是任务范围访问能力，`submitted_by` 仅作审计。缺失/错误令牌或未知任务统一 `404`，合法访问下的审批业务冲突返回 `409`，`200 accepted` 仍须核查任务最终状态。

首次消息同步执行，不提供后台任务队列；审批接受后连接断开不取消已接受动作，应凭已有令牌查询结果，禁止重放。任务及审批仅驻留单进程内存，Microsoft advisory 按任务保留，恢复不再次调用 LLM。当前 schema 为 `2.2-a-task-approval`；`task_lifecycle_tracked`、`task_approval_roundtrip_supported`、`task_access_token_required`、`workflow_approval_identity_bound` 仅反映内置行为检查，完整测试与全局结果另列。

本轮禁止实施 `R07` 类型交接、持久化、`R08`–`R11` CAD 修复或取证，不启动真实 CAD。它们继续保留为后续事项及相应失败关闭条件，不阻塞本轮无 COM 平台开发。禁止修改历史架构审查结论、`reviewrep` 或 evidence，禁止降低既有能力基线；出现新的状态、审批或门禁失败时保留反例与审计，先做最小修复再复验。
