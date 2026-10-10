# 输出目录

每执行一次 `cad run`，就在这里新建一个运行目录 `<yyyyMMdd_HHmmss>_<任务名>/`，存放这一次建模的完整流程记录和全部产物。运行目录不入库，可以整体拷走。

```
output/20261009_153012_planetary_gear/
├── run.json        流程清单: 输入、各步骤状态与耗时、校核结论、指标 (metrics)、产物计数、SolidWorks 版本
├── run.log         完整日志
├── registry.tsv    零件登记表 (标识 → 文件), 供续跑
├── assemblies.json 本次装配体清单, 供续跑
├── input/          输入快照 (design.json 原样拷贝)
├── models/         零件 .SLDPRT 与装配体 .SLDASM (按设计的分组子目录)
├── drawings/       工程图 .SLDDRW + .PDF、图纸目录.md、index.json; _formats/ 为版面审核用的空白图框
├── exports/        STEP (AP214 含颜色)、全套图纸合订 PDF、dxf/ 激光下料 1:1 DXF
├── bom/            物料清单 BOM_物料清单.md + 分类 CSV
├── checks/         校核报告: integrity / assemblies / interference / drawing_audit / baseline (回归基准比对) / 设计专属 (如 kinematics)
└── images/         主装配体截图 (等轴测、主视、俯视、背面、内部结构)
```

判断一次运行是否可信，看 `run.json` 的 `status` 与 `checks`：任何一项校核不通过，运行状态即为 `failed`，产物保留供排查。

续跑已有运行目录 (例如零件和装配已完成，只重出工程图)：

```powershell
.\cad resume 20261009_153012_planetary_gear --from drawings
```

运行目录可写目录名、`output/<目录名>` 或完整路径。从 `parts` 续跑会重新建模，不沿用旧登记表。
