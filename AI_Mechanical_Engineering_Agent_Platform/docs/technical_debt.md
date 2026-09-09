# 技术债记录

## V2.1-A 复杂特征库审查改进项（2026-09-07）

### 目标、适用范围与输入输出

本节登记复杂特征库审查的全部改进建议，作为后续独立任务的输入。来源为 [V2.1-A 复杂特征库 Claude 审查报告](../../reviewrep/2026-09-07-v2.1-a-complex-feature-library-review.md)，被审源码为 `HEAD=bbeafc9`，结论为 `PASS WITH COMMENTS`，报告第 3 节明确没有 `Blockers`。该报告不同于 2026-08-17 的夹套审查。

报告作者同时参与被审实现，独立性受限；源码差异、测试输出和诊断数值可以逐项复核，架构方向、引用回读降级及授权范围等自评结论仍须由未参与者复审。用户明确要求 `Improvement` 记录到 backlog 且不阻塞 V2.1-B，因此下列事项本轮仅登记，不随孔特征增强顺带实施，也不表示风险已经关闭。

### 改进项清单

| 来源编号 | 问题与影响 | 后续处理与验收条件 | 当前状态 |
|---|---|---|---|
| `HIGH-001` | 阵列 `seed_feature` 的 Schema 按单个标识校验，Adapter 按分号或逗号拆成多个种子；空 `referenced_features` 跳过绑定检查，可能超出单种子证据与图依赖 | 统一拆分规则并要求恰好一个种子；无条件检查引用，镜像相同前置条件一并核查；补非法输入在 COM 前拒绝测试。多种子须作为新参数档案单独取证 | 待处理，优先安排 |
| `HIGH-002` | 线性与圆周阵列翻转标记恒为 `false`，边方向符号随拓扑可能变化；错误方向可能仍有相同体积 | 设计相对声明主轴的 `direction_sign` / `flip_direction`；用带符号余弦计算翻转；覆盖同向、反向、非整周和落点；参数档案改变后重新真机取证 | 待处理，优先安排 |
| `HIGH-003` | `EdgeDirection` 的圆参数索引、端点差和归一化缺少自动测试 | 覆盖普通、零与非有限向量，真实探针方向夹具、同向/反向/45 度平行性，保证纯逻辑不依赖 SolidWorks | 待处理 |
| `MEDIUM-001` | 圆周阵列轴只校验平行方向，未交叉验证轴心位置；可能绕错误孔心阵列 | 设计 `axis_origin_x_mm` / `axis_origin_z_mm` 或等价通用意图，与解出圆边锚点按容差比对，补位置反例并重新取证 | 待处理 |
| `MEDIUM-002` | Adapter `Dispose` 清空 `_sketches` 却保留 `_features` 中已释放的 RCW 引用 | 生命周期收尾清空 `_features`，验证不使用释放后对象 | 待处理 |
| `MEDIUM-003` | 规格层与 Handler 参数 Schema 曾出现 `count` / `instance_count` 矛盾，缺少一致性保护 | 从 Registry 枚举所有 Handler，用有效必填参数构造定义并校验两层合同，包含反例 | 待处理 |
| `MEDIUM-004` | Adapter 的主轴、平行性、种子拆分、实例数和特征类型等新增判据缺少自动测试 | 抽取不依赖 COM 的纯判断并测试；`VerifyCreatedFeatureKind`、`SelectEntity` 等 COM 路径保留诊断证据，不引入无必要的 COM 模拟 | 待处理 |
| `MEDIUM-005` | `DiagnosticFeatureTypes` 硬编码白名单与 Registry 重复，遗漏时错误信息误指未注册 | 明确诊断限制后改用 Registry 全集或排除列表；区分未注册与诊断范围拒绝，补新增 Handler 回归 | 待处理 |
| `LOW-001` | 15 个未引用中间 evidence 目录与当前绑定证据混放，增加误绑定和排查成本 | 先核实所有引用，提出归档/清理清单；保留当前绑定与审计链，禁止本轮顺带删除 | 待处理 |
| `LOW-002` | 审查协议称 `self-check` 为只读验证，但命令实际写入受控 `output/reports/` | 后续增加 `--output` 指向临时目录，或明确修订协议的生成报告例外；验证审查路径且不得用清理命令隐藏工作区变化 | 待处理 |
| `LOW-003` | `AxisParallelCosine=0.999` 的容差依据未记载 | 基于主轴实测误差决定收紧至 `1-1e-6` 或保留并解释建模公差；补边界用例 | 待处理 |
| 审查第 7 节、第 11 节第 11 项 | `anchor_*` 使用绝对坐标，参数化尺寸更新后引用可能失效；这是相对引用模型的设计缺口 | 在把阵列/镜像接入参数化零件族前评审面归属、长度排序、包围盒相对位置等通用判据，记录独立设计结论与变参反例 | 待设计，本轮不扩展 |
| 审查第 8 节、第 11 节第 8 项 | 成功路径未记录实际解出的边，难以从运行日志复核选择 | 在 `SelectEdgesByCriteria`、`ResolveAxisEdge` 记录 `index`、`kind`、`anchor`、`direction`，验证信息来自实测且足以关联诊断 | 待处理 |
| 审查第 8 节 | 诊断证据目录依赖操作者显式指定 `--output`，默认 `output/solidworks/` 被忽略 | 评估可追踪默认输出与证据提升流程，防止遗漏应保存的诊断；不自动把中间产物升为生产证据 | 待处理 |
| 审查独立性声明及第 7 节 | 边引用设计、`GetTypeName2` 替代引用回读、授权范围与整体架构为作者自评 | 由未参与实现的审查者复核运行期与取证期保护强度，重点检查引用确实写入及落点正确，保留独立审查结论 | 待独立复审 |
| 历史 `LOW-001` / `ARCH-001` 及延后项 | `CommonFeatureTemplates` 未补齐，未注册的专用 Builder 尚留存，`revolve_boss` 无通用 Feature 证据 | 分别开展模板复用、无调用代码清理评估与旋转特征独立采证；没有 evidence 时 `shaft_basic` 继续失败关闭 | 继续延后 |

