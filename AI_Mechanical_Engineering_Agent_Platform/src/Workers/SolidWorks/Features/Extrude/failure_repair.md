# Extrude Boss Handler 失败修复

## 目标与输入输出

本手册处理 `extrude_boss` 的类型、深度、方向和 API evidence 失败。输入为 Feature 参数、活动草图引用、validation issues 和证据状态；输出为可行动失败阶段、无 COM 回归结果或独立诊断要求。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 类型不是 `extrude_boss` 或 Registry 缺失 | 修正类型或补完整独立注册 | Registry 解析成功且 Worker 无新增类型 switch |
| `invalid_feature_parameter` | `depth_mm` 缺失、非有限正数，或 `direction` 不属于声明枚举 | 修正参数；不得截断、取绝对值或静默改方向 | 纯校验通过，非法样例在 COM 前拒绝 |
| `feature_api_evidence_insufficient` | `FeatureExtrusion2` 通用映射仍为 `unverified` | 停止执行；完成 blind / mid-plane 独立诊断和长参数审查 | 精确参数轮廓绑定当前 Handler 版本并被审查为 `verified` |

## 修复步骤

1. 先确认正数毫米深度和 `blind|mid_plane` 枚举。
2. 检查上游草图引用与闭合性；不能在 Extrude Handler 内重建未知草图。
3. 重跑 Registry、参数校验和整图 evidence 预检。
4. 当前 evidence 未验证时确认 `RealCadConnected=false`、`RealCadExecuted=false`。
5. API 修复只在独立诊断中记录完整 `FeatureExtrusion2` 参数、单位、返回 Feature、重建和产物。

## 禁止事项

禁止把 `mid_plane` 降级为 `blind`，禁止猜测 COM 长参数，禁止用 V1.9 plate/flange 专用成功替代 Handler 诊断，禁止复制第三方代码。
