# 孔特征失败修复

## 目标与适用范围

本手册处理 V2.1-B 四类孔在定义、Worker 前预检、Adapter、GeometryValidator 与 QualityGate 的失败。复用现有 `HoleHandler`，不增加专用 Agent 或绕过执行链。

## 输入与输出

输入为原始及标准化孔参数、FeatureGraph、参考面、API evidence、当次 Feature/Geometry 报告；输出必须有 `failure_stage`、直接原因、证据位置、修复动作和下一步验证命令。没有真实几何读数时记录未验证，不能补造期望值充当实测值。

## 失败映射

| `failure_stage` | 直接原因与证据 | 修复动作 | 关闭条件 |
|---|---|---|---|
| `unsupported_hole_type` | `hole_type` 不在四类型目录，见输入与 Handler 结果 | 更正类型，不默认为普通孔 | 未知类型在 Worker 前拒绝，合法类型标准化正确 |
| `invalid_hole_parameter` | 孔径/深度非法，沉孔或沉头比例无效，角度/螺纹目录/孔位或数量依赖不合法 | 修正原始输入并保留失败值，不在 Adapter 偷补默认尺寸 | 正负参数回归通过，非法请求 Worker 与 COM 调用为零 |
| `hole_reference_face_missing` | 引用缺失、无法解析或与图中放置依据不符 | 补足通用图引用与合法位置；不得猜测实体面 | 可证明引用有效，未知或不匹配引用仍前置拒绝 |
| `simple_hole_execution_failed` | 普通孔 Adapter 路径、草图选择或切除失败 | 读取当次选择、API 参数和返回，隔离重现选定策略 | 结果、重建及孔几何均通过 |
| `counterbore_execution_failed` | 已授权沉孔创建失败 | 分别核对主孔与大径台阶，禁止退化成单径孔 | 同次实测两级同轴圆柱、直径和台阶深度通过 |
| `countersink_execution_failed` | 已授权沉头孔创建失败 | 核对近侧、入口径、角度单位与终止 | 同次锥面、入口径、角度和主孔通过 |
| `tapped_hole_execution_failed` | 已授权攻丝孔创建或标准/规格映射失败 | 读取孔向导定义和错误，不改为普通 Cut 兜底 | 孔几何及实际孔向导类型、螺纹元数据均通过 |
| `hole_geometry_validation_failed` | 孔数量/中心/直径/深度/沉孔/沉头/螺纹元数据缺失或不匹配 | 对照当前模型独立读数逐项定位；缺读数先补 Reader 证据，不直接改 Validator 为通过 | 每孔测量与期望、可用性和容差均通过，质量门禁读取同次报告 |
| `tapped_hole_api_unverified` | 标准攻丝孔证据未验证或不适用于请求 | 保留未验证，先补标准数据库、选择、创建和元数据读回诊断 | 精确档案和当前源码受证；此前保持真实执行连接计数为零 |
| `feature_api_unverified` | 普通孔增强档案、沉孔或沉头孔无有效证据，或复合源码/运行时/报告不匹配 | 按 [孔 API 证据](api_evidence.md) 重采准确策略；不得重写旧报告源码绑定 | 当前证据门禁通过且仅授权精确档案 |
| `feature_adapter_missing` | Worker 没有注入 Adapter | 修复现有注入链，Handler 不自行创建 COM 会话 | 注入测试和受控分发通过 |
| `feature_result_invalid` / `feature_artifact_missing` | 返回/重建无效，或当次 SLDPRT、STEP、报告缺失 | 修复当次创建、保存、导出与报告，保留诊断 | 有效结果、STEP 内容、独立几何及质量门禁均通过 |

历史 `hole_execution_failed` 与 `invalid_feature_parameter` 保留为旧报告背景；V2.1-B 类型化路径使用上表专用阶段。实际输出应以失败发生的层次为准，不用更早一层的通用标签覆盖孔类型原因。

## 执行步骤

1. 先读原始输入与失败报告，定位类型、参数、引用、API 或几何层。确认失败不是同次报告缺失或历史证据误用。
2. 对照有限参数与引用合同，在 Worker 前修正确定的输入问题；通孔免必填盲孔深度不等于所有深度相关约束失效。
3. 涉及 SolidWorks API 时先查官方、SDK、宏与已有 evidence，再使用隔离 Runner；标准攻丝孔不得用草图切除替代。
4. 输入修复后用 `dotnet run --project src/Interfaces/CliHost -- dry-run-cad --input examples/hole_simple_plate.json` 复现，其余孔型替换样例文件。该命令强制模拟，不受输入 `dry_run=false` 或真实环境开关影响；查看终端给出的 `dry_run_report.json`，模拟通过仍为 `NotDeliverable`、`real_cad_executed=false`。
5. 每次变更后运行相关孔回归，以及下列项目验证命令；诊断仅提供证据，最终真实验收仍走完整主工作流。

```powershell
dotnet build AI_Mechanical_Engineering_Agent_Platform.sln
dotnet test
dotnet run --project src/Interfaces/CliHost -- self-check
```

## 验证标准

四个默认 dry-run 样例必须通过；未知类型、非法尺寸、引用缺失、孔位越界、错误螺纹和未验证真实执行必须受控拒绝；非空 Feature 缺少几何读数必须失败。准确记录 build、test、自检阶段及总体状态，不能把本阶段通过写成全环境已就绪。

## 禁止事项

禁止 Handler 直接 COM、大型类型 switch、无证据真实执行、未知孔型回退、普通 Cut 冒充攻丝、装饰螺纹冒充螺旋实体、旧产物补证、修改 `reviewrep`、复制第三方脚本或进入 V2.1-C。