### 执行步骤与验证标准

1. 后续任务先复核来源报告、当前代码及现有证据，确认问题仍存在后制定最小改动。
2. 优先评估 `HIGH-001`、`HIGH-002` 的错误 CAD 输出风险；本轮登记不等于接受它们作为新增能力的依据。
3. 修改纯判据后运行相关测试，再运行 build、test、self-check；修改 `BoundSourcePaths` 或真实参数档案后重新采集相应证据，不直接重写旧报告的源码绑定。
4. 只有独立复核、自动检查和所需同次真机证据齐全时才逐条关闭；总体自检失败必须与本阶段检查结果分别说明。

### 常见失败与禁止事项

不得把本节改进项重命名为准入 `Blockers`，不得以登记 backlog 冒充修复，也不得借此修改只读 `reviewrep`、删除证据、扩大真实执行授权或进入 V2.1-C。新增实际阻断仍应如实报告，不能套用非阻断结论忽略。

## V1.1 Claude Improvements

V1.1 Claude 审查未发现 Blockers，仅提出 Improvements。以下事项进入技术债，不阻塞 V1.2 主线：

- 为 `ExecuteWithApplicationAsync` 后续补充更明确的真实执行超时边界。
- 清理历史切孔候选中的死代码，降低后续 API 修复噪声。
- 统一 `repair_log` 命名，避免报告字段语义分散。
- 清理或归档旧 evidence JSON，保留当前阶段可复现证据。
- V1.6 已消除 V1.1 工程图报告的重复写入。
- 后续抽取保存、导出、COM 释放等 SolidWorks 共享工具。

处理规则：以上事项不得作为 V1.2 Blocker。若后续阶段触碰同一代码路径，应优先就近修复，并遵守 V2.0 的本地交互默认启用、CI/单测/dry-run 禁用规则。

## V1.2 Claude Improvements

V1.2 Claude 审查结论为 PASS WITH COMMENTS，未发现 Blockers，仅提出 Improvements。以下事项进入技术债，不阻塞 V1.3 主线：

