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
4. 只有输入完全合法后才评估 evidence；V2.0-C 精确 TopPlane profile 可使用已验证证据，其他轮廓仍返回 evidence-insufficient。
5. 证据诊断不得通过主 Worker盲试；必须独立记录版本、参数、返回对象、重建和几何结果。

## 禁止事项

禁止默认选择 Top Plane，禁止把非空实体当作闭合轮廓，禁止绕过全图预检、部分执行或复制第三方代码。

## V2.0-C Adapter 失败修复

| `failure_stage` | 直接原因 | 修复与关闭条件 |
|---|---|---|
| `feature_adapter_missing` | Worker 未注入 `ISolidWorksFeatureAdapter` | 修复组合根；`SketchHandler` 仍不得创建真实 Adapter。 |
| `sketch_execution_failed` | 标准基准选择、草图进入/退出或事务失败 | 在专用 diagnostic 中证明选择、事务和重建完整通过。 |
| `sketch_geometry_create_failed` | line / rectangle / circle 参数、单位或返回对象无效 | 核对坐标合同和米制转换；每个预期实体必须有效。 |
| `feature_result_invalid` | COM 未抛异常但草图结果、依赖或重建不合格 | 保留失败并补结构化结果校验，不得继续下游特征。 |
| `feature_artifact_missing` | 当次 SLDPRT、STEP 或报告缺失/为空 | 修复保存、导出或报告；三项输出和运行标识必须一致。 |
| `feature_api_unverified` | 请求超出已验证 TopPlane line / center rectangle / circle 精确 profile | 停止生产；不得把任意面、arc、slot、constraints 或 dimensions 解释为已验证。 |

权威 evidence 来自 `20260730_085830_6592380`，而不是旧 run。只读取当次结果；diagnostic 仍为 `NotDeliverable`，修复后从 `run-cad-workflow` 最终验收。禁止 Handler COM、任意面猜测、空实体成功和进入 V2.0-D。
