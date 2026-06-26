# SolidWorks Worker 说明

本目录是 V0.9-B 的 SolidWorks 执行层骨架，也就是当前的 dry-run skeleton。当前只提供 `ISolidWorksWorker` 合同和 `FakeSolidWorksWorker`，用于验证平台边界、dry-run 流程和 self-check，不连接真实 SolidWorks。

当前明确禁止：

- 不启动 SolidWorks。
- 不调用 COM。
- 不调用 `SldWorks.Application`。
- 不生成真实 `.SLDPRT`、`.SLDDRW`、STEP 或 PDF 文件。
- 不得复制外部 `solidworks-automation-skill/scripts` 源码。
- 不把 Python COM 脚本直接塞进 Worker。

`FakeSolidWorksWorker` 只生成模拟产物：

- `output/solidworks/artifacts/fake_plate_basic_4holes.SLDPRT.txt`
- `output/solidworks/artifacts/fake_plate_basic_4holes.STEP.txt`
- `output/solidworks/reports/build_report.json`
- `output/solidworks/logs/fake_solidworks_worker.log`

架构边界：

- Agent 只能提出计划和协作建议，不能直接调用 Worker。
- Gateway 不能直接调用 Worker。
- Skill 只生成 `SolidWorksBuildPlan`。
- Worker 才是未来执行 SolidWorks 操作的边界。
- Validator 负责校验构建计划、执行模式和输出产物。
- Reviewer 负责工程合理性复审。
- `QualityGate` 负责最终裁决。

真实 `RealSolidWorksWorker` 计划在 V1.0 才实现。即使进入 V1.0，真实执行也必须显式设置 `allow_real_cad_execution=true`，并继续经过平台调度、审计日志和 `QualityGate`。
