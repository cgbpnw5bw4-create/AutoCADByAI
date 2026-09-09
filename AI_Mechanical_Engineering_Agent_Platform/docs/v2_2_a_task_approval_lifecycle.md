# V2.2-A 平台任务生命周期与审批闭环

## 当前完成状态

V2.2-A 的单进程平台闭环已实现并通过本轮行为验证：新增 `53/53` 测试通过，实际 HTTP 检查 `32/32` 通过，四个阶段自检字段均为 `true`。完整测试 `551` 项中 `543` 通过、`8` 失败；失败项与上一轮相同，仍由既有 CAD 证据不一致引起，全局自检保持 `Failed`。本阶段可作为后续类型交接与持久化开发的基础，不能据此放行真实 CAD。

## 目标与阶段授权

本阶段承接 V2.1-B 可靠性补强已完成的 `R01`–`R05`，最小必要开发范围为 [2026-09-09 架构审查](2026_09_09_architecture_review.md) 的 `R06`：为具体审批请求建立身份和原子匹配，补齐任务状态、查询与宿主审批入口，并确保恢复结果仍通过 `chief-engineer` 原有后处理与 `QualityGate`。

用户本轮已授权自主选择并开发下一阶段，当前选择为 **V2.2-A 平台任务生命周期与审批闭环**。上轮“不进入 V2.1-C”是上轮任务的范围限制，本轮平台开发已有新授权，无需再次确认；本阶段也不以 V2.1-C 命名或扩大 CAD 功能。阶段声明不改变历史审查结论、真实 CAD 准入或证据状态。

## 适用范围与暂不实施项

适用于 `WorkflowEngine` 审批合同与存储、平台任务存储、`AgentGatewayHost` 任务查询和审批提交，以及 `chief-engineer` 恢复后的业务收尾与质量裁决。六个 canonical 开发 Agent 已覆盖职责，继续复用现有配置。

`R07` 的上游业务结果类型交接、跨进程或跨重启持久化、执行中取消后的持久恢复留待后续。`R08`、`R09` 阵列绑定与方向、`R10` 显式孔与逐孔 Reader、`R11` 既有 CAD 证据不一致保持未解决，不阻塞本轮不依赖 COM 的平台开发；对应真实执行继续失败关闭。本轮不修改 CAD Adapter、evidence 或只读 `reviewrep`，不降低既有能力基线；四个新行为字段作为 `true` 保护项追加到基线。

需求理解模块的执行与审查入口为 [执行流程](../src/Modules/RequirementUnderstanding/execution.md)、[失败修复](../src/Modules/RequirementUnderstanding/failure_repair.md)、[宿主及流程接口证据](../src/Modules/RequirementUnderstanding/api_evidence.md) 和 [审查清单](../src/Modules/RequirementUnderstanding/review_checklist.md)。

## 输入与输出

输入为宿主接收的公开 Agent 请求、任务标识、任务访问令牌、待审批请求的具体身份、审批决定与提交者信息，以及同一工作流的上下文、已完成步骤和审计。任务访问令牌是任务范围的持有者访问能力，持有该令牌即可查询和提交该任务审批；它不等于用户账户身份。`submitted_by` 仅用于审计，不是身份认证依据。

输出为可查询的任务状态、当前待审批请求、审批接受或拒绝原因、恢复后的最终 Agent 输出、统一质量门禁结论及完整审计。审批请求被消费不等于整个任务通过，最终任务状态必须反映恢复后的后续步骤、`chief-engineer` 后处理与 `QualityGate` 裁决。

## 运行合同

