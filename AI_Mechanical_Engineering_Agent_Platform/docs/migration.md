# 2026-10-09 重构说明: 从 V2.3 治理平台到建模内核

## 发生了什么

平台从"以历史证据和自检治理为中心"改为"以可复用建模内核为中心"。依据见 [architecture.md](architecture.md) 第 3、4 节 (语言分工与冗余分析)。

- **移植进来**：CFC-650 铜箔清洗机 V2 的 C# 内核 (`D:\07_WorkSpace\Claude\20260910\src`)、行星齿轮减速器的齿形几何与齿轮配合装配 (`D:\07_WorkSpace\Claude\0924\PlanetaryGear\scripts`)。两个原项目目录保持原样，未修改。
- **移出主干**：旧平台的全部源码、测试、工具、文档、证据与入库产物。它们完整保留在 git 历史中，最后版本为提交 `5e08134` (V2.3 Engineering Planning Loop)。

## 旧内容去向

| 旧路径 | 内容 | 现在 |
|---|---|---|
| `src/PlatformCore` (含 7257 行自检)、`src/QualityGate` | 工作流、质量门、自检、能力回归闸 | 移除；运行时校核由 `Pipeline` + `Checks` 完成 |
| `src/Workers/SolidWorks` | 晚绑定 COM、Feature Handler、证据策略、零件族 Builder | 移除；内核改用 CFC 已标定的早期绑定实现 |
| `src/DomainSchemas`、`src/Modules` | CADModelSpec / FeatureGraph / BuildPlan、各模块文档 | 移除；设计以 C# 设计项目 + `design.json` 参数表达 |
| `src/AgentRuntime.Microsoft`、`src/ModelRuntime`、`src/Interfaces/*` | 产品运行时多 Agent、模型接入、网关、API 宿主、任务审批 | 移除；AI 角色由 Claude Code / Codex 直接编写设计项目承担 |
| `tools/*SmokeRunner*`、`tools/SolidWorksEdgeProbe` | 冒烟测试与拓扑探针 | 移除；`cad run` / `cad diag` 取代 |
| `tests/PlatformSelfCheck.Tests` (783 项) | 旧架构测试 | 移除；`tests/CadTests` 覆盖新内核纯逻辑，真实能力由回归样例运行证明 |
| `docs/` 39 份、`.codex/`、`.agents/`、`references/` | 阶段文档、Codex 角色与技能、外部参考分析 | 移除；规则集中到 `AGENTS.md`，知识集中到 4 份文档 |
| `evidence/`、`output/`、`examples/`、`config/solidworks.local*.json` | 历史证据、验证日志与 TRX、旧请求样例、本地授权 | 删除 (入库部分可从 git 历史取回；本地未入库的旧 CAD 产物经用户确认直接删除) |

旧平台中值得保留的经验已写入 [api_calibration.md](api_calibration.md)：STEP 必须核对身份、本机可能存在包装 SLDPRT 的文件过滤层。

## 需要旧代码时

```powershell
git show 5e08134:AI_Mechanical_Engineering_Agent_Platform/<旧路径>
git worktree add ../AutoCADByAI_v2.3 5e08134
```
