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
2. 读取项目执行规范和当前阶段索引。
3. 读取当前模块文档。
4. 读取最新报告和相关测试。
5. 做最小修改。
6. 更新 self-check 字段和测试。
7. 运行 build、test、self-check。
8. 输出已验证结果和剩余风险。

## 失败流程

出现失败时先定位 `failure_stage`。如果涉及 API，必须查官方资料、本地参考资料和 evidence 报告，再提出候选策略。不能只记录 `Failed` 后停止。
