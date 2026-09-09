# 项目执行总规范

## 目标、适用范围与输入输出

本规范用于所有阶段的开发、审查和验证，目标是让修改范围、失败原因与完成证据一致。输入为任务、当前源码和阶段协议；输出为最小变更、可行动失败记录、实际命令日志和本次报告。

## 核心规则

- 不允许跳阶段。
- 不允许没有 self-check 字段就声称完成。
- 不允许失败后只记录 `Failed` 就停止。
- `failure_stage` 必须可行动，必须能指导下一步查证或修复。
- API 失败必须进入 API Evidence Driven Repair Loop。
- 真实 CAD 失败必须先在诊断 Runner 中隔离验证，再回填 Worker。
- `Agent` 不能直接调用 `Worker`。
- `Gateway` 不能直接调用 `Worker`。
- `LLM` 不能直接调用 `Worker`。
- 本地交互式主流程默认执行真实 CAD，默认 `dry_run=false`、`SW_VISIBLE=true`。
- `dry_run=true`、`SW_DISABLE_REAL_EXECUTION=true`、CI、单元测试或 `SW_FORCE_FAKE_WORKER=true` 时必须使用 Fake Worker 或在连接前失败关闭。
- 不允许复制第三方 `scripts` 源码。
- 不允许 Markdown 英文说明泛滥。

## 修改前检查

执行任务前先确认当前阶段、涉及模块、允许修改目录、禁止修改目录、需要读取的说明文件和已有报告。涉及 `SolidWorks` API 时，必须先读取模块的 `api_evidence.md` 和最新诊断报告。

### 本轮 V2.2-A 平台开发边界

用户本轮已授权自主选择并推进下一阶段，当前入口为 [V2.2-A 平台任务生命周期与审批闭环](v2_2_a_task_approval_lifecycle.md)。前轮 `R01`–`R05` 已完成，本轮最小范围为 `R06` 的具体审批身份与原子匹配、任务状态及查询、宿主审批入口，以及恢复后的 `chief-engineer` 原后处理和 `QualityGate`。上轮“不进入 V2.1-C”限定上轮范围，本轮独立平台开发已有新授权，无需再次确认。

`R07` 类型交接、跨进程或跨重启持久化留待后续；`R08`–`R11` 的 CAD 问题不阻塞本轮无 COM 平台开发，也不因平台开发而被标记已修复。保留真实执行失败关闭、历史报告和证据状态；不得修改旧 evidence 或只读 `reviewrep`，不得降低既有能力基线。

宿主任务查询与审批提交必须将首次响应的 `task_access_token` 放入 `X-Task-Access-Token` 请求头；令牌只授予对应任务的访问能力，`submitted_by` 仅作审计。首次消息同步执行，令牌随首轮结果返回；审批接受后连接断开不取消动作，使用已有令牌查询结果并拒绝重放。任务和审批只保存在单进程内存，重启不可恢复；Microsoft advisory 在同一任务内复用，恢复不再次调用 LLM。

## 修改后验证

每次修改后必须运行：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

如果涉及真实 CAD，self-check、CI 和单元测试仍不能启动 CAD；本地交互式 `run-cad-workflow` 主流程则按 V2.0 默认启用规则运行。

V2.2-A 由执行代理统一运行上述验证，并为定向自检选择本轮新目录，例如 `self-check --output output/validation/v2_2_a_task_approval`。文档代理不重复编译或测试；具体 API 和阶段字段已在 [阶段页](v2_2_a_task_approval_lifecycle.md) 登记，实际计数和日志继续回填。审批恢复须验证原业务收尾与统一质量门禁，不能仅凭审批消费成功、HTTP `200` 或核心工作流中间通过宣布任务完成。

### 自检输出定向

需要保留既有报告或执行只读源码审查时，使用 V2.1-B 可靠性补强已提供的输出定向形式：

```powershell
dotnet run --project src/Interfaces/CliHost -- self-check --output output/validation/20260909_reliability
```

`--output <目录>` 指定本次输出根目录，主报告位于该目录的 `reports/platform_self_check_report.json`。省略参数时保留既有 `output` 默认行为。本轮承诺仅覆盖默认自检的持久产物；内部临时夹具仍使用系统临时目录，显式调用的既有 smoke 流程不在本轮承诺内。验证调用方必须保留实际命令、退出码和报告路径，并核查默认自检的持久产物遵循指定目录。源码只读审查仍会生成验证产物，应把目录放在获准的输出位置，禁止写入 `reviewrep` 或既有真机 evidence。

报告中的阶段字段、全局 `final_status` 和 `dotnet test` 的真实结果分别回填；布尔汇总不能代替测试执行记录。输出路径无效、文件不可读或样例损坏时，必须报告路径与直接原因；不得用旧报告覆盖问题，也不得用清理命令隐藏工作区变化。

### 自检 schema 迁移

V2.1-B 可靠性补强将 `schema_version` 升为 `2.1-b-reliability`，新增 `workflow_step_retry_limit_enforced`、`workflow_cancelled_approval_preserved`、`workflow_multi_approval_history_preserved` 和 `structured_cad_input_fails_closed`。孔汇总字段改为 `hole_self_check_group_passed`，旧的 `hole_feature_regression_tests_passed` 已删除。孔能力字段采用可空 `bool?`，未完成观测返回 `null`，对应原因和未观测项分别查看 `hole_self_check_issues`、`hole_self_check_unobserved_capabilities`。

消费方必须检查 schema、迁移旧字段并处理 `null`；不能将 `null` 当作通过，也不能用内置检查组结果代替实际测试结果。这是有意的合同变更，禁止宣称旧消费方无需迁移即可完全兼容。

V2.2-A 的当前 `schema_version` 为 `2.2-a-task-approval`，新增 `task_lifecycle_tracked`、`task_approval_roundtrip_supported`、`task_access_token_required`、`workflow_approval_identity_bound` 四个布尔行为检查字段。保留前轮孔字段的可空语义和迁移规则。四个新字段、完整测试统计及全局 `final_status` 必须分别列出，不能相互代替。

## 失败处理

失败不是终点。发现失败后，先读取最新报告，判断 `failure_stage`，再选择对应模块的 `failure_repair.md`。没有对应 playbook 时，先生成 failure analysis 和 evidence report，再做最小修复。

若真实 CAD 证据中的产物长度与报告 `size_bytes` 不一致，保留实际失败并核查可信来源；无法完整恢复时重新采集同次诊断和主流程证据。禁止仅修改 `size_bytes`、文件或源码 hash 制造通过。2026-09-09 确认的 7 组既有不一致、版本归因边界及后续优先顺序见 [本轮架构审查](2026_09_09_architecture_review.md)。完整测试即使因既有证据失败，也必须如实报告失败数量与日志。