| 合同边界 | 行为要求 | 不满足时的结果 |
|---|---|---|
| 具体审批身份 | 每次等待均产生可区分的审批身份；提交同时绑定任务或工作流及本次审批，不能仅凭工作流标识批准当前任意步骤 | 缺失、过期或不匹配身份被拒绝，当前有效待审批项保持可用 |
| 原子匹配与消费 | 身份校验与消费在存储中是一个原子操作；并发提交同一审批最多一个成功；旧审批不能消费后续新审批 | 重复、并发失败或跨审批重放不执行下游，不覆盖已生效决定 |
| 预取消保护 | 保留 V2.1-B 已建立的提交前取消保护，在消费待审批项前检查预取消 | 待审批请求、步骤历史和审计保持可继续处理 |
| 任务生命周期 | 从任务创建、开始执行到等待、恢复及终止，查询状态与实际流程一致；异常也须形成可追踪任务结果 | 不得把已完成或已失败任务遗留为初始状态，也不得把等待任务标记完成 |
| 宿主查询与提交 | 查询与审批都须携带该任务访问令牌；授权通过后，无待审批项、非法提交及身份冲突提供业务拒绝原因 | 未知任务与无访问权统一返回 `404`；业务冲突返回 `409` |
| 恢复业务收尾 | 审批恢复继续原工作流的剩余步骤，再复用 `chief-engineer` 首次执行的原有后处理路径 | 不绕过业务收尾，也不从头重复已完成步骤 |
| 统一质量裁决 | 恢复后的 Agent 输出仍进入宿主原有 `QualityGate`；批准仅改变等待节点，不替代最终裁决 | 后续失败、打回或再次等待必须如实反映到任务状态和响应 |
| 审计与历史 | 保留前轮累计步骤与审计语义，并能关联当前审批身份、提交及决定 | 不能通过重建空任务丢弃历史或让历史审批作用于另一步骤 |

下列 API、字段和状态已按实现合同回填；行为测试、完整测试及全局自检结果仍以本页末尾的执行记录为准。

## API 与自检字段登记

| 接口或字段组 | 本轮用途 | 最终合同 |
|---|---|---|
| 首次消息响应 | 返回首轮执行结果与后续任务访问能力 | `POST /agents/{agentId}/message` 响应新增 `task_id`、`task_status`、`pending_approval`、`task_access_token`、`failure_stage` |
| 任务查询接口 | 查询任务状态、最新结果及当前待审批请求 | `GET /tasks/{taskId}`；携带 `X-Task-Access-Token` 请求头；返回 `task`、`pending_approval`、`failure_stage`、`result` |
| 宿主审批提交接口 | 提交具体审批身份和明确决定，返回受理结果与恢复后的任务状态 | `POST /tasks/{taskId}/approvals`；携带同一访问请求头；请求体和状态码见下文 |
| 审批存储原子操作 | 按工作流、本次审批身份和步骤匹配并消费 | `IWorkflowApprovalStore.TryTake(workflowId, approvalRequestId, stepId, out pending)`；在存储原子边界内匹配三个标识，最多消费一次 |
| 阶段 self-check 字段 | 反映本轮实际行为检查 | `schema_version=2.2-a-task-approval`；四个布尔字段为 `task_lifecycle_tracked`、`task_approval_roundtrip_supported`、`task_access_token_required`、`workflow_approval_identity_bound` |

### 访问令牌与查询响应

`task_access_token` 只在首次消息响应返回。调用方应保存该值，并在后续查询或审批中使用 `X-Task-Access-Token` 请求头。查询响应的 `result` 嵌套原消息响应结构，但不再次提供令牌；审批响应也不返回令牌。丢失首次令牌后，当前接口不支持仅凭 `task_id` 补取。

`GET /tasks/{taskId}` 成功返回 `200`；未知任务、缺少令牌或错误令牌统一返回 `404`。`task` 包含任务标识、状态和时间；`pending_approval` 描述当前待审批请求，`failure_stage` 描述平台失败阶段，`result` 是已形成的业务结果，执行中可能尚无结果。不能根据一次查询没有 `result` 推断任务已失败。

### 宿主审批请求与返回

从当前 `pending_approval` 取得标识后提交以下请求体，示例占位值必须替换为同一次等待的实际值：

