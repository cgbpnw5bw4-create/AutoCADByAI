# 输入目录

每个设计任务一个子目录，至少包含 `design.json`；同目录下的其它文件 (需求说明、参考图、计算书) 会随运行一起拷入运行目录的 `input/` 快照。

```jsonc
{
  "project": "planetary_gear",          // 设计项目 (src/CadProjects 中的 Id, `cad list` 可查)
  "title": "行星齿轮减速器 i=5",          // 写入零件属性、BOM 与图纸合订本标题
  "steps": ["parts", "assembly", "checks", "drawings", "export", "post"],   // 可省略 = 全部
  "params": { "module": 2, "z_sun": 18 } // 由设计项目解释; 未声明的键会被当作拼写错误拒绝
}
```

参数在连接 SolidWorks 之前校验 (如行星轮系的同心、装配、邻接条件)，不合格直接退出，不产生运行目录。

| 任务 | 设计项目 | 说明 |
|---|---|---|
| `planetary_gear/` | `planetary_gear` | 2K-H 行星齿轮减速器: 渐开线齿轮零件、齿轮配合运动装配、多角度运动学与干涉校核、零件图与装配图 |
| `planetary_gear_m15/` | `planetary_gear` | 同一设计的第二组参数 (m=1.5, 21/27/75, 齿宽 15): 验证按模数缩放的派生尺寸 |
| `cfc650_m03/` | `cfc650_module` | CFC-650 铜箔清洗机 M03 碱洗模块: 约 330 件的中型固定布局装配, GB 紧固件穿孔, 干涉兼作孔位校核 |
| `microwave_furnace/` | `imported` | 由 STEP 导入的微波实验炉 (58 种 229 件): 直管参数化、自制件按实体特征重建 (截面拉伸 73 + 钣金 26 = 99/111 个实体, 逐实体差集为 0)、实例位置与原 STEP 核对、原模型干涉登记 (674 处)、5 张大装配图; 整机约 50 分钟。`geometry/` 与源 STEP (`inbox/`) 不入库, 缺失时按 AGENTS.md「验证」重新导入 |

**由 STEP 导入的设计**：`cad import <STEP> --task <任务>` 生成 `design.json` (`"project": "imported"`)、`import.json` 与 `geometry/`。`import.json` 是这台设备的可编辑描述：
- `parts`：零件表。`kind` 自制件 / 外购件，`code` / `name` / `spec` / `brand` 进 BOM，`material` 为 `Mat` 的字段名 (`S304`、`AL`、`PVC`、`UNKNOWN` …)；`model` 为 `geometry` (原几何文件)、`tube` (参数化直管：`axis`、`center`、`start`、`length`、`dout`、`din`) 或 `bodies` (按实体重建：`prisms` 中每个实体是截面沿坐标轴的拉伸 —— `axis`、`from`、`to` 与截面 `loops`，环由直线 `L`、圆弧 `A`、整圆 `O` 组成，坐标为草图 u,v，`box` 为原实体包围盒；可再带斜口 `cuts` (每个切去过点 `p`、外法向 `n` 一侧，`n` 须平行于某个坐标面) 与横穿孔 `holes` (`axis`、草图坐标 `c`、直径 `d`、沿孔轴 `from`→`to`)，切除只作用于该实体；`sheets` 中每个实体是 SolidWorks 钣金 —— 基板垂直于 `axis`、上表面在 `top`、板厚 `t`、折弯内半径 `r`、角部间隙 `gap`，`loops` 为基体法兰草图 (外环为外部虚拟交点轮廓，内环为基板上的孔与切口)，`flanges` 为各条边线法兰 (`edge` 外环上的一段、`dir` ±1 朝向、`length` 高度、`return` 末端回折边宽度)，`cutouts` 为法兰上的孔与从边缘切进去的缺口 (每个是一个封闭环 `loop`，沿 `axis` 从 `from` 切到 `to`)；未能识别的实体保留 `file` 中的原几何，运行时载入后删去被重建的实体)。改截面坐标、拉伸长度、孔位孔径、板厚与法兰高度即改零件。
- `assemblies`：装配层级。每个子件 `ref` 引用零件或子装配，`xf` 为 12 个数 (局部 X/Y/Z 轴方向 + 平移 mm)。移动、替换、增删实例都在这里改。
- `world`：原 STEP 中每个零件实例的位置，供 `cad run` 核对；改了装配后，"装配位置与原 STEP 一致" 会如实报出差异。
- `accepted_interferences`：原模型自带的干涉 (导入时对原装配做干涉检查得到；同一对零件、同一相对位置、同样体积的只记一条)。运行时与之相符的干涉记为"已登记接受"、仍逐条列入 `checks/interference.md`，只有新增的判不通过。移动了零件或改了尺寸，相关干涉就对不上，会按新增报出。不要手工往里加条目。
- `geometry/` 不入库，可由源 STEP 重新导入 (`--force` 会覆盖 import.json 的手工修改)。

**回归基准** `expected.json`：回归样例目录里另有这个文件，记录人工确认过的结果 (零件数、外形、质量、STEP 实体数、图纸清单与每张图的图幅 / 比例 / 尺寸数 / 外形尺寸读数等)，运行结束时逐项比对。由 `cad baseline <运行目录>` 生成，不要手改数值；结果有意改变时重新生成，并在提交说明里写明原因。

新设计: 在 `src/CadProjects/<名称>/` 写设计项目 (继承 `CadProject`)，再在这里建 `input/<任务>/design.json`。做法见 [AGENTS.md](../AGENTS.md)。
