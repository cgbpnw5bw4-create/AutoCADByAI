# 项目执行总规范

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

## 修改后验证

每次修改后必须运行：

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

如果涉及真实 CAD，self-check、CI 和单元测试仍不能启动 CAD；本地交互式 `run-cad-workflow` 主流程则按 V2.0 默认启用规则运行。

## 失败处理

失败不是终点。发现失败后，先读取最新报告，判断 `failure_stage`，再选择对应模块的 `failure_repair.md`。没有对应 playbook 时，先生成 failure analysis 和 evidence report，再做最小修复。
