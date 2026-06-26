# solidworks-automation-skill 分析

本文分析外部仓库 `wzyn20051216/solidworks-automation-skill` 对本项目 SolidWorks Module 设计的参考价值。该仓库采用 Python COM 自动化方式控制 SolidWorks，并包含 `scripts/`、`references/`、`subskills/`、`examples/`、`mcp-server/` 等目录。

本项目只参考其结构和工程思想，不复制其源码，不把该仓库加入 submodule，不在当前阶段调用 COM，也不接真实 SolidWorks。

## 仓库定位

`solidworks-automation-skill` 是面向 AI Agent 的 SolidWorks 自动化 Skill。它通过 Python COM 封装 SolidWorks API，覆盖零件建模、装配体、工程图、导出、外观、Motion Study、自审查和 MCP 工具暴露。

该仓库的定位是“可直接执行的本地自动化 Skill”。本项目的定位是“长期可托管的多 Agent 平台”。因此二者不能直接合并。

## 可借鉴能力

可借鉴的能力包括：

- preflight：在执行前检查依赖、SolidWorks 安装和 COM 可用性。
- API 查证优先：遇到未封装 API 时优先查官方 API Help 或本地 SDK，不凭记忆补长参数。
- 能力拆分：把建模、装配、工程图、导出、外观、审查等能力分层组织。
- 自审查：生成多视角预览、结构化 JSON 报告和 Markdown 摘要。
- MCP 工具封装：通过受控工具暴露能力，而不是开放任意 Python 或 VBA 执行。
- 串行执行：SolidWorks 是桌面 COM 应用，外部工具应避免并发调用造成会话不稳定。
- 错误返回包含建议动作，便于上层 Agent 纠错。

## 不直接复用原因

本项目当前阶段不直接复用该仓库源码，原因如下：

- 本项目主体是 C# / .NET 平台，不是 Python Skill 包。
- Worker 是唯一外部执行层，Agent 和 Skill 不得直接控制 SolidWorks。
- 当前版本只做 dry-run skeleton，不连接真实 SolidWorks。
- 直接复制 Python COM 脚本会绕过 `WorkerContracts`、`QualityGate` 和审计边界。
- 真实 SolidWorks 自动化需要单独处理许可证、桌面会话、COM 注册、错误码、输出校验和人工复审。

## 与本项目架构的映射关系

建议映射如下：

| 外部仓库能力 | 本项目映射 |
|---|---|
| `sw_preflight.py` | `SolidWorksEnvironmentValidator` |
| `sw_connect.py` | `SolidWorksSessionManager` / `RealSolidWorksWorker` 内部组件 |
| `sw_part.py` | `PartModelingSkill` / `PartModelingWorker` |
| `sw_drawing.py` | `DrawingGenerationSkill` / `DrawingWorker` |
| `sw_export.py` | `ExportArtifactSkill` / `ExportWorker` |
| `sw_review.py` | `SolidWorksArtifactReviewer` / `QualityGate` |
| `references/*.md` | 本项目中文技术参考文档 |
| `subskills/` | 后续专项 Module 或 Skill，例如螺纹孔、圆角倒角、钣金 |
| `mcp-server/` | 后续 AgentGateway / MCP 接入参考，不作为当前核心执行路径 |

## 建议吸收的设计思想

后续实现 SolidWorks Module 时，优先吸收以下设计思想：

1. 先做环境 preflight，再执行建模或导出。
2. 对长参数 API、枚举、by-ref 参数和单位约定保留查证记录。
3. Worker 内部隐藏 COM 细节，对上层暴露稳定、结构化、带单位的输入输出。
4. 每次输出文件都经过 artifact 存在性、大小、重建状态和预览图检查。
5. 自审查报告进入 `ReviewReport`，再由 `QualityGate` 裁决。
6. MCP 接入只能作为外部工具桥，不得绕过 Agent Gateway 和 Worker 权限边界。
7. 不开放任意 Python / VBA 执行能力。

## 禁止直接复制的内容

当前 V0.9 / V1.0 阶段禁止：

- 复制 `scripts/` 源码。
- 复制 `mcp-server/` 源码。
- 把 Python COM 脚本直接放入 Worker。
- 让 Agent 直接运行 Python、VBA、COM 或 SolidWorks 宏。
- 把外部仓库作为 git submodule。
- 绕过 `WorkflowEngine`、`WorkerContracts`、Validator、Reviewer 或 `QualityGate`。

## 许可证注意事项

该外部仓库采用 MIT License。当前项目只参考结构与工程思想，不直接复制完整源码。

如果未来引入任何源码片段，必须保留原版权声明和 MIT License，并在 `THIRD_PARTY_NOTICES.md` 中记录来源、文件、用途和修改情况。

## 后续 SolidWorks Module 实现优先级

建议优先级：

1. `SolidWorksBuildPlan`：由 Skill 生成 dry-run 建模计划。
2. `SolidWorksEnvironmentValidator`：验证环境、许可证和 COM 注册条件，但当前不实际调用。
3. `RealSolidWorksWorker` 接口草案：只定义输入输出和权限边界。
4. `SolidWorksArtifactReviewer`：定义输出 artifact 复审标准。
5. `QualityGate` 接入：把环境检查、Worker 输出和 reviewer 报告合并裁决。
6. 真实 COM 执行：必须等平台边界、测试和人工审批策略稳定后再接入。
