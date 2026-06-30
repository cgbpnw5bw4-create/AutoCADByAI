# SolidWorks 真实建模诊断 Runner

`SolidWorksSmokeRunner` 是 V1.0-B-DIAG 阶段的独立诊断工具，只用于手动验证本机 SolidWorks COM / API 的最小建模链路。

它不依赖 `Agent`、`Gateway`、`LLM`、`WorkflowEngine`，也不会被默认 `self-check` 调用。

## 运行方式

```powershell
dotnet run --project tools/SolidWorksSmokeRunner -- --output output/solidworks/diagnostics
```

如果当前 SolidWorks 环境必须使用指定零件模板，可以传入：

```powershell
dotnet run --project tools/SolidWorksSmokeRunner -- --output output/solidworks/diagnostics --template "C:\Path\To\Part.prtdot"
```

也可以使用环境变量 `SW_TEMPLATE_PART_PATH` 提供模板路径。

## 输出内容

工具会在输出目录下创建：

```text
output/solidworks/diagnostics/plate_basic_4holes/<timestamp>/
```

并尽量写出：

* `plate_basic_4holes.SLDPRT`
* `plate_basic_4holes.STEP`
* `diagnostic_report.json`

如果任一步骤失败，`diagnostic_report.json` 会记录 `failure_stage`、`operations`、`errors`、保存结果和 STEP 导出结果。

## 边界说明

该工具只用于诊断 SolidWorks COM / API 顺序，不接入平台主流程，不允许被 `Agent`、`Gateway` 或 `LLM` 直接调用。验证成功的 API 顺序才允许回填到 `RealSolidWorksWorker`。
