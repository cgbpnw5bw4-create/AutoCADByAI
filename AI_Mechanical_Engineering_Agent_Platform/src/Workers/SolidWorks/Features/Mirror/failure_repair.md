# 镜像失败修复

## 目标

定位并修复 `mirror` Feature 的失败，避免把未验证能力误判为缺陷，也避免以放宽校验的方式"修好"问题。

## 适用范围

真实执行或 dry-run 返回 镜像相关 `failure_stage` 时使用。

## 必须读取

- 本目录的 `api_evidence.md`
- `src/Workers/SolidWorks/api_evidence.md`
- `src/Workers/SolidWorks/failure_repair.md`
- `docs/cad_capability_matrix.md`
- 同一次运行的 `feature_execution_report.json`（若存在）

## 失败阶段与处理

| failure_stage | 含义 | 处理 |
|---|---|---|
| `feature_api_unverified` | 镜像 无真实执行证据 | **预期行为，不是缺陷。** 需按 `api_evidence.md` 的六项条件采集证据后才能执行 |
| `unsupported_feature_type` | Feature 类型未注册或 Handler 不匹配 | 检查 `FeatureHandlerRegistry.CreateDefault` 注册情况 |
| `invalid_feature_parameter` | 参数超出声明档案 | 按 `ParameterSchema` 修正输入；**不得放宽 Validate** |
| `feature_adapter_missing` | 执行上下文缺少 Adapter | 检查执行管线注入，不要在 Handler 中新建 COM 对象 |

## 执行步骤

1. 先固定唯一 `failure_stage`，不要同时改多处。
2. 若为 `feature_api_unverified`：停止排查缺陷，转入证据采集流程。
3. 若为 `invalid_feature_parameter`：核对输入与 `ParameterSchema`，修正输入侧。
4. 若怀疑 API 参数错误：在诊断 Runner 中隔离复现，不要直接改主 Worker。

## 验证标准

- 修复后 `dotnet test` 全绿。
- self-check 中 `mirror_handler_registered` 与 `unverified_feature_blocks_execution` 均为 true。
- 状态仍为 `unverified` 时，真实执行必须继续被拒绝。

## 禁止事项

- 不得为了让流程通过而调高 `api_evidence_status`。
- 不得放宽 `Validate` 以绕过参数校验。
- 不得在 Handler 或 Worker 中直接调用 COM。
- 不得盲改 COM 长参数列表。
