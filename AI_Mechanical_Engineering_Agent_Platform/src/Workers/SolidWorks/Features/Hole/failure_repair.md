# Hole Handler 失败修复

## 目标与输入输出

本手册处理 `hole` 的类型、直径和策略证据失败。输入为孔参数、位置/方向/终止引用、Registry 与 evidence 状态；输出为前置拒绝结果或独立策略诊断要求。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 类型不是 `hole` 或 Registry 未注册 | 修正类型，或补完整 Handler 注册 | Registry 解析成功，未知类型继续受控拒绝 |
| `invalid_feature_parameter` | 两个直径字段都缺失或没有有限正数 | 提供 `hole_diameter_mm`，或使用兼容 `diameter_mm` | 参数校验通过且 COM 连接计数为零 |
| `feature_api_evidence_insufficient` | 没有接受的 Hole API 映射，状态为 `unverified` | 先评审唯一策略，再诊断放置、方向、终止、标准、类型和返回值 | 精确策略和参数轮廓经审查为 `verified` |

## 修复步骤

1. 校验正直径，不在执行层使用隐式默认值。
2. 确认位置、方向、终止和引用信息是否完整；参数校验通过不等于策略已验证。
3. 整图预检必须在 COM 前返回 `feature_api_evidence_insufficient`。
4. 若评审 `HoleWizard5`，只能作为官方研究候选；不得先接入 Worker 后补证据。
5. 若选草图加切除组合，必须定义事务边界、子步骤失败阶段、报告与几何验证。

## 禁止事项

禁止根据 `AddHoleWizardHole` 名称盲调 Hole Wizard，禁止猜测标准/类型枚举，禁止用 V1.9 草图切除结果宣称通用孔已验证，禁止复制第三方代码。
