# Revolve Boss Handler 失败修复

## 目标与输入输出

本手册处理 `revolve_boss` 的类型、角度、轮廓/轴选择和 API evidence 失败。输入为 Feature 参数、引用、selection mark、validation issues 与证据状态；输出为可行动失败阶段和无 COM 关闭条件。

## 失败映射

| `failure_stage` | 直接原因 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_feature_type` | 类型不是 `revolve_boss` 或未注册 | 修正类型或补独立 Handler 注册 | Registry 解析成功且 Worker 不增加类型 switch |
| `invalid_feature_parameter` | `angle_degrees` 缺失、非有限数、≤0 或 >360 | 修正到 `(0,360]`；不得夹紧或默认改为 360 | 纯校验通过，越界样例仍受控拒绝 |
| `feature_api_evidence_insufficient` | 通用引用、mark 和 `FeatureRevolve2` 映射未验证 | 独立诊断轮廓、轴、完整长参数、返回与几何 | 当前 Handler 版本和精确参数轮廓经审查为 `verified` |

## 修复步骤

1. 先校验角度，再校验上游闭合半轮廓和构造中心线引用。
2. 确认 profile mark 候选 `0`、axis mark 候选 `16`，但不得仅凭 V1.9 记录授权当前 Adapter。
3. 重跑整图参数校验和 evidence 预检，当前应保持 COM 连接计数为零。
4. 独立诊断记录 `ISelectData.Mark`、`IEntity.Select4`、`FeatureRevolve2` 全部参数、弧度转换、选择顺序、返回 Feature、重建和几何审查。
5. 只有同参数轮廓证据可重复并经审查后，才能修改状态并重跑主流程。

## 禁止事项

禁止把 shaft 专用 360 度证据泛化到任意旋转，禁止猜测 mark 或长参数，禁止用偏移拉伸静默回退，禁止部分执行或复制第三方代码。