- `ExecuteWithApplicationAsync` 真实执行阶段仍缺少强制超时边界；V2.0 本地交互默认启用后，该超时护栏优先级提高，CI、单元测试和 dry-run 继续禁止真实执行。
- V1.2 尺寸标注使用非关联、硬编码的最小尺寸策略；后续阶段若扩展语义标注，应在报告中继续暴露非关联边界并避免虚假成功。
- V1.2 尺寸 Builder 的内部失败分支仍缺少更细的纯单元测试；后续补测试时优先覆盖 `source_drawing_missing`、视图缺失和保存/导出失败路径。
- 后续抽取工程图保存、PDF 导出、报告写入和 COM 释放的共享工具，减少 V1.1/V1.2/V1.3 间重复实现。

处理规则：以上事项不得作为 V1.3 Blocker。本阶段仅实现标题栏/图纸属性最小链路，不扩大到 BOM、复杂国标模板、公差系统或 V1.4。
## V1.3 Claude Improvements

V1.3 Claude 审查结论为 PASS WITH COMMENTS，未发现 Blockers。以下事项作为后续技术债登记，不阻塞当前最小标题栏链路：
- 标题栏 `Passed` 目前表示自定义属性已写入并可回读，不表示图纸模板 Sheet Format 中的 note 已可见渲染。报告和 self-check 必须持续暴露 `title_block_population_strategy=custom_properties_only` 与 `title_block_fields_verified_in_sheet_format=false`，直到后续版本实现模板 note 链接校验。
- V1.6 已把标题栏、尺寸、工程图和零件 Builder 的 COM 调用层抽象为可注入接口，并补充属性回读不一致、保存/导出缺失及 COM 返回空值等行为测试。
- 旧工程图 report 双写、历史 evidence JSON 清理、共享保存/导出/COM 释放工具抽取继续按“触碰即就近修复”的规则处理。
处理规则：`ExecuteWithApplicationAsync` 执行超时已在本轮从技术债提升为修复项；其余事项不得被误读为 V1.4 Blocker。真实 SolidWorks 默认仍保持关闭，只有显式 smoke 开关才允许启动 COM/CAD Runtime。

## V1.6 后续整理

- `SolidWorksFakeSuccessGuard` 当前仍与 COM facade 位于 `SolidWorksComInterop.cs`，后续可纯移动到独立文件，保持行为不变。
- 默认四步工程路由已收口为可注入的 `InternalWorkflowRoute.EngineeringDefault`；动态路由编排仍需要独立设计路由选择策略与回归测试。
- Router 对不受支持结构化类型的负路径仍需补专门测试，确保不会静默回退到受控零件类型。
- 平台 self-check 已写入 `schema_version`、`run_id`、`generated_at` 和 `source_revision`；发布包各上游阶段报告的跨产物 provenance 仍需后续统一 schema，才能做完整的运行级交叉校验。

## V1.7 Improvements Backlog

- 主工作流每个阶段仍各自建立和释放 COM 会话；后续可在不改变 Worker 边界和串行 COM 规则的前提下研究会话复用。
- 标题栏仅验证自定义属性写入、回读和重建；Sheet Format note 的可见渲染校验需要独立 API 证据与模板兼容测试。
- E2E source execution evidence 当前由受控主工作流内存结果写入 manifest；后续可将 request_id/provenance 写入全部阶段报告的统一 schema。

处理规则：以上事项均为 Improvement，不阻塞 V1.7 主流程；不得借技术债实现新的 CAD 功能、BOM、装配体或复杂模板。

## V1.8 零件族技术债与边界

### 目标与适用范围

本节记录参数化零件族在完成首批三族后仍需要跟踪的事项。输入为 V1.8 实现、测试、self-check 与 API evidence；输出为后续 Improvement 列表，不得被解读为本轮可以省略的验收条件。

### 完成 V1.8 前必须清理的事项

