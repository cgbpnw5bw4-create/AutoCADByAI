# Markdown 中文撰写规范

本项目所有 Markdown 文件必须使用中文撰写。该规则用于保证架构说明、模块说明、审查模板和后续知识沉淀在同一种语言体系下维护。

## 适用范围

适用范围包括但不限于：

- 根目录 `README.md`。
- `docs/*.md`。
- `src/**/README.md`。
- `src/**/SKILL.md`。
- `references/**/*.md`。
- Module 文档。
- Worker 文档。
- Validator / Reviewer 文档。
- self-check 说明文档。
- 审查报告模板。

## 必须中文的规则

所有解释性文字、说明性文字、使用说明、架构说明、模块说明、开发规范、审查结论和操作注意事项必须使用中文。

标题、段落、列表说明和表格说明也必须以中文为主。不得在普通说明段落中保留大段英文描述。

## 允许保留英文的例外

以下内容可以保留英文：

- 类名、接口名、方法名、命名空间。
- 文件路径。
- 命令行命令。
- API 名称。
- NuGet 包名。
- Git commit message。
- 许可证原文。
- 必须引用的第三方术语。
- 代码块中的源码、JSON、YAML、PowerShell、C#、Python 等内容。

保留英文标识符时，应在周围用中文解释其作用和边界。

## 文件级例外标记

如果某个 Markdown 文件必须完整保留英文原文，例如许可证原文，可以在文件顶部加入：

```markdown
<!-- markdown-lang: exempt -->
```

该标记只应用于确有必要的文件，例如第三方许可证原文。普通 README、架构说明、模块说明和开发规范不得使用该标记规避中文要求。

如果某个新增草稿暂时包含英文说明，但希望 self-check 先记录为 Warning 而不是 Failed，可以在文件顶部加入：

```markdown
<!-- markdown-lang: warning -->
```

该标记只适合短期草稿。进入正式交付前，应删除该标记并改为中文说明。

## 阈值配置

`MarkdownChineseValidator` 支持通过 `MarkdownChineseValidatorOptions` 调整判断阈值：

- `MinEnglishLetters`
- `EnglishRatioThreshold`
- `EnglishToChineseMultiplier`
- `ExemptRelativePaths`

默认阈值适用于当前项目。只有在规则误判或项目文档规模明显变化时，才应调整阈值。

## 第三方许可证原文处理

第三方许可证原文可以保留英文，不应擅自翻译为唯一版本。若需要说明许可证含义，应在原文旁边增加中文说明。

当前项目引用第三方资料时，应在 `THIRD_PARTY_NOTICES.md` 中说明来源、许可证、使用方式和是否复制源码。

## 第三方参考资料中文化要求

第三方仓库、论文、文档或示例可以作为参考资料，但沉淀到本项目的 Markdown 必须用中文重新组织。不得直接复制大段英文说明，也不得直接复制第三方源码作为项目实现。

对于 `solidworks-automation-skill` 这类外部仓库，本项目只吸收结构思想、能力分类、preflight、自审查、API 查证和 MCP 封装边界，不能直接复制其 `scripts/` 源码。

外部参考资料分析报告统一放在 `references/external/`，并遵守该目录下的 README 规范。

## self-check 检查标准

self-check 会扫描项目内所有 `.md` 文件，并跳过 `.git`、`bin`、`obj` 和 `output` 临时目录。

检查器会忽略 fenced code block、inline code、路径、URL 和命令中的英文。普通说明段落如果英文比例过高，会被记录为 Issue 或 Warning。

`docs/`、`README.md`、`src/**/README.md` 和 `references/**/*.md` 中出现大段英文说明时，默认应导致 self-check Failed。带有 `markdown-lang: warning` 的草稿文件会被记录为 Warning，主流程仍可继续，但必须在正式交付前处理。

## 违反规则时的处理方式

发现不合规 Markdown 时，应先把说明性文字改为中文，再重新运行：

```powershell
dotnet run --project src\Interfaces\CliHost -- self-check
```

如果文档中必须保留英文，应确认它属于允许例外，并在上下文中补充中文说明。