```json
{
  "workflow_id": "<当前待审批请求的 workflow_id>",
  "approval_request_id": "<当前待审批请求的 approval_request_id>",
  "step_id": "<当前待审批请求的 step_id>",
  "decision": "Approve",
  "submitted_by": "<审计记录中的提交者>",
  "comment": "<审批说明，可省略>"
}
```

`decision` 支持 `Approve`、`Reject`、`RequestRevision`。任务访问令牌放在 `X-Task-Access-Token` 请求头，不放进业务请求体。提交必须同时匹配当前 `workflow_id`、`approval_request_id` 和 `step_id`，且不能替换原任务输入。

| HTTP 状态 | 语义 | 调用方应读取的结果 |
|---|---|---|
| `200` | `accepted=true`，该审批提交已被接受 | 响应中的 `task` 是任务查询结构，仍须读取其 `task.status` 和业务 `result`；批准后可能失败、被打回或再次等待 |
| `409` | 具体审批身份不匹配、重复提交、无待审批项、审批处理中或业务提交字段非法 | `accepted=false` 与 `failure_reason`；当前任务快照可供判断，不再次执行已消费的审批 |
| `404` | 未知任务、缺少或错误访问令牌 | 不区分任务不存在与无访问权，不提供该任务内容 |

HTTP `200` 表示审批受理，不表示任务最终 `Passed`。审批批准仅解除对应等待节点，最终任务状态仍由后续流程、原有后处理和 `QualityGate` 决定。

### 任务状态、同步执行与恢复边界

任务状态包括 `Created`、`Running`、`Passed`、`Rejected`、`Failed`、`Cancelled` 和 `WaitingForHumanApproval`；旧枚举 `WaitingForReview` 保留。首次执行或审批恢复进入 `Running`，完成后按实际结果进入终态或再次等待；人工拒绝及要求修订停止后续步骤。

首次消息是同步执行请求，没有后台队列；`task_id` 和访问令牌随首轮结果返回，不能把此入口当作立即返回任务句柄的异步提交。已取得令牌的调用方可以在审批恢复期间查询到 `Running`。

审批被接受后，即使 HTTP 连接断开也继续执行收尾，不以连接断开取消已接受的业务动作。调用方应凭已有令牌查询任务结果，不能重放同一审批；重复提交返回冲突。任务、审批和恢复上下文仅保存在当前单进程内存中，进程重启后均不可恢复。

Microsoft Runtime 的 advisory 按任务保留；恢复复用该任务已有 advisory，不再次调用 LLM。恢复随后仍进入原 `chief-engineer` 业务后处理及统一 `QualityGate`。这不扩展为跨重启持久化保证。

### 自检字段含义

| 字段 | 行为检查范围 |
|---|---|
| `task_lifecycle_tracked` | 任务执行结果与状态被一致记录 |
| `task_approval_roundtrip_supported` | 任务等待、显式审批及恢复形成平台闭环 |
| `task_access_token_required` | 查询和提交须具有该任务的访问能力 |
| `workflow_approval_identity_bound` | 审批绑定当前工作流、本次请求与步骤，不能跨等待重放 |

这些字段是内置行为检查结果，不代替完整 `dotnet test`。完整测试、阶段检查与全局 `final_status` 必须分别回填；四字段通过不能覆盖既有 CAD 证据失败。

## 执行步骤

1. 读取项目必读协议、当前任务及审批合同、[质量门禁规范](quality_gate.md)，确认实现和测试均不依赖真实 CAD。
2. 先完成具体审批身份与存储原子匹配，使身份错误、预取消、重复提交和跨审批重放在执行下游前被拒绝。
3. 同步任务生命周期状态，保留每次执行或恢复的结果、等待信息和失败原因，再接入宿主查询及审批入口。
4. 让审批恢复复用 `chief-engineer` 原后处理和宿主 `QualityGate`；验证批准后失败、再次等待以及门禁打回均不会产生任务假成功。
5. 添加对应行为回归与阶段自检，再由执行代理统一运行构建、完整测试和指定输出目录的 self-check，回填下面的实际结果。