- 主执行链中分散的 `plate_basic_4holes` 类型判断必须收口到 `PartTypeRegistry` 和对应 Definition / Builder。与旧版真实工程图、报告文件名和 plate 专用发布包相关的历史实现可以保留，但不得参与通用零件族分发。
- 不得留下大型 `switch(part_type)`。self-check 中的 `part_family_builders_do_not_use_large_switch` 必须由结构检查和行为测试共同支撑，不能只检查某一个源文件字符串。
- `unsupported_part_type`、`missing_required_parameter`、`invalid_parameter_value` 必须有未调用 Worker 的行为测试；否则不能降级为后续 Improvement。

### 可以在 V1.8 完成后继续跟踪的 Improvement

- `flange_basic` 真实路径可在独立 smoke 中验证 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4` 及螺栓孔布置；未完成该 evidence 时保持 dry-run-only。
- `shaft_basic` 真实路径需要 `CreateLine`、`CreateCenterLine`、`FeatureRevolve2` 的专用诊断 Runner，包括台阶截面、中心线和旋转返回值证据。
- 发布包、工程图名称和跨阶段 provenance 后续可进一步通用化，但必须保持 V1.7 `plate_basic_4holes` 真实可交付语义不回退。
- COM 会话复用、动态路由选择策略和更丰富的零件族工程图仍需要独立设计与回归测试。

### 验证步骤、常见失败和禁止事项

完成实现后运行 build、test 和默认 self-check，确认 plate 回归、flange / shaft dry-run、Registry 和前置拒绝均通过。若法兰或轴的真实 API 证据不足，正确处理是保留 dry-run-only 并记录 evidence 缺口，不是盲改主 Worker。

禁止借技术债越界实现装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9；本轮完成后就停在 V1.8 审查门。

## V1.9 Phase 1 Improvements Backlog

### 目标与适用范围

本节只记录不阻塞 V1.9 Phase 1 build-only 合同的改进项。输入是当前实现、报告、测试和审查结论，输出是后续可单独设计的 backlog，不得用于放宽本轮产物、质量门禁或授权要求。

### 不阻塞改进项

- 用源码文本搜索支撑“无大型 `switch(part_type)`”仍是辅助证据；后续可增加更精确的结构化分析，但 V1.9 必须已有 Registry 行为测试。
- 内部默认路由已由 `InternalWorkflowRoute` 注入；后续零件或业务域扩展只应提供新的受校验路由定义，不在 Orchestrator 内新增硬编码数组。
- `HumanApproval` 已具备单进程宿主内的显式提交/恢复闭环，但不代替 V2.0 的统一执行策略和 QualityGate。跨进程或跨重启审批恢复仍需部署方注入持久化 `IWorkflowApprovalStore`。
- 轴的偏移多段拉伸作为备选方案记入 backlog。V1.9 Phase 1 固定使用闭合轮廓、中心线和 `FeatureRevolve2` 360° 旋转，不混用偏移拉伸。

### 执行、验证和失败边界

执行 V1.9 时先确认所有阻断性合同已完成，再把上述事项保留为 Improvements。若当前实现仍使用 Builder 直调作为最终验收、缺少 ArtifactValidator / Reviewer / QualityGate、或无法产生 build-only 发布包，则不得降级为 backlog。

禁止借 backlog 扩大 flange / shaft 工程图、并发 COM、跳过质量门禁或进入 V2.0。

## V1.9 Phase 2 验收后 Improvements

flange 与 shaft diagnostic、视觉审查、非空产物和最终主工作流程均已通过，因此下列增强不阻断 V1.9 基础零件族验收：

- diagnostic 的 `geometry_body_count_status` 仍为 `NotVerified`；后续可在稳定 COM 边界内增加实体数量读取与报告。
- diagnostic 的 `theoretical_volume_status` 仍为 `NotVerified`；后续可根据参数计算理论体积，并与 SolidWorks 质量属性结果做带公差比较。
- 自动几何增强必须保留现有特征树、四视图和主工作流程证据，不能用新增字段替代人工视觉复核或 QualityGate。

瞬时源哈希读锁的恢复规则已经完成，不是未解决债务：只有复制和目标校验成功时才降为 warning；目标缺失、为空或校验失败仍必须阻断。上述 Improvements 不授权 flange / shaft 工程图，也不进入 V2.0。
