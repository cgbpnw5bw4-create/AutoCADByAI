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

**回归基准** `expected.json`：回归样例目录里另有这个文件，记录人工确认过的结果 (零件数、外形、质量、STEP 实体数、图纸清单与每张图的图幅 / 比例 / 尺寸数等)，运行结束时逐项比对。由 `cad baseline <运行目录>` 生成，不要手改数值；结果有意改变时重新生成，并在提交说明里写明原因。

新设计: 在 `src/CadProjects/<名称>/` 写设计项目 (继承 `CadProject`)，再在这里建 `input/<任务>/design.json`。做法见 [AGENTS.md](../AGENTS.md)。
