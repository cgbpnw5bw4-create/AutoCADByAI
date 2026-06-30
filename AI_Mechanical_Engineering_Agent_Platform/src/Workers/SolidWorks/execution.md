# SolidWorks Worker 执行说明

## 组件职责

- `FakeSolidWorksWorker`：默认 dry-run Worker，只生成模拟 artifact，不启动 SolidWorks。
- `RealSolidWorksWorker`：真实执行入口，受安全开关保护。
- `SolidWorksSessionManager`：封装 COM 连接、超时和释放。
- `SolidWorksPlaneSelector`：兼容中英文模板的基准面选择。
- `SolidWorksPlateFeatureBuilder`：封装板件建模 API，不让主 Worker 堆满 dynamic COM 调用。
- `SolidWorksSmokeRunner`：独立诊断 Runner，不依赖 Agent、Gateway、LLM 或 WorkflowEngine。
- `SolidWorksEnvironmentValidator`：执行前预检系统、模板、输出目录和安全开关。

## 执行顺序

默认走 `FakeSolidWorksWorker`。真实执行必须先通过 `SolidWorksEnvironmentValidator`，再由 `SolidWorksSessionManager` 连接，最后由 `SolidWorksPlateFeatureBuilder` 执行受控建模步骤。

## 禁止事项

- 不默认启动 SolidWorks。
- 不让 Agent、Gateway、LLM 直接调用 Worker。
- 不复制第三方 Python COM 脚本。
- 不把 COM 类型泄漏到平台 Contracts。
- 不在诊断 Runner 未验证时回填主 Worker。
