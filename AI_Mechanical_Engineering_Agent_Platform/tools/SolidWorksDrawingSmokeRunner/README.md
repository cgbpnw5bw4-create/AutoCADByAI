# SolidWorks 工程图诊断 Runner

`SolidWorksDrawingSmokeRunner` 是 V1.1 阶段的独立诊断工具，只用于手动验证真实工程图基础视图链路。它不接入 `Agent`、`Gateway`、`LLM` 或默认 `self-check`，也不替代平台 Worker。

## 运行方式

```powershell
dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part "output/solidworks/real/plate_basic_4holes/<timestamp>/plate_basic_4holes.SLDPRT" --output output/solidworks/real/plate_basic_4holes_drawing
```

如果需要指定工程图模板：

```powershell
dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part "C:\path\plate_basic_4holes.SLDPRT" --output output/solidworks/real/plate_basic_4holes_drawing --template "C:\path\gb_a4.drwdot"
```

也可以通过环境变量 `SW_TEMPLATE_DRAWING_PATH` 指定模板。

## 输出

成功时输出：

- `plate_basic_4holes.SLDDRW`
- `plate_basic_4holes.pdf`
- `drawing_report.json`

失败时 `drawing_report.json` 必须包含可行动的 `failure_stage`，例如 `source_part_missing`、`drawing_template_missing`、`front_view_create_failed`、`slddrw_save_failed` 或 `pdf_export_failed`。

## 禁止事项

- 不默认启动 SolidWorks。
- 不做尺寸标注、标题栏、BOM、装配体或复杂模板。
- 不复制第三方脚本。
- 不把宏作为生产路径。
- 验证成功的 API 顺序必须回填到 Worker 封装，而不是让平台调用诊断工具。
