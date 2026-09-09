# Codex 执行协议

Codex 不允许在未读取相关说明文件的情况下直接修改核心逻辑。

## 任务输入必须包含

- 当前版本阶段。
- 本轮任务目标。
- 必须先读取的说明文件。
- 允许修改的目录。
- 禁止修改的目录。
- 不要做的事。
- 成功标准。
- 必须运行的命令。
- self-check 字段。
- 如果失败，应该读取哪个 `failure_repair.md`。
- 如果是 API 失败，必须执行 API Evidence Driven Repair Loop。
- 完成后输出摘要。

## 执行流程

1. 读取 `AGENTS.md` 和 `docs/index.md`。
2. 检查 `docs/codex_agent_registry.md`，确认是否已有 canonical agent 覆盖当前职责。
3. 检查 active `.codex/agents/` 是否只包含 canonical agents，若只是职责扩展，不创建新 Agent。
4. 将新要求优先写入对应 Skill 或 Markdown，例如 `solidworks-api-repair`、`quality-review`、`markdown-docs-standard`、`api_evidence.md`、`review_checklist.md` 或本协议。
5. 读取项目执行规范和当前阶段索引。
6. 读取当前模块文档。
7. 读取最新报告和相关测试。
8. 做最小修改。
9. 更新 self-check 字段和测试。
10. 运行 build、test、self-check。
11. 输出已验证结果和剩余风险。

## 失败流程

出现失败时先定位 `failure_stage`。如果涉及 API，必须查官方资料、本地参考资料和 evidence 报告，再提出候选策略。不能只记录 `Failed` 后停止。

## V2.1-B 执行补充

目标是四类通用孔增强，适用定义、验证、Adapter 与文档，不改变 Handler 架构。输入为当前图、协议、前置审查及 API evidence；输出为标准化请求、失败阶段、几何检查、回归和中文文档。

执行时先读 `docs/v2_1_b_hole_features.md`，确认复杂特征库审查 `HEAD=bbeafc9` 无 Blockers；全部 Improvements 保留于 `docs/technical_debt.md`，不顺带实施。既有六个 canonical agent 足以覆盖职责，不新增 Hole Agent。四个样例默认 dry-run，实际参数与面引用在 Worker 前校验，未验证 API 在连接前拒绝，缺独立几何不得通过 QualityGate。

验证须运行完整 build、test、self-check 并逐项回填十二个孔字段，分开说明总体失败与阶段通过。API 失败按孔手册隔离取证，禁止复制脚本、Handler COM、Cut 冒充攻丝、旧 evidence 换绑或进入 V2.1-C。
