# Sketch Handler 失败修复

## 目标与输入输出

本手册定位 `sketch` 的类型、基准引用、实体 JSON 和 API evidence 失败。输入为 `FeatureDefinition`、Handler validation issues、Registry 结果和证据状态；输出为稳定 `failure_stage`、修复后的无 COM 校验结果，或后续独立诊断要求。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 特征类型不是 `sketch`，或 Registry 未注册 | 修正类型；若扩展新类型，补独立 Handler、注册、测试和文档 | Registry 解析成功，未知类型仍受控拒绝 |
| `sketch_reference_missing` | `reference_plane` / target reference 为空 | 修正 canonical 基准引用，不在执行层猜默认平面 | 无 COM 校验通过，错误样例仍被拒绝 |
| `invalid_feature_parameter` | 缺少 `entities` 或实体 JSON 无法反序列化 | 补合法 `SketchEntity[]` JSON，保留原始 issue | 参数校验通过且 COM 连接计数为零 |
| `unsupported_sketch_entity` | 实体为空或含未支持类型 | 改用受支持实体；扩展时补实现、证据和测试 | 支持实体通过，未支持实体继续拒绝 |
| `feature_api_evidence_insufficient` | `api_evidence_status=unverified` | 停止真实执行，按 `api_evidence.md` 完成引用、实体、闭合性和产物诊断 | 同版本、同参数轮廓证据经审查为 `verified` |

## 修复步骤

1. 先确认类型和 Registry，不修改 Worker switch。
2. 检查服务端草图引用和实体 JSON。
3. 重跑 Handler `Validate`、全图预检及 COM 前阻断测试。
4. 只有输入完全合法后才评估 evidence；当前正常结果仍是 `feature_api_evidence_insufficient`。
5. 证据诊断不得通过主 Worker盲试；必须独立记录版本、参数、返回对象、重建和几何结果。

## 禁止事项

禁止默认选择 Top Plane，禁止把非空实体当作闭合轮廓，禁止绕过全图预检、部分执行或复制第三方代码。
