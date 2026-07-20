# CADModeling Module

## 目标与适用范围

本模块负责把已接受的结构化设计意图转换为 BuildPlan，并规划受控 CAD Worker 执行。V1.8 通过通用 `CADModelSpec` 和 `PartTypeRegistry` 支持 `plate_basic_4holes`、`flange_basic`、`shaft_basic` 三个零件族。

`plate_basic_4holes` 保持已有真实 SolidWorks 主工作流程能力；`flange_basic` 和 `shaft_basic` 本轮只承诺 Schema、Validator、BuildPlan、Builder 与 dry-run，不声称真实验收已完成。

## 输入与输出

输入 `CADModelSpec` 包含 `part_type`、`dimensions`、`features`、`material`、`output_requirements`、`drawing_requirements` 和 `execution_options`。输出包括已校验 BuildPlan、Worker request、ArtifactValidator 结果、Reviewer 结果、QualityGate 裁决和发布包上下文。

## 执行步骤

```text
结构化输入
→ CADModelSpec
→ PartTypeRegistry
→ 参数 Validator
→ BuildPlan
→ Worker
  → PartFamilyBuilderRegistry
  → 对应 PartFamilyBuilder
→ ArtifactValidator
→ Drawing
→ QualityGate
→ ReleasePackage
```

- Agent 创建计划和协作建议，不直接调用 Worker。
- Skill 生成结构化 BuildPlan，不执行 CAD API。
- Worker 负责 dry-run 或受控 SolidWorks 执行。
- 每个零件族将 Schema、Validator、BuildPlan 生成、Builder、`failure_stage`、API evidence 与测试封装在独立定义中。

## 验证标准与常见失败

未注册类型返回 `unsupported_part_type`；缺少和非法参数返回 `missing_required_parameter` 或 `invalid_parameter_value`，且必须在 Worker 之前停止。验证至少覆盖 plate 回归、flange dry-run、shaft dry-run、三族注册和未调用 Worker 的负路径。失败详见 `failure_repair.md`，API 证据边界详见 `api_evidence.md`。

## 禁止事项

- 不使用大型 `switch(part_type)` 或分散 plate 特判代替 Registry。
- Agent、Gateway 和 LLM 永远不直接调用 CAD API、SDK、COM 或 Worker。
- 默认 self-check 不启动 SolidWorks。
- 本轮不做装配体、BOM、复杂轴特征、键槽、螺纹、法兰密封面、批量任务队列或 V1.9。
