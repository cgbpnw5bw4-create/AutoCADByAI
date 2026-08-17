# V2.1-A 圆筒夹套参数化零件族

## 目标与边界

V2.1-A 新增独立零件族 `jacket_basic`，用于自动生成直筒圆柱夹套。当前版本只覆盖同轴直筒夹套，不包含封头、接管、膨胀节、支座、加强圈、焊缝或装配关系。

默认示例参数为：外径 140 mm、内径 120 mm、轴向长度 180 mm、材料 Q235。参数必须为有限正数，内径必须小于外径，单边壁厚 `(outer_diameter_mm - inner_diameter_mm) / 2` 不得小于 1 mm。

## 架构链路

夹套必须沿主架构执行：

`CADModelSpec -> PartTypeRegistry -> JacketBasicValidator -> FeatureGraph -> BuildPlanCompiler -> PartFamilyBuilderRegistry -> RealSolidWorksWorker -> ArtifactValidator -> Reviewer -> QualityGate -> ReleasePackage`

`FeatureGraph` 固定为 TopPlane 外圆草图、盲拉伸、TopPlane 内圆草图、深度为两倍轴向长度的盲切四个操作；特征标识为 `jacket_body_extrude` 和 `jacket_inner_cut`。不得从 CLI 直接调用 Builder，也不得以文件存在、COM 返回非空或独立 diagnostic 代替主流程验收。

## SolidWorks API 证据

本零件族不引入新的 COM API。Builder 复用 `CreateCircle`、`FeatureExtrusion2`、`FeatureCut4` 与 `RealSolidWorksGeometryReader`：外圆以盲拉伸形成圆柱体，内圆以两倍轴向长度的盲切形成夹套内腔；随后必须实测实体数量、外包络、内外圆柱面直径和体积。

现有 V1.9 自由文本与历史产物不能构成 V2.1-A 的结构化生产授权。夹套真实执行因此保持连接前失败关闭，直到受控目录中具备绑定源码修订、SolidWorks 运行时版本、真实 diagnostic、几何测量和有效 STEP 内容的证据。API 调用失败分别报告 `jacket_profile_create_failed`、`jacket_extrude_failed`、`jacket_inner_cut_failed`；重建、几何和 STEP 内容失败使用对应的可行动阶段并写入失败构建报告。

## 自检与验收

平台自检增加以下字段：

- `jacket_part_family_registered`
- `jacket_uses_generic_feature_graph`
- `jacket_real_builder_implemented`
- `jacket_dry_run_passed`
- `jacket_real_workflow_supported`
- `jacket_api_evidence_documented`
- `jacket_production_evidence_active`
- `v2_1_a_jacket_documented`

自检还会行为式验证 V2.0-C 四类生产 Handler 的证据链，并输出 `feature_production_evidence_active`。任一受绑定 diagnostic 缺失、源码修订过期或运行时证据不完整时，平台自检必须为 `Failed`。

真实验收命令：

```powershell
dotnet run --project src/Interfaces/CliHost -- run-cad-workflow --input examples/real_cad_jacket_request.json
```

只有同一次主流程的 SLDPRT、符合 ISO 10303-21 物理内容的 STEP、构建报告、实测几何、Artifact Validator、Reviewer 与 QualityGate 全部通过，发布包状态为 `Deliverable`，才可判定夹套建模成功。2026-08-17 审查产物的 `.STEP` 内容不符合该格式，且结构化生产证据尚未恢复，因此当前不得认定 V2.1-A 真实验收通过。
