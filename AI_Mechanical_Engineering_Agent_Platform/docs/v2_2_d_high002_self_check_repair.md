# V2.2-D HIGH-002 自检拒绝判据修复

## 问题与范围

依据 [2026-09-29 项目审查](../../reviewrep/2026-09-29-v2.2-d-project-review.md) 的 HIGH-002，仅修复两个受保护的未取证拒绝字段。原 `unverified_api_blocks_real_execution` 绕过注册表直接检查证据策略；原 `unverified_feature_blocks_execution` 在复杂特征全部取证后没有未取证样本。

本轮不处理该审查的其他编号，不改变 Handler、Adapter、证据指纹算法、能力基线或历史报告，不执行真实 CAD 或重新采证。

## 修复后的行为

自检为旋转与圆角分别创建隔离注册表，使用合法参数生成单节点计划，先通过真实 `FeatureHandlerRegistry.ValidateForRealExecution` 正向控制。随后仅将私有 Handler 实例的证据状态改为 `unverified`，将同一计划送入同一注册表预检。

负样本必须实际出现在预检结果中，并以 `feature_api_unverified` 拒绝；错误放行或错误拒绝阶段均失败。参数无效、注册表缺失、共享证据对象、没有目标结果或执行异常，都不能生成通过结论。探针最终恢复状态，不改变生产实例和原证据文件。保留原五种复杂特征的正向及状态检查。

删除仅凭方法存在宣称连接前拒绝的检查及过期注释。自检证明的是注册表准入行为；Worker 连接次数为零仍由既有注入会话测试独立验证。

## 报告合同与失败关闭

两个字段改为可空布尔值：`true` 表示所需负样本全部完成且行为正确；`false` 表示已观测到错误行为；没有负样本或观测不完整时为 `null`，已经观测到的负样本失败不会被另一项未知结果覆盖。

新增 `unverified_evidence_negative_samples` 记录每项实际负样本数量，`unverified_evidence_self_check_issues` 记录失败或未观测原因。沿用既有报告版本标识，消费方必须处理这两个字段新增的 `null` 值，不能将其视为通过。

能力快照仅包含有值的判据；空值不转换为 `true` 或 `false`。既有 `SelfCheckCapabilityRegressionGate` 将缺项报告为未观测配置错误并阻断，受保护基线保持原样。

## 验证记录

新增回归覆盖：全部已取证时仍有负样本、无样本、注册表缺失、共享实例、未调用或未返回负样本、参数错误、错误放行、错误拒绝阶段、部分未知、异常恢复、生产证据隔离及报告接线。

本轮完整构建、测试、自检与保护快照复核结果统一写入 [验证汇总](../output/validation/v2_2_d_high002/verification_summary.json)。此前 V2.2-D 的783项测试与真实 CAD 取证记录保留原样；本轮结果独立记录，不覆盖历史通过或失败结论。
