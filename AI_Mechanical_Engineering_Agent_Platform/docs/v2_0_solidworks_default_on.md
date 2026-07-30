# V2.0-SW-DEFAULT-ON：SolidWorks 本地交互默认执行

## 阶段结论

本阶段把真实 SolidWorks 从“显式启用”改为“本地交互默认启用”。默认配置为：

- `RealExecutionDefaultEnabled=true`。
- `dry_run=false`。
- `VisibleModeDefault=true`。
- 本地交互式 CLI 可启动或连接 SolidWorks。

请求级 `allow_real_cad_execution` 和环境级 `SW_ENABLE_REAL_EXECUTION` 已废弃，不再参与 Worker 选择，也不会产生确认缺失阶段。

这是相对 V1.x 的行为破坏性变化：本地交互式 CLI 在 `dry_run=false` 时会尝试真实 SolidWorks。自动化脚本、计划任务和无人值守环境在迁移前必须显式使用 `dry_run=true`、`SW_DISABLE_REAL_EXECUTION=true` 或 `SW_FORCE_FAKE_WORKER=true`。

## 统一关闭条件

只有以下条件关闭真实执行：

1. 请求显式 `dry_run=true`。
2. `SW_DISABLE_REAL_EXECUTION=true`。
3. CI 环境。
4. 单元测试环境。
5. `SW_FORCE_FAKE_WORKER=true`。

`SW_VISIBLE=false` 只选择后台运行。

即使策略选择真实 Worker，连接前仍必须通过只读执行环境探测。非 Windows、无交互式桌面或缺少 `SldWorks.Application` COM 注册时，以 `real_execution_environment_unavailable` 失败并保持连接尝试次数为 0；不得自动回退 Fake Worker 制造成功。

## 架构边界

默认规则变化不改变主工作流：

```text
ChiefEngineerOrchestrator
→ WorkflowEngine
→ SolidWorksWorkflowRouter
→ PartTypeRegistry
→ PartFamilyBuilder
→ RealSolidWorksWorker
→ ArtifactValidator
→ Reviewer
→ QualityGate
```

Agent、Gateway 和 LLM 仍不能直接调用 Worker。被关闭的执行使用 Fake Worker 完成可验证的 dry-run 链路，不得制造真实 CAD 成功证据。

## 配置迁移

旧命令中的启用变量和请求确认字段应删除。当前变量为：

- `SW_DISABLE_REAL_EXECUTION=true`：关闭真实执行。
- `SW_VISIBLE=false`：后台运行。
- `SW_FORCE_FAKE_WORKER=true`：强制 Fake Worker。

`config/solidworks.local.json` 仅保留模板、可见性和超时设置。

## 验证

构建、测试和 self-check 的规范命令见 `docs/solidworks_real_acceptance_protocol.md`。self-check 仅验证策略和边界，不启动 SolidWorks；`solidworks_execution_environment_probe_supported` 必须为 `true`。
