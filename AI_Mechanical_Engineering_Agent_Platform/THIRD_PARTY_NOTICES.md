# 第三方声明

本文记录当前项目参考或可能引用的第三方资料。

## solidworks-automation-skill

仓库地址：

```text
https://github.com/wzyn20051216/solidworks-automation-skill
```

该仓库采用 MIT License。

当前项目只参考其结构与工程思想，包括能力分类、preflight、自审查、API 查证优先和 MCP 工具封装边界。当前项目没有复制该仓库的完整源码，也没有把该仓库作为 git submodule。

当前 V0.9 / V1.0 阶段不得复制其 `scripts/` 目录源码，不得把 Python COM 脚本直接塞进 Worker，也不得绕过平台的 Agent、Skill、Worker、Validator、Reviewer 和 `QualityGate` 分层。

如果未来确实需要引入该仓库的源码片段，必须：

- 保留原版权声明和 MIT License。
- 在本文中记录来源文件、用途和修改情况。
- 确认引入代码仍然通过 Worker 边界执行。
- 确认 Agent 和 LLM 不能直接调用 SolidWorks、COM 或外部脚本。
