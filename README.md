# AutoCADByAI

程序驱动 SolidWorks 的机械设计平台：AI 编写设计项目，平台完成零件建模、中小型装配、工程图、BOM、校核与交付，每次运行的全套产物放进一个运行目录。

- 平台代码与说明：[AI_Mechanical_Engineering_Agent_Platform/README.md](AI_Mechanical_Engineering_Agent_Platform/README.md)
- 开发规则 (Codex / Claude Code)：[AI_Mechanical_Engineering_Agent_Platform/AGENTS.md](AI_Mechanical_Engineering_Agent_Platform/AGENTS.md)
- 审查报告：[reviewrep/](reviewrep/)

2026-10-09 起平台改为以可复用建模内核为中心，旧的 V2.x 治理架构保留在 git 历史中 (提交 `5e08134`)，说明见 [docs/migration.md](AI_Mechanical_Engineering_Agent_Platform/docs/migration.md)。底层目录名 `AI_Mechanical_Engineering_Agent_Platform` 保持不变。
