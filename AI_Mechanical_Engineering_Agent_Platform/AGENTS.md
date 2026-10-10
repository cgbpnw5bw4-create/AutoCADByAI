# 开发规则 (Codex / Claude Code)

本仓库是程序驱动 SolidWorks 的机械设计平台。你的主要工作是**编写设计项目**，并把可复用的部分**沉淀**进零件库或内核。先读 [README.md](README.md) 与 [docs/architecture.md](docs/architecture.md)。

## 分层与边界

- `src/CadKernel` 是**唯一**调用 SolidWorks COM 的程序集。零件库和设计项目通过内核 API (`Sk`、`Turn`、`Features`、`Cat`、`AsmBuild`、`MateAsm`、`Std` …) 建模，可以使用 SolidWorks 类型，但不新开 SolidWorks 会话。
- 零件库 `src/CadLibrary` 不依赖任何设计项目；设计项目 `src/CadProjects/<设计>` 只调用内核和零件库。
- Python (`python/`) 只做输出后处理，不调用 COM。
- 输出只写当次运行目录 `Run.Dir` 下的 `models/ drawings/ exports/ bom/ checks/ images/`；不写平台其它位置。

## 新设计的做法

1. 查 [docs/library.md](docs/library.md) 与 [docs/api_calibration.md](docs/api_calibration.md)，能复用的直接用。
2. 在 `src/CadProjects/<设计>/` 新建类继承 `CadProject`：
   - `Validate()`：读取并校验参数 (不连接 SolidWorks)，返回问题列表；
   - `Prepare()`：项目级设置 `Cat.ProjectName / Version / Date`，零件多时设 `Cat.Lazy = true` 只建用到的。不要写在 `BuildParts` 里 —— 续跑跳过零件步骤时会丢失；
   - `BuildParts()`：用 `Cat.Make(new Part{...}, m => ...)` 建零件；
   - `BuildAssemblies()`：固定布局用 `AsmBuild` (变换矩阵 + 全部固定)，运动机构用 `MateAsm` (配合)；返回 `AsmTarget` 列表，恰好一个 `Main`；
   - 可选：`ConfigureDrawings()` 技术要求、`ExtraChecks()` 专属校核、`BomExtras`、`InteriorHide`。
3. 建 `input/<任务>/design.json`，运行 `cad run <任务>`，以 `run.json` 的 `status` 与 `checks` 判断结果。
4. 运行通过后，把下次还会用的零件族按 docs/library.md 第 2 节门槛提炼进零件库，并在该文件登记。
5. 要长期保留的设计：逐张看图确认无误后 `cad baseline <运行目录>`，生成 `input/<任务>/expected.json`，它就成为回归样例。

`cad` 指平台根目录的 `cad.cmd` (先增量构建再运行)。

## 必须遵守

- **不得绕过三道防线**：建模一律经 `Sk` 的 `Ext/Cut/CutThru/Rev` (体积校验) 和 `Cat.Make` (孤立草图、实体数)；新增特征类型时同样在特征后读回校验 (见 `Features.cs`)。SolidWorks 特征 API 失败时不抛异常。
- **紧固件真实穿孔**：连接件用 `AsmBuild.BoltNut / BoltTap`，螺纹孔按公称直径建模，让干涉检查兼作孔位校核。
- **校核结论必须如实**：检查本身失败不得记为通过 (如干涉检查异常不能当作 0 处)；不得为让运行通过而放宽判据。校核名称要和判据一致；没有绝对判据的量 (外形、质量、尺寸数) 用 `Run.Metric` 登记，交给回归基准比对，不要包装成"总是通过"的校核。
- **回归基准**：改动内核或零件库后，回归样例的 `回归基准` 必须通过。结果有意改变时，先看图确认，再 `cad baseline <运行目录>` 重新生成，并在提交说明里写明哪些数值变了、为什么；不得为了通过而放宽允差或删条目。
- **静态状态**：内核静态类的可变状态由流水线在每次运行开始时重置 (`Cat.Reset`、`Std.Reset`、`Drw.Reset`)；新增可变静态字段时一并加进对应的 `Reset()`。
- 新标定的 API 行为写进 `docs/api_calibration.md` 对应小节，注明来源与日期。
- 运行目录 `output/` 与 `.cad/` 不入库。
- 不在单元测试中启动 SolidWorks；真实运行用 `cad run`。

## 验证

```powershell
dotnet build AutoCADByAI.sln          # 0 错误 0 警告
dotnet test                           # 纯逻辑测试全部通过
.\cad run planetary_gear              # 改动内核、零件库后: 回归样例 run.json 为 passed (含回归基准)
.\cad run planetary_gear_m15          # 涉及零件库齿轮、参数化派生尺寸时
.\cad run cfc650_m03                  # 涉及装配、紧固件、出图版面时
```

需要真实 SolidWorks 的验证会启动专用实例；它不碰用户自己的 SolidWorks 窗口，但会临时修改并恢复少量用户级选项 (见 api_calibration.md 第 4 节)。

## 文档

说明文字用中文；代码标识符、路径、命令、API 名称保留英文。文档只有 README、AGENTS 与 docs/ 下 4 份，不按阶段新增文档；设计说明写在设计项目的类注释里。
