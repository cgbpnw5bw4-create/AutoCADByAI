# SolidWorks Worker 执行说明

## 组件职责

- `FakeSolidWorksWorker`：默认 dry-run Worker，只生成模拟 artifact，不启动 SolidWorks。
- `RealSolidWorksWorker`：真实执行入口，受安全开关保护。
- `SolidWorksSessionManager`：封装 COM 连接、超时和释放。
- `SolidWorksPlaneSelector`：兼容中英文模板的基准面选择。
- `SolidWorksPlateFeatureBuilder`：封装板件建模 API，不让主 Worker 堆满 dynamic COM 调用。
- `SolidWorksDrawingBuilder`：封装 V1.1 工程图基础视图 API，不让 `RealSolidWorksWorker` 直接堆满工程图 COM 调用。
- `SolidWorksSmokeRunner`：独立诊断 Runner，不依赖 Agent、Gateway、LLM 或 WorkflowEngine。
- `SolidWorksDrawingSmokeRunner`：V1.1 工程图独立诊断 Runner，只能手动运行，不被默认 self-check 调用。
- `SolidWorksEnvironmentValidator`：执行前预检系统、模板、输出目录和安全开关。

## 执行顺序

默认走 `FakeSolidWorksWorker`。真实执行必须先通过 `SolidWorksEnvironmentValidator`，再由 `SolidWorksSessionManager` 连接，最后由 `SolidWorksPlateFeatureBuilder` 执行受控建模步骤。

## V1.1 工程图基础视图链路

V1.1 只允许基于已经生成的 `plate_basic_4holes.SLDPRT` 创建基础工程图。最小链路如下：

```text
plate_basic_4holes.SLDPRT
→ SolidWorksDrawingBuilder
→ 新建 Drawing 文档
→ 插入 Front / Top / Right / Isometric 基础视图
→ 保存 plate_basic_4holes.SLDDRW
→ 导出 plate_basic_4holes.pdf
→ 写出 drawing_report.json
→ SolidWorksArtifactValidator
```

真实工程图默认关闭。只有 `SW_ENABLE_REAL_EXECUTION=true` 且 `SW_REAL_DRAWING_SMOKE_TEST=true` 时，self-check 才允许进入工程图 smoke test。严格模式需要额外设置 `SW_STRICT_REAL_DRAWING_TEST=true`。

独立诊断命令：

```powershell
dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part "output/solidworks/real/plate_basic_4holes/<timestamp>/plate_basic_4holes.SLDPRT" --output output/solidworks/real/plate_basic_4holes_drawing
```

V1.1 不做尺寸标注、标题栏、BOM、装配体工程图、复杂模板和钣金展开图。

## 禁止事项

- 不默认启动 SolidWorks。
- 不让 Agent、Gateway、LLM 直接调用 Worker。
- 不复制第三方 Python COM 脚本。
- 不把 COM 类型泄漏到平台 Contracts。
- 不在诊断 Runner 未验证时回填主 Worker。