## 验证标准与结果回填

验证过程不得启动 COM 或真实 CAD。下列命令由本轮执行代理统一运行，文档编写阶段不重复运行构建或测试：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/v2_2_a_task_approval
```

| 场景 | 必须检查的结果 | 本轮验证 |
|---|---|---|
| 正常任务完成与执行异常 | 查询状态及最终结果分别反映完成或失败，不停留在初始状态 | 通过；任务创建、运行、失败及查询快照反例已覆盖 |
| 首次等待人工审批 | 查询能够取得当前具体审批身份及等待状态，下游尚未执行 | 通过；具体审批请求可查询，下游执行计数为零 |
| 任务访问令牌 | 首次消息提供令牌，查询或提交缺失/错误令牌统一 `404`，后续响应不泄露令牌 | 通过；单元测试及真实 HTTP 均验证 `404` 和令牌不再返回 |
| 错误或过期审批身份 | 拒绝提交、下游零新增执行，正确待审批项仍可提交 | 通过；错请求、错步骤、跨任务与旧审批均被拒绝 |
| 同一审批重复及并发提交 | 存储原子匹配，最多一次消费和恢复 | 通过；并发反例仅一次消费，下游仅执行一次 |
| 连续两次人工审批 | 第二次具有独立身份；第一份提交重放不能批准第二次；前段历史完整 | 通过；第二次身份独立，历史及原始审批问题保留 |
| 预取消后重新提交 | 取消不消费审批项，之后合法提交仍可恢复 | 通过；预取消无损，之后合法提交仍可完成 |
| 拒绝与要求修订 | 下游不执行，任务与最终响应同步为对应终止结果 | 通过；有效终态均为 `Rejected`，CAD Worker 调用为零 |
| 批准后继续失败或再次等待 | 后续实际状态覆盖中间批准状态，不能提前标记整个任务通过 | 通过；后续失败、再次等待和未解决 Runtime 问题均保留实际状态 |
| 恢复后的业务收尾与门禁 | 原 `chief-engineer` 后处理得到执行；最终输出经过 `QualityGate`，门禁打回仍反映在任务状态 | 通过；批准后 Fake CAD 仅执行一次，缺产物仍被 Validator 和 QualityGate 拒绝 |
| 同步首次请求与审批中查询 | 首轮结果返回后才能持有令牌；审批恢复期间可查询 `Running`，后续终态可追踪 | 通过；运行中可查询 `Running`，此前快照不会被后续修改 |
| 接受后连接断开 | 已接受动作继续收尾，凭已有令牌查询结果，重放不再次执行 | 通过模拟已受理请求的 `RequestAborted` 验证继续收尾与不可重放；未做真实 TCP 断连试验 |
| Microsoft advisory 恢复 | 同一任务恢复复用原 advisory，不增加 LLM 调用，继续原后处理和质量裁决 | 通过计数模拟 Invoker 验证同任务只调用一次，原问题及异常后的 RuntimeMetadata 保留；未调用外部模型 |
| 完整构建、测试与阶段自检 | 命令、退出码、计数、日志和最终字段；中文检查仍须通过 | 构建 `0` 错误、`0` 警告；完整测试 `543/551`，新增 `53/53`；四字段及中文检查通过 |
| 全局自检与 CAD 限制 | 如实列出已有 CAD 证据相关失败，保持 `R08`–`R11` 状态与失败关闭 | 全局自检 `Failed`、退出 `2`；8 个完整测试失败均与上一轮同名，`R08`–`R11` 按上述范围保留 |

## 本轮验收证据与后续范围

最终完整测试在构建成功后执行：`dotnet test --no-build --logger "trx;LogFileName=verified_final.trx" --results-directory output/tests/20260909_task_approval --verbosity quiet`，退出 `1`。新测试来自 `WorkflowApprovalIdentityTests`（18 项）、`GatewayTaskLifecycleTests`（28 项）和 `ApprovalCadContinuationTests`（7 项），均通过。

证据入口为 [验证汇总](../output/tests/20260909_task_approval/validation_summary.json)、[最终完整测试 TRX](../output/tests/20260909_task_approval/verified_final.trx)、[实际 HTTP 检查](../output/tests/20260909_task_approval/gateway_http_validation.json) 与 [默认自检报告副本](../output/tests/20260909_task_approval/platform_self_check_report.json)。默认自检实际命令为 `dotnet run --project src/Interfaces/CliHost -- self-check`，退出 `2`；定向自检报告位于 [本阶段报告](../output/validation/v2_2_a_task_approval/reports/platform_self_check_report.json)。HTTP 检查只启动本机回环地址的 Mock 宿主，检查后已关闭进程，未运行 CAD 或外部模型，报告不保存任务令牌。

8 项失败包括五类复杂特征证据、基础 Feature 证据、V2.0-D 候选证据，以及依赖生产证据的总自检断言；原因和前轮一致，见 [既有证据核验](2026_09_09_architecture_review.md)。没有修改 evidence、真实 API Adapter 或 `reviewrep`；原审查文件 SHA256 差异为零。四份旧孔测试报告只被测试更新了时间，已核实并恢复本轮之前内容；本轮验证产物单独保留。没有 Git 提交。

只读质量复审已确认本轮必要代码发现关闭。后续优先补齐 `R07` 的类型交接，再单独设计任务持久化、断点恢复与幂等执行记录；这些尚未实现。CAD 证据恢复、阵列及真实逐孔读取仍按独立能力准入处理。本轮没有通过修改旧失败断言、产物长度或源码绑定来制造通过。

## 常见失败与修复步骤

审批身份不匹配时，先读取同一任务当前待审批信息，再核对提交是否针对该次等待；不得替调用方改成当前身份后自动批准。并发或重复提交失败时，读取已生效决定和任务最新状态，禁止重跑已经执行的下游。

任务查询或审批得到 `404` 时，先核对 `task_id` 与首次响应保存的令牌；`submitted_by` 不能替代访问令牌。审批得到 `409` 时检查 `failure_reason` 和任务当前状态；网络断开后先查询，不把重复提交当作恢复方法。进程重启导致的内存状态丢失不能通过改审批标识恢复。

任务状态与响应不一致时，保留请求、流程结果和审计，定位首次执行、恢复、异常或门禁打回分支的状态更新。审批通过后收尾缺失时，检查恢复是否复用 `chief-engineer` 原路径及宿主门禁；不得直接把 `WorkflowEngine` 的中间通过转换为整个任务完成。

已有 CAD 证据导致完整测试或全局自检失败时，按 [历史架构审查](2026_09_09_architecture_review.md) 保留原因和证据，不将其改写为本轮平台测试通过或新增失败。若本轮产生新的失败，须单独列出触发、原因、源码位置和下一步验证命令。

## 禁止事项

禁止绕过 `chief-engineer`、WorkflowEngine、Worker 或 `QualityGate`；禁止让宿主直接调用 Worker。禁止用裸工作流标识代替具体审批身份，禁止通过先读取再无条件删除的组合冒充原子匹配，禁止重放旧审批消费新等待项。

禁止把单进程内存状态宣称为跨重启持久化，把任务访问令牌或 `submitted_by` 宣称为用户账户身份认证，把同步首次消息描述成后台队列，或把本阶段通过宣称为真实 CAD 可交付。禁止在查询、审批结果或普通审计中再次输出访问令牌；禁止在恢复时为重新取得 advisory 再调 LLM。禁止修改历史审查结论、只读 `reviewrep`、既有 evidence，禁止降低既有能力基线，禁止为平台阶段测试修饰 CAD 证据失败。
