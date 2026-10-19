# AutoCADByAI — 程序驱动 SolidWorks 的机械设计平台

由 AI 编写设计项目，平台在 SolidWorks 中完成**零件建模、中小型装配、工程图**，每次运行当场校核并把全套产物放进一个运行目录。

- 内核与零件库来自两个已实测的项目：CFC-650 铜箔清洗机 V2 (238 零件 / 39 装配 / 106 张图) 和行星齿轮减速器 (齿轮配合运动装配)。
- 今后的每个设计都在同一内核上完成，可复用的零件族沉淀进零件库 (见 [docs/library.md](docs/library.md))。

## 快速开始

需要 Windows、SolidWorks 2023、.NET 10 SDK、Python 3 + PyMuPDF (`pip install -r python/requirements.txt`)。

```powershell
dotnet build AutoCADByAI.sln
dotnet test
.\cad doctor                  # 环境检查, 不启动 SolidWorks
.\cad run planetary_gear
```

`cad` 是平台根目录的 `cad.cmd`：先增量构建，再运行 `src/CadCli`，保证运行的总是当前源码。本机 SolidWorks 目录与 Python 解释器写在 `config/cad.json` (Python 写完整路径：`WindowsApps` 下的 `python.exe` 别名可能排在 PATH 前面)。

程序会自行启动一个**专用 SolidWorks 实例**，不操作你自己打开的 SolidWorks 窗口：实例记录同时核对 PID 与进程启动时间，PID 被系统复用时不会误认你的 SolidWorks。运行期间临时关闭用户选项"输入尺寸值"、导出 STEP 时临时修改 STEP 选项，结束后自动恢复。

## 目录

```
input/<任务>/design.json   输入: 每个设计任务一个子目录 (见 input/README.md); 由 STEP 导入的另有 import.json
inbox/                     外来资料暂存 (客户 STEP 等, 不入库)
output/<时间>_<任务>/       输出: 每次运行一套完整产物与流程记录 (见 output/README.md, 不入库)
src/CadKernel              内核: 会话 / 建模 / 装配 / 紧固件 / 出图 / BOM / 校核 / 导出 / 运行目录
src/CadLibrary             零件库: 渐开线齿轮、外购件样本、细节造型 (持续沉淀)
src/CadProjects            设计项目: 行星齿轮减速器 (两组参数)、CFC-650 碱洗模块、STEP 导入 (微波实验炉) —— 均为回归样例
src/CadCli                 命令行 cad (根目录 cad.cmd 构建后调用)
python/                    输出后处理: 图纸版面审核、全套图纸合订
tests/CadTests             纯逻辑测试 (不连接 SolidWorks)
config/cad.json            本机 SolidWorks 安装目录与 Python 解释器
docs/                      architecture / api_calibration / library / migration
```

## 命令

| 命令 | 作用 |
|---|---|
| `cad run <任务> [--from 步骤] [--drawings 过滤]` | 新建一次运行：参数校验 → 零件 → 装配 → 校核 → 工程图 → 导出 → 后处理 |
| `cad resume <运行目录> [--from 步骤]` | 在已有运行目录上续跑 (默认从工程图) |
| `cad baseline <运行目录>` | 看图确认无误后，把该次结果写成回归基准 `input/<任务>/expected.json` |
| `cad import <STEP> [--task 任务名]` | 把外来 STEP 转成平台的设计 (`input/<任务>/import.json` + 原几何)，之后 `cad run <任务>`；见 docs/architecture.md「由 STEP 导入的设计」 |
| `cad list` | 设计项目、输入任务、最近运行 |
| `cad doctor` | 环境检查 (含各输入任务的模型路径长度: 平台目录过深时出图会挂起, 用 `subst` 映射短盘符) |
| `cad clean` | 关闭专用实例中 output/ 下的文档 |
| `cad diag <文件>` | 特征树、包围盒、体积 |

## 每次运行的校核

模型完整性 (逐个重开: 加载警告、重建错误、浮动/缺失零部件)、干涉 (兼作孔位校核)、BOM、工程图生成 (含"每张图至少有尺寸") 与参考检查、STEP 身份与内容、图纸版面审核，以及设计专属校核 (如行星轮系多角度运动学)。结论写入 `run.json`，任何一项不通过则运行状态为 `failed`。

这些校核只能说明"没有报错"。回归样例另有**回归基准** (`input/<任务>/expected.json`)：零件数、装配外形、总质量、STEP 实体数、图纸清单与每张图的图幅 / 比例 / 尺寸数 / 外形尺寸读数等逐项与人工确认过的结果比对，偏离即不通过 (见 [docs/architecture.md](docs/architecture.md) 第 2 节)。

## 文档

- [docs/architecture.md](docs/architecture.md) — 三层结构、运行流程、语言分工与冗余分析
- [docs/api_calibration.md](docs/api_calibration.md) — 经实测标定的 SolidWorks API 行为
- [docs/library.md](docs/library.md) — 零件库条目与沉淀规则
- [docs/migration.md](docs/migration.md) — 2026-10-09 重构说明与旧内容去向
- [AGENTS.md](AGENTS.md) — Codex / Claude Code 开发规则
