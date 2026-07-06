# 技术债记录

## V1.1 Claude Improvements

V1.1 Claude 审查未发现 Blockers，仅提出 Improvements。以下事项进入技术债，不阻塞 V1.2 主线：

- 为 `ExecuteWithApplicationAsync` 后续补充更明确的真实执行超时边界。
- 清理历史切孔候选中的死代码，降低后续 API 修复噪声。
- 统一 `repair_log` 命名，避免报告字段语义分散。
- 清理或归档旧 evidence JSON，保留当前阶段可复现证据。
- 复查 V1.1 工程图报告写入路径，消除重复写入。
- 后续抽取保存、导出、COM 释放等 SolidWorks 共享工具。

处理规则：以上事项不得作为 V1.2 Blocker。若后续阶段触碰同一代码路径，应优先就近修复，并继续保持真实 SolidWorks 默认关闭。

## V1.2 Claude Improvements

V1.2 Claude 审查结论为 PASS WITH COMMENTS，未发现 Blockers，仅提出 Improvements。以下事项进入技术债，不阻塞 V1.3 主线：

- `ExecuteWithApplicationAsync` 真实执行阶段仍缺少强制超时边界；V1.3 继续保持真实执行默认关闭，后续应在会话管理层补超时护栏。
- V1.2 尺寸标注使用非关联、硬编码的最小尺寸策略；后续阶段若扩展语义标注，应在报告中继续暴露非关联边界并避免虚假成功。
- V1.2 尺寸 Builder 的内部失败分支仍缺少更细的纯单元测试；后续补测试时优先覆盖 `source_drawing_missing`、视图缺失和保存/导出失败路径。
- 后续抽取工程图保存、PDF 导出、报告写入和 COM 释放的共享工具，减少 V1.1/V1.2/V1.3 间重复实现。

处理规则：以上事项不得作为 V1.3 Blocker。本阶段仅实现标题栏/图纸属性最小链路，不扩大到 BOM、复杂国标模板、公差系统或 V1.4。
## V1.3 Claude Improvements

V1.3 Claude 审查结论为 PASS WITH COMMENTS，未发现 Blockers。以下事项作为后续技术债登记，不阻塞当前最小标题栏链路：
- 标题栏 `Passed` 目前表示自定义属性已写入并可回读，不表示图纸模板 Sheet Format 中的 note 已可见渲染。报告和 self-check 必须持续暴露 `title_block_population_strategy=custom_properties_only` 与 `title_block_fields_verified_in_sheet_format=false`，直到后续版本实现模板 note 链接校验。
- 标题栏、尺寸、工程图 Builder 的 COM 调用层仍应后续抽象为可注入接口，以便纯单元测试覆盖属性回读不一致、保存空文件、PDF 导出空文件和 COM 返回 null 等内部失败分支。
- 旧工程图 report 双写、历史 evidence JSON 清理、共享保存/导出/COM 释放工具抽取继续按“触碰即就近修复”的规则处理。
处理规则：`ExecuteWithApplicationAsync` 执行超时已在本轮从技术债提升为修复项；其余事项不得被误读为 V1.4 Blocker。真实 SolidWorks 默认仍保持关闭，只有显式 smoke 开关才允许启动 COM/CAD Runtime。
