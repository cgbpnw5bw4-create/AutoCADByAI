# 技术债记录

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
