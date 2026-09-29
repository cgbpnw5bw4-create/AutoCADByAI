using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker.Features.Pattern;

namespace SolidWorksWorker.Features;

/// <summary>
/// The exclusive COM boundary for generic feature handlers. Handlers pass only
/// domain definitions and never receive ModelDoc2, FeatureManager, SketchManager,
/// ISolidWorksComFacade, dynamic objects, or raw feature RCWs.
/// </summary>
public sealed class RealSolidWorksFeatureAdapter : ISolidWorksFeatureAdapter
{
    public const string AdapterIdentifier = "solidworks.real-feature-adapter";
    public const string CurrentAdapterVersion = "2.0-c.2";

    // 取自本机 SDK 反射（SOLIDWORKS 2023 swconst）：
    // swFeatureFilletOptions_e.swFeatureFilletUniformRadius = 2
    // swFeatureFilletType_e.swFeatureFilletType_Simple = 0
    private const int SwFeatureFilletUniformRadius = 2;
    private const int SwFeatureFilletTypeSimple = 0;

    // 同一来源：swChamferType_e.swChamferAngleDistance = 1。
    // Options 是 swFeatureChamferOption_e 位标志；本档案不授权 flip(1)、
    // keep-feature(2)、切线延伸(4) 与 propagate-to-parts(8)，因此取 0。
    private const int SwChamferAngleDistance = 1;
    private const int SwFeatureChamferNoOptions = 0;

    // 阵列与镜像的选择集标记。
    //
    // 这三个 API 与圆角、倒角一样不收几何引用参数，但它们要区分"哪个是种子、
    // 哪个是方向"，所以选择时必须打标记。标记值不是从记忆里写的：本项目先试过
    // IFeatureManager.CreateDefinition + 显式引用属性那条路，实测
    // ILinearPatternFeatureData.AccessSelections 对尚未归属特征的定义对象返回
    // false，写进去的 D1Axis 读回来是 null——那条路在"新建"场景走不通。
    // 因此退回选择集路线，并在创建之后立刻回读特征自身的定义对象校验引用，
    // 把标记值从"猜测"变成"每次执行都验证一遍的事实"。
    private const int SeedFeatureSelectionMark = 4;
    private const int DirectionSelectionMark = 1;
    private const int MirrorPlaneSelectionMark = 2;
    private const int MirrorTargetSelectionMark = 1;

    // IFeature.GetTypeName2 对这三类特征的返回值，实测自本机 SOLIDWORKS 2023。
    private const string SwLinearPatternTypeName = "LPattern";
    private const string SwCircularPatternTypeName = "CirPattern";
    private const string SwMirrorPatternTypeName = "MirrorPattern";

    private const double MmToMeters = 0.001d;
    private static readonly double OneDegreeInRadians = Math.PI / 180d;

    private static readonly IReadOnlyDictionary<string, string[]> PlaneAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["FrontPlane"] = ["Front Plane", "FrontPlane", "前视基准面"],
            ["TopPlane"] = ["Top Plane", "TopPlane", "上视基准面"],
            ["RightPlane"] = ["Right Plane", "RightPlane", "右视基准面"]
        };

    private readonly object _model;
    private readonly ISolidWorksComFacade _com;
    private readonly Dictionary<string, SketchComArtifact> _sketches =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _axisConfirmations = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>已创建特征的 COM 句柄，按 feature_id 索引。阵列与镜像的种子由此取回。</summary>
    private readonly Dictionary<string, object> _features = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<object> _ownedReferences = [];
    private readonly HashSet<object> _ownedReferenceSet =
        new(ReferenceEqualityComparer.Instance);
    private bool _disposed;

    public RealSolidWorksFeatureAdapter(object model, ISolidWorksComFacade com)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _com = com ?? throw new ArgumentNullException(nameof(com));
    }

    public string AdapterId => AdapterIdentifier;

    public string AdapterVersion => CurrentAdapterVersion;

    public Task<FeatureHandlerExecutionResult> ExecuteSketchAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.SketchExecutionFailed,
            () => CreateSketch(feature, operation)));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteExtrudeBossAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.ExtrudeExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                SelectDependencySketch(operation);
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.ExtrudeExecutionFailed, "FeatureManager is unavailable.");
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "FeatureExtrusion2",
                    [
                        true, false, false, 0, 0, depth * MmToMeters, 0d,
                        false, false, false, false, 0d, 0d, false, false,
                        false, false, true, true, true, 0, 0d, false
                    ]));
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Increase);
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteRevolveBossAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(PartFamilyFailureStages.ShaftRevolveFailed, () =>
        {
            var validation = new Revolve.RevolveBossHandler().Validate(feature);
            if (!validation.IsValid) throw Stage(validation.FailureStage!, string.Join("; ", validation.Issues));
            var sketchId = operation.Parameters.GetValueOrDefault("sketch_id");
            if (sketchId is null || !feature.ReferencedSketches[0].Equals(sketchId, StringComparison.OrdinalIgnoreCase) ||
                !_sketches.TryGetValue(sketchId, out var sketch) || sketch.Axis is null ||
                sketch.ReferencePlane != "RightPlane" || operation.SketchPlane != "RightPlane" ||
                !operation.DependsOn.Contains(sketch.OperationId, StringComparer.OrdinalIgnoreCase) ||
                !_axisConfirmations.TryGetValue(sketch.OperationId, out var axisOperation) ||
                !operation.DependsOn.Contains(axisOperation, StringComparer.OrdinalIgnoreCase) ||
                operation.DependsOn.Any(dependency => _sketches.TryGetValue(dependency, out var other) && !ReferenceEquals(other, sketch)))
                throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "旋转必须直接依赖唯一已校验闭合草图及其构造轴确认步骤。");
            if (_com.TryGetProperty(sketch.Axis, "ConstructionGeometry") is not true)
                throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "构造轴读回不是 ConstructionGeometry=true。");
            var volumeBefore = MeasureSolidVolume();
            if (volumeBefore != 0d)
                throw Stage(PartFamilyFailureStages.InvalidFeatureParameter, "本档案只支持首次建立单一旋转实体。");
            SelectSketch(sketch);
            var selectionManager = Own(_com.TryGetProperty(_model, "SelectionManager"))
                ?? throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "缺少 SelectionManager。");
            var selectData = Own(_com.TryInvoke(selectionManager, "CreateSelectData"))
                ?? throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "轴选择数据创建失败。");
            if (!_com.TrySetProperty(selectData, "Mark", 16) ||
                _com.TryGetProperty(selectData, "Mark") is not int mark || mark != 16 ||
                !_com.TryInvokeBool(sketch.Axis, "Select4", true, selectData) ||
                _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", 0) is not int profileCount || profileCount != 1 ||
                _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", 16) is not int axisCount || axisCount != 1 ||
                _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", -1) is not int selectedCount || selectedCount != 2)
                throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "必须选中一个 mark 0 草图和一个 mark 16 构造轴。");
            var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                ?? throw Stage(PartFamilyFailureStages.ShaftRevolveFailed, "缺少 FeatureManager。");
            object?[] arguments =
            [
                true, true, false, false, false, false, 0, 0,
                2d * Math.PI, 0d, false, false, 0.01d, 0.01d, 0, 0d, 0d, true, true, true
            ];
            var resultObject = Own(_com.InvokeWithArgs(manager, "FeatureRevolve2", arguments));
            VerifyCreatedFeatureKind(resultObject, "Revolution", "旋转实体");
            var result = ValidateFeatureResult(feature, operation, resultObject, volumeBefore, VolumeChangeExpectation.Increase);
            var definition = Own(_com.TryInvoke(resultObject, "GetDefinition"));
            var actualAngle = _com.TryInvoke(definition, "GetRevolutionAngle", true);
            if (actualAngle is not double angle || !double.IsFinite(angle) || Math.Abs(angle - 2d * Math.PI) > 1e-9 ||
                _com.TryInvoke(definition, "IsBossFeature") is not true || _com.TryInvoke(definition, "IsThinFeature") is not false)
                throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "旋转读回必须为完整 360° 非薄壁实体凸台。");
            var bodies = _com.TryInvoke(_model, "GetBodies2", 0, false);
            OwnReturned(bodies);
            if (bodies is not Array bodyArray || bodyArray.Length != 1)
                throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "旋转结果必须恰好为一个实体。");
            return result with { Logs = result.Logs.Concat(
            [
                "revolve_selection:profile_IFeature.Select2_mark_0;axis_ISketchSegment.Select4_mark_16",
                "revolve_axis_construction_geometry:true",
                $"revolve_api_arguments:{JsonSerializer.Serialize(arguments)}",
                $"revolve_angle_readback_radians:{angle:R};feature_type:Revolution;solid_body_count:1"
            ]).ToArray() };
        }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteExtrudeCutAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.CutExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                var sketchManager = ActivateSketchForFeatureCut(FindDependencySketch(operation));
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.CutExecutionFailed, "FeatureManager is unavailable.");
                try
                {
                    var featureObject = Own(_com.InvokeWithArgs(
                        manager,
                        "FeatureCut4",
                        BlindCutArguments(depth * MmToMeters)));
                    return ValidateFeatureResult(
                        feature,
                        operation,
                        featureObject,
                        volumeBefore,
                        VolumeChangeExpectation.Decrease);
                }
                finally
                {
                    CloseActiveSketchIfNeeded(sketchManager);
                }
            }));
    }

    // V2.1-A 复杂特征执行入口。
    //
    // 这五个 API 目前没有本项目的真实执行证据，因此 Adapter 在这一层显式返回
    // feature_api_unverified，而不是写一段从未运行过的 COM 调用。理由：
    public Task<FeatureHandlerExecutionResult> ExecuteFilletAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.FeatureResultInvalid,
            () =>
            {
                var radius = RequiredPositive(feature.Parameters, "radius_mm");
                feature.Parameters.TryGetValue("edge_selection", out var criteriaJson);
                if (!EdgeSelectionCriteriaParser.TryParse(criteriaJson, out var criteria, out var criteriaIssue) ||
                    criteria is null)
                {
                    throw Stage(
                        PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                        criteriaIssue ?? "edge_selection criteria are unusable.");
                }

                SelectEdgesByCriteria(criteria, PartFamilyFailureStages.FeatureResultInvalid);

                var volumeBefore = MeasureSolidVolume();
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "FeatureManager is unavailable.");

                // FeatureFillet3 的 14 个参数取自本机 SDK 反射，不含任何几何引用：
                // 目标边完全来自上面 Select4 建立的选择集。
                // Options = swFeatureFilletUniformRadius(2)，Ftyp = swFeatureFilletType_Simple(0)。
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "FeatureFillet3",
                    [
                        SwFeatureFilletUniformRadius,
                        radius * MmToMeters,
                        0d,
                        0d,
                        SwFeatureFilletTypeSimple,
                        0,
                        0,
                        null, null, null, null, null, null, null
                    ]));

                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Changed);
            }));
    }

    /// <summary>
    /// 按声明式判据选中边。
    /// <para>
    /// 这是整条链路的安全关键点：SolidWorks 的圆角、倒角、阵列、镜像都只作用于
    /// 当前选择集，选错边时 API 依然返回非空 IFeature，产出的是特征打在错误位置
    /// 的零件。因此判据未唯一命中时必须直接失败，绝不"取第一条"。
    /// </para>
    /// </summary>
    /// <summary>
    /// 按判据求解边，返回实测数据与 COM 句柄的配对。
    /// <para>
    /// 求解与 fail-closed 判定集中在这里，供"选中它"（圆角、倒角）和
    /// "取它的方向"（线性阵列、圆周阵列）两类用法共用。安全关键逻辑只写一份，
    /// 否则新增一种用法就多一次漏掉 ExpectedCount 闸的机会。
    /// </para>
    /// </summary>
    private (IReadOnlyList<EnumeratedEdge> Enumerated, EdgeSelectionResult Resolution) ResolveEdges(
        EdgeSelectionCriteria criteria)
    {
        var enumerated = SolidWorksEdgeEnumerator.EnumerateModel(_com, _model);
        if (enumerated.Count == 0)
        {
            throw Stage(PartFamilyFailureStages.EdgeSelectionNotFound, "the model exposed no solid edges to select.");
        }

        var resolution = EdgeSelectionResolver.Resolve(
            enumerated.Select(edge => edge.Measured).ToArray(),
            criteria);
        if (!resolution.IsResolved)
        {
            throw Stage(
                resolution.FailureStage ?? PartFamilyFailureStages.EdgeSelectionNotFound,
                resolution.Issues.FirstOrDefault() ?? "edge selection could not be resolved.");
        }

        return (enumerated, resolution);
    }

    private void SelectEdgesByCriteria(EdgeSelectionCriteria criteria, string stage)
    {
        var (enumerated, resolution) = ResolveEdges(criteria);

        _com.TryInvoke(_model, "ClearSelection2", true);
        var selectionManager = Own(_com.TryGetProperty(_model, "SelectionManager"))
            ?? throw Stage(stage, "SelectionManager is unavailable.");
        var selectData = Own(_com.TryInvoke(selectionManager, "CreateSelectData"))
            ?? throw Stage(stage, "CreateSelectData returned null for the edge selection.");

        foreach (var match in resolution.Matches)
        {
            var comEdge = enumerated[match.Index].ComEdge;
            if (!_com.TryInvokeBool(comEdge, "Select4", true, selectData))
            {
                throw Stage(stage, $"IEntity.Select4 failed for resolved edge index {match.Index}.");
            }
        }

        var selectedCount = _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", -1);
        if (selectedCount is int count && count != resolution.Matches.Count)
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                $"selection set holds {count} objects but the criteria resolved {resolution.Matches.Count}.");
        }
    }

    public Task<FeatureHandlerExecutionResult> ExecuteChamferAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.FeatureResultInvalid,
            () =>
            {
                var distance = RequiredPositive(feature.Parameters, "distance_mm");
                var angle = RequiredPositive(feature.Parameters, "angle_deg");
                if (angle >= 90d)
                {
                    throw Stage(
                        PartFamilyFailureStages.InvalidFeatureParameter,
                        $"angle_deg={angle} is outside the authorized open interval (0, 90).");
                }

                feature.Parameters.TryGetValue("edge_selection", out var criteriaJson);
                if (!EdgeSelectionCriteriaParser.TryParse(criteriaJson, out var criteria, out var criteriaIssue) ||
                    criteria is null)
                {
                    throw Stage(
                        PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                        criteriaIssue ?? "edge_selection criteria are unusable.");
                }

                SelectEdgesByCriteria(criteria, PartFamilyFailureStages.FeatureResultInvalid);

                var volumeBefore = MeasureSolidVolume();
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "FeatureManager is unavailable.");

                // InsertFeatureChamfer 的 8 个参数取自本机 SDK 反射，同样不含几何引用：
                // 目标边完全来自上面 Select4 建立的选择集。角度按 SolidWorks 约定取弧度。
                // VertexChamDist1/2/3 只对 swChamferVertex 有意义，此档案传 0。
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "InsertFeatureChamfer",
                    [
                        SwFeatureChamferNoOptions,
                        SwChamferAngleDistance,
                        distance * MmToMeters,
                        angle * OneDegreeInRadians,
                        0d,
                        0d,
                        0d,
                        0d
                    ]));

                // 与圆角同理：倒角是削料还是补料取决于边的凹凸性，写死方向会在
                // 凹边上产生假失败，因此只要求体积发生可测变化。
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Changed);
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteLinearPatternAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.FeatureResultInvalid,
            () =>
            {
                var instances = RequiredInstanceCount(feature);
                var spacing = RequiredPositive(feature.Parameters, "spacing_mm");
                var seed = ResolvePatternSeed(feature);
                var directionEdge = ResolveAxisEdge(feature, "direction_selection", "direction", EdgeKinds.Line);

                var volumeBefore = MeasureSolidVolume();
                var manager = RequireFeatureManager();

                ClearSelection();
                SelectFeatures([seed], SeedFeatureSelectionMark, "linear pattern seed");
                SelectEntity(directionEdge.Edge, DirectionSelectionMark, "linear pattern direction");

                // FeatureLinearPattern4 的 20 个参数取自本机 SDK 反射。
                // 只用第一方向：Num2=1、Spacing2=0 且 CtrlByNum2=true 表示第二方向未启用。
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "FeatureLinearPattern4",
                    [
                        instances, spacing * MmToMeters, 1, 0d,
                        directionEdge.FlipDirection, false, string.Empty, string.Empty,
                        false, false, false, false,
                        true, true, false, false,
                        false, false, 0d, 0d
                    ]));

                VerifyCreatedFeatureKind(featureObject, SwLinearPatternTypeName, "linear pattern");
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Changed);
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteCircularPatternAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.FeatureResultInvalid,
            () =>
            {
                var instances = RequiredInstanceCount(feature);
                var angle = RequiredPositive(feature.Parameters, "angle_deg");
                if (angle > 360d)
                {
                    throw Stage(
                        PartFamilyFailureStages.InvalidFeatureParameter,
                        $"angle_deg={angle} exceeds the authorized 360 degree envelope.");
                }

                var seed = ResolvePatternSeed(feature);
                var axisEdge = ResolveAxisEdge(feature, "axis_selection", "axis", EdgeKinds.Circle);

                var volumeBefore = MeasureSolidVolume();
                var manager = RequireFeatureManager();

                ClearSelection();
                SelectFeatures([seed], SeedFeatureSelectionMark, "circular pattern seed");
                SelectEntity(axisEdge.Edge, DirectionSelectionMark, "circular pattern axis");

                // FeatureCircularPattern5 的 14 个参数取自本机 SDK 反射。
                // EqualSpacing=true 时 Spacing 是总角度，实例在其上等分。
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "FeatureCircularPattern5",
                    [
                        instances, angle * OneDegreeInRadians, axisEdge.FlipDirection, string.Empty,
                        false, true, false, false,
                        false, false, 1, 0d, string.Empty, false
                    ]));

                VerifyCreatedFeatureKind(featureObject, SwCircularPatternTypeName, "circular pattern");
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Changed);
            }));
    }

    public Task<FeatureHandlerExecutionResult> ExecuteMirrorAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Guard(
            PartFamilyFailureStages.FeatureResultInvalid,
            () =>
            {
                feature.Parameters.TryGetValue("mirror_plane", out var planeName);
                if (string.IsNullOrWhiteSpace(planeName))
                {
                    throw Stage(PartFamilyFailureStages.InvalidFeatureParameter, "mirror_plane is required.");
                }

                var plane = ResolveStandardPlane(planeName);
                var targets = ResolveSeedFeatures(feature, "target_features");

                var volumeBefore = MeasureSolidVolume();
                var manager = RequireFeatureManager();

                ClearSelection();
                SelectFeatures([plane], MirrorPlaneSelectionMark, "mirror plane");
                SelectFeatures(targets, MirrorTargetSelectionMark, "mirror target");

                // InsertMirrorFeature2 的 5 个参数取自本机 SDK 反射：
                // BMirrorBody=false 表示镜像特征而非实体；BMerge=true 保持单实体。
                var featureObject = Own(_com.InvokeWithArgs(
                    manager,
                    "InsertMirrorFeature2",
                    [false, false, true, false, 0]));

                VerifyCreatedFeatureKind(featureObject, SwMirrorPatternTypeName, "mirror");
                return ValidateFeatureResult(
                    feature,
                    operation,
                    featureObject,
                    volumeBefore,
                    VolumeChangeExpectation.Changed);
            }));
    }

    /// <summary>
    /// 解出一条边并交叉校验它的方向。
    /// <para>
    /// 判据负责"选哪条"，声明的主轴负责"应该指向哪儿"。两者必须互相印证：
    /// 判据可能命中一条完全合法、但不是设计者想要的边，而阵列方向错一点，
    /// 整排实例的位置就全错——错得还很像成功。
    /// </para>
    /// </summary>
    private (object Edge, bool FlipDirection) ResolveAxisEdge(
        FeatureDefinition feature,
        string criteriaParameter,
        string axisParameter,
        string expectedKind)
    {
        feature.Parameters.TryGetValue(criteriaParameter, out var criteriaJson);
        if (!EdgeSelectionCriteriaParser.TryParse(criteriaJson, out var criteria, out var criteriaIssue) ||
            criteria is null)
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                criteriaIssue ?? $"{criteriaParameter} criteria are unusable.");
        }

        if (criteria.ExpectedCount != 1)
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                $"{criteriaParameter} must declare expected_count=1; a direction or axis comes from one edge only.");
        }

        feature.Parameters.TryGetValue(axisParameter, out var declaredAxis);
        if (!PrincipalAxisRules.TryParse(declaredAxis, out var declared))
        {
            throw Stage(PartFamilyFailureStages.InvalidFeatureParameter,
                $"{axisParameter} 必须为 x/y/z 或带 +、- 符号的主轴方向。");
        }
        if (expectedKind == EdgeKinds.Circle && !PrincipalAxisRules.HasExplicitOriginAxisPosition(criteria, declared))
        {
            throw Stage(PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                "圆周轴判据必须显式约束主轴横向两坐标为 0，不能只校验平行。");
        }

        var (enumerated, resolution) = ResolveEdges(criteria);
        var match = resolution.Matches[0];
        if (!match.Kind.Equals(expectedKind, StringComparison.OrdinalIgnoreCase))
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                $"{criteriaParameter} resolved a {match.Kind} edge but {expectedKind} is required.");
        }

        if (!PrincipalAxisRules.TryResolveFlip(match.Direction, declaredAxis, out var flipDirection))
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                $"{criteriaParameter} 的实测方向无效或不平行于 {axisParameter}={declaredAxis}，已拒绝猜测方向。");
        }

        if (expectedKind == EdgeKinds.Circle && !PrincipalAxisRules.IsOnOriginAxis(match, declared, criteria.ToleranceMm))
        {
            throw Stage(
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                $"{criteriaParameter} 实测圆心不在声明的过原点主轴上，平行轴不能作为同轴证据。");
        }

        return (enumerated[match.Index].ComEdge, flipDirection);
    }

    // 阵列只允许一个显式绑定的前序种子；镜像保留独立的多目标解析。
    private object ResolvePatternSeed(FeatureDefinition feature)
    {
        var validation = PatternParameterRules.ValidateSingleSeed(feature, nameof(RealSolidWorksFeatureAdapter));
        if (validation is not null)
        {
            throw Stage(PartFamilyFailureStages.InvalidFeatureParameter, string.Join(" ", validation.Issues));
        }

        var seed = feature.Parameters["seed_feature"];
        return _features.TryGetValue(seed, out var created)
            ? created
            : throw Stage(PartFamilyFailureStages.FeatureResultInvalid,
                $"seed_feature={seed} 尚未由当前 Adapter 创建，不能用于阵列。");
    }

    /// <summary>
    /// 按 feature_id 取回执行链里已创建的特征对象。
    /// <para>
    /// 取不到必须失败而不是跳过：一个没有种子的阵列在 SolidWorks 里可能照样
    /// 返回非空对象，产出的却是"什么都没阵列"的零件。
    /// </para>
    /// </summary>
    private object[] ResolveSeedFeatures(
        FeatureDefinition feature,
        string parameterName)
    {
        feature.Parameters.TryGetValue(parameterName, out var text);
        var ids = (text ?? string.Empty)
            .Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0)
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{parameterName} must reference at least one previously created feature.");
        }

        var resolved = new List<object>(ids.Length);
        foreach (var id in ids)
        {
            if (!_features.TryGetValue(id, out var created))
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    $"{parameterName} references {id}, which this adapter never created.");
            }

            resolved.Add(created);
        }

        return [.. resolved];
    }

    private static int RequiredInstanceCount(FeatureDefinition feature)
    {
        if (!feature.Parameters.TryGetValue("instance_count", out var text) ||
            !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ||
            count < 2)
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                "instance_count must be an integer of at least 2.");
        }

        return count;
    }

    private object RequireFeatureManager() =>
        Own(_com.TryGetProperty(_model, "FeatureManager"))
        ?? throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "FeatureManager is unavailable.");

    /// <summary>
    /// 按标记把若干特征加入选择集，并逐个确认加入成功。
    /// </summary>
    private void SelectFeatures(object[] features, int mark, string what)
    {
        foreach (var item in features)
        {
            if (!_com.TryInvokeBool(item, "Select2", true, mark))
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    $"IFeature.Select2 failed for a {what} with mark {mark}.");
            }
        }
    }

    /// <summary>
    /// 按标记把一个实体（边、面）加入选择集。
    /// </summary>
    private void SelectEntity(object entity, int mark, string what)
    {
        var selectionManager = Own(_com.TryGetProperty(_model, "SelectionManager"))
            ?? throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "SelectionManager is unavailable.");
        var selectData = Own(_com.TryInvoke(selectionManager, "CreateSelectData"))
            ?? throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"CreateSelectData returned null for the {what} selection.");

        if (!_com.TrySetProperty(selectData, "Mark", mark) ||
            !_com.TryInvokeBool(entity, "Select4", true, selectData))
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"IEntity.Select4 failed for the {what} with mark {mark}.");
        }
    }

    /// <summary>
    /// 创建之后确认 SolidWorks 真的建出了我们要的那类特征。
    /// <para>
    /// 标记值决定了 SolidWorks 把哪个选中项当成种子、哪个当成方向或基准面。
    /// 标记错了，这些 API 未必报错——可能建出别的东西，或者什么都没阵列。
    /// GetTypeName2 是能在执行期廉价拿到的最强判据：它由 SolidWorks 自己给出，
    /// 只有选择集被正确解读时才会是预期的那一类。
    /// </para>
    /// <para>
    /// 曾尝试回读 IXxxPatternFeatureData 上的 D1Axis / Axis / Plane 做更强的引用校验，
    /// 实测本机后期绑定下 AccessSelections 一律被拒（对新建定义对象和已归属特征都是），
    /// 因此改为"特征类别 + 几何变化"这一对可稳定获得的判据；实例落点的正确性由
    /// 采证时的只读拓扑探针独立复核，并由源码 revision 绑定锁住。
    /// </para>
    /// </summary>
    private void VerifyCreatedFeatureKind(object? featureObject, string expectedTypeName, string what)
    {
        if (featureObject is null)
        {
            return;
        }

        var typeName = _com.TryInvoke(featureObject, "GetTypeName2")?.ToString();
        if (!string.Equals(typeName, expectedTypeName, StringComparison.OrdinalIgnoreCase))
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"the created {what} reports feature type {typeName ?? "unknown"} " +
                $"but {expectedTypeName} was expected; the selection marks did not bind what was intended.");
        }
    }

    /// <summary>
    /// 按标准名解析基准面并返回其对象。基准面名称是稳定的，
    /// 不属于会漂移的自动生成名，因此这里按名查找是安全的。
    /// </summary>
    private object ResolveStandardPlane(string requestedPlane)
    {
        if (!PlaneAliases.TryGetValue(requestedPlane, out var aliases))
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{requestedPlane} is not an authorized standard plane.");
        }

        var current = Own(_com.TryInvoke(_model, "FirstFeature"));
        while (current is not null)
        {
            var name = _com.TryGetProperty(current, "Name")?.ToString();
            var typeName = _com.TryInvoke(current, "GetTypeName2")?.ToString();
            if (aliases.Contains(name ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                string.Equals(typeName, "RefPlane", StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            current = Own(_com.TryInvoke(current, "GetNextFeature"));
        }

        throw Stage(
            PartFamilyFailureStages.FeatureResultInvalid,
            $"standard plane {requestedPlane} was not found in the feature tree.");
    }

    public Task<FeatureHandlerExecutionResult> ExecuteHoleAsync(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        FeatureHandlerExecutionState state,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (feature.Parameters.ContainsKey("hole_type"))
        {
            var validation = new HoleValidator().Validate(feature);
            if (!validation.IsValid)
                return Task.FromResult(FeatureHandlerExecutionResult.Failed(validation.FailureStage!, validation.Issues.ToArray()));
            var strategy = Hole.HoleExecutionStrategies.Get(validation.Definition!.HoleType);
            return Task.FromResult(FeatureHandlerExecutionResult.Failed(strategy.UnverifiedStage,
                $"{strategy.UnverifiedStage}: {strategy.HoleType} 尚未取证；禁止进入 COM 或降级为普通 Cut。"));
        }
        return Task.FromResult(Guard(
            PartFamilyFailureStages.HoleExecutionFailed,
            () =>
            {
                RequireBlind(feature.Parameters);
                var requestedDiameter = RequiredPositiveAlias(
                    feature.Parameters,
                    "hole_diameter_mm",
                    "diameter_mm");
                var depth = RequiredPositive(feature.Parameters, "depth_mm");
                var volumeBefore = MeasureSolidVolume();
                var sketch = FindDependencySketch(operation);
                if (sketch.EntityTypes.Count == 0 ||
                    sketch.EntityTypes.Any(type =>
                        !type.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase)))
                {
                    throw Stage(
                        PartFamilyFailureStages.HoleExecutionFailed,
                        "Simple hole requires a dependency sketch containing circles only.");
                }

                if (sketch.CircleDiametersMm.Count == 0 ||
                    sketch.CircleDiametersMm.Any(diameter =>
                        Math.Abs(diameter - requestedDiameter) > 1e-6))
                {
                    throw Stage(
                        PartFamilyFailureStages.HoleExecutionFailed,
                        "Simple-hole diameter does not match its dependency circle geometry.");
                }

                var sketchManager = ActivateSketchForFeatureCut(sketch);
                var manager = Own(_com.TryGetProperty(_model, "FeatureManager"))
                    ?? throw Stage(PartFamilyFailureStages.HoleExecutionFailed, "FeatureManager is unavailable.");
                try
                {
                    var featureObject = Own(_com.InvokeWithArgs(
                        manager,
                        "FeatureCut4",
                        BlindCutArguments(depth * MmToMeters)));
                    return ValidateFeatureResult(
                        feature,
                        operation,
                        featureObject,
                        volumeBefore,
                        VolumeChangeExpectation.Decrease);
                }
                finally
                {
                    CloseActiveSketchIfNeeded(sketchManager);
                }
            }));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        for (var index = _ownedReferences.Count - 1; index >= 0; index--)
        {
            try
            {
                _com.ReleaseComObject(_ownedReferences[index]);
            }
            catch
            {
                // Reference cleanup must continue even when one RCW is stale.
            }
        }

        _ownedReferences.Clear();
        _ownedReferenceSet.Clear();
        _sketches.Clear();
        _axisConfirmations.Clear();
    }

    private FeatureHandlerExecutionResult CreateSketch(
        FeatureDefinition feature,
        SolidWorksOperation operation)
    {
        var isRightPlane = (feature.TargetReference ?? operation.SketchPlane).Equals("RightPlane", StringComparison.OrdinalIgnoreCase);
        if (isRightPlane)
        {
            var validation = Sketch.RevolveProfileRules.Validate(feature);
            if (!validation.IsValid) throw Stage(validation.FailureStage!, string.Join("; ", validation.Issues));
        }
        if (operation.OperationType == "CreateCenterLine")
        {
            var axisSketchId = feature.Parameters.GetValueOrDefault("sketch_id");
            if (!isRightPlane || !Sketch.RevolveProfileRules.IsCenterLine(feature) || axisSketchId is null ||
                !_sketches.TryGetValue(axisSketchId, out var existing) || existing.Axis is null ||
                existing.AxisId != feature.Parameters.GetValueOrDefault("entity_id") ||
                operation.DependsOn.Count != 1 || operation.DependsOn[0] != existing.OperationId ||
                existing.AxisParameters is null ||
                new[] { "x1_mm", "y1_mm", "x2_mm", "y2_mm" }.Any(key =>
                    Coordinate(feature.Parameters, key) != Coordinate(existing.AxisParameters, key)) ||
                !_axisConfirmations.TryAdd(existing.OperationId, operation.OperationId))
                throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "中心线确认必须精确引用本次同草图已创建的唯一构造轴。");
            _sketches[operation.OperationId] = existing;
            return Passed(feature, operation, geometryChangeValidated: true) with
                { Logs = ["construction_centerline_reference_confirmed;construction_geometry:true"] };
        }
        if (!feature.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                "Serialized sketch entities are missing.");
        }

        SketchEntity[] entities;
        try
        {
            entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
        }
        catch (JsonException ex)
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Serialized sketch entities are invalid: {ex.Message}");
        }

        RequireEmptyCollection(feature.Parameters, "constraints");
        RequireEmptyCollection(feature.Parameters, "dimensions");
        if (!isRightPlane && entities.Any(entity => entity.IsConstructionCenterLine))
            throw Stage(PartFamilyFailureStages.InvalidFeatureParameter, "TopPlane 档案不授权构造轴。");
        SelectExactPlane(feature.TargetReference ?? operation.SketchPlane);

        var sketchManager = Own(_com.TryGetProperty(_model, "SketchManager"))
            ?? throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "SketchManager is unavailable.");
        var entered = false;
        object? sketchFeature = null;
        object? axis = null;
        SketchEntity? axisDefinition = null;
        try
        {
            _com.Invoke(sketchManager, "InsertSketch", true);
            entered = true;
            var activeSketch = Own(
                    _com.TryInvoke(_model, "GetActiveSketch2") ??
                    _com.TryGetProperty(sketchManager, "ActiveSketch"))
                ?? throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "InsertSketch did not activate a sketch.");

            foreach (var entity in entities)
            {
                var created = CreateSketchEntity(sketchManager, entity);
                if (entity.IsConstructionCenterLine) { axis = created; axisDefinition = entity; }
            }

            // TopPlane 保留既有执行合同，本次仅修复独立 RightPlane 档案。
            if (!isRightPlane)
                sketchFeature = Own(_com.TryInvoke(activeSketch, "GetFeature")) ?? activeSketch;
            _com.Invoke(sketchManager, "InsertSketch", true);
            entered = false;
            if (isRightPlane)
            {
                // ISketch 没有 GetFeature。树末尾仅是候选，必须反向取得同一个草图才可接受。
                sketchFeature = Own(_com.Invoke(_model, "FeatureByPositionReverse", 0));
                if (sketchFeature is null || _com.TryInvoke(sketchFeature, "GetTypeName2") is not "ProfileFeature")
                    throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "旋转轮廓缺少真实二维草图 IFeature。");
                var specificSketch = Own(_com.Invoke(sketchFeature, "GetSpecificFeature2"));
                if (!SameComIdentity(activeSketch, specificSketch))
                    throw Stage(PartFamilyFailureStages.SketchExecutionFailed, "候选 IFeature 与本次创建的旋转草图身份不一致。");
            }
        }
        finally
        {
            if (entered)
            {
                _com.TryInvoke(sketchManager, "InsertSketch", true);
            }
        }

        EnsureModelRebuild();
        var artifact = new SketchComArtifact(
            sketchFeature ?? throw new InvalidOperationException("草图未解析到有效建模对象。"),
            entities.Select(entity => entity.EntityType).ToHashSet(StringComparer.OrdinalIgnoreCase),
            entities
                .Where(entity =>
                    entity.EntityType.Equals(
                        SketchEntityTypes.Circle,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entity => CircleRadius(entity.Parameters) * 2d)
                .ToArray(),
            operation.OperationId, isRightPlane ? "RightPlane" : operation.SketchPlane,
            axis, axisDefinition?.EntityId, axisDefinition?.Parameters);
        _sketches[operation.OperationId] = artifact;
        _sketches[feature.FeatureId] = artifact;
        if (feature.Parameters.TryGetValue("sketch_id", out var sketchId) &&
            !string.IsNullOrWhiteSpace(sketchId))
        {
            _sketches[sketchId] = artifact;
        }

        return Passed(
            feature,
            operation,
            geometryChangeValidated: true);
    }

    private static bool SameComIdentity(object expected, object? actual)
    {
        if (ReferenceEquals(expected, actual)) return true;
        if (actual is null || !OperatingSystem.IsWindows() ||
            !Marshal.IsComObject(expected) || !Marshal.IsComObject(actual)) return false;
        nint expectedIdentity = 0, actualIdentity = 0;
        try
        {
            expectedIdentity = Marshal.GetIUnknownForObject(expected);
            actualIdentity = Marshal.GetIUnknownForObject(actual);
            return expectedIdentity == actualIdentity;
        }
        finally
        {
            if (actualIdentity != 0) Marshal.Release(actualIdentity);
            if (expectedIdentity != 0) Marshal.Release(expectedIdentity);
        }
    }

    private object CreateSketchEntity(object sketchManager, SketchEntity entity)
    {
        object? created;
        if (entity.IsConstructionCenterLine)
        {
            created = _com.Invoke(sketchManager, "CreateCenterLine",
                Coordinate(entity.Parameters, "x1_mm"), Coordinate(entity.Parameters, "y1_mm"), 0d,
                Coordinate(entity.Parameters, "x2_mm"), Coordinate(entity.Parameters, "y2_mm"), 0d);
            if (created is null || _com.TryGetProperty(created, "ConstructionGeometry") is not true)
                throw Stage(PartFamilyFailureStages.SketchGeometryCreateFailed, "CreateCenterLine 未返回可验证的构造线。");
        }
        else if (entity.EntityType.Equals(SketchEntityTypes.Line, StringComparison.OrdinalIgnoreCase))
        {
            created = _com.Invoke(
                sketchManager,
                "CreateLine",
                Coordinate(entity.Parameters, "x1_mm"),
                Coordinate(entity.Parameters, "y1_mm"),
                0d,
                Coordinate(entity.Parameters, "x2_mm"),
                Coordinate(entity.Parameters, "y2_mm"),
                0d);
        }
        else if (entity.EntityType.Equals(SketchEntityTypes.Rectangle, StringComparison.OrdinalIgnoreCase))
        {
            var centerX = OptionalCoordinate(entity.Parameters, "center_x_mm");
            var centerY = OptionalCoordinate(entity.Parameters, "center_y_mm");
            var horizontal = entity.Parameters.ContainsKey("length_mm")
                ? RequiredPositive(entity.Parameters, "length_mm")
                : RequiredPositive(entity.Parameters, "width_mm");
            var vertical = entity.Parameters.ContainsKey("height_mm")
                ? RequiredPositive(entity.Parameters, "height_mm")
                : RequiredPositive(entity.Parameters, "width_mm");
            created = _com.Invoke(
                sketchManager,
                "CreateCenterRectangle",
                centerX,
                centerY,
                0d,
                centerX + horizontal * MmToMeters / 2d,
                centerY + vertical * MmToMeters / 2d,
                0d);
        }
        else if (entity.EntityType.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase))
        {
            if (entity.Parameters.ContainsKey("pattern"))
            {
                throw Stage(
                    PartFamilyFailureStages.SketchGeometryCreateFailed,
                    $"Sketch entity {entity.EntityId} requests an unsupported circle pattern.");
            }

            var centerX = OptionalCoordinate(entity.Parameters, "center_x_mm");
            var centerY = OptionalCoordinate(entity.Parameters, "center_y_mm");
            var radius = CircleRadius(entity.Parameters) * MmToMeters;
            created = _com.Invoke(
                sketchManager,
                "CreateCircle",
                centerX,
                centerY,
                0d,
                centerX + radius,
                centerY,
                0d);
        }
        else
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch entity type {entity.EntityType} is not authorized.");
        }

        if (!OwnReturned(created))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch entity {entity.EntityId} returned no geometry object.");
        }
        return created!;
    }

    private FeatureHandlerExecutionResult ValidateFeatureResult(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        object? featureObject,
        double volumeBefore,
        VolumeChangeExpectation volumeChangeExpectation)
    {
        if (featureObject is null)
        {
            throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "Feature API returned null.");
        }

        EnsureRebuildAndErrorCode(featureObject);
        var volumeAfter = MeasureSolidVolume();
        var tolerance = Math.Max(1e-12d, Math.Abs(volumeBefore) * 1e-9d);
        var geometryChanged = volumeChangeExpectation switch
        {
            VolumeChangeExpectation.Increase => volumeAfter > volumeBefore + tolerance,
            VolumeChangeExpectation.Decrease => volumeBefore > 0d && volumeAfter < volumeBefore - tolerance,
            VolumeChangeExpectation.Changed => volumeBefore > 0d && Math.Abs(volumeAfter - volumeBefore) > tolerance,
            _ => false
        };
        if (!geometryChanged)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"Solid-body volume did not {volumeChangeExpectation.ToString().ToLowerInvariant()}: " +
                $"before={volumeBefore:R}, after={volumeAfter:R} cubic metres.");
        }

        // 记下 feature_id 到 COM 特征对象的映射，供阵列与镜像取种子。
        // 映射留在 Adapter 内部而不是放进共享的执行状态：执行状态会跨越
        // Adapter 边界被 Handler 与流水线读到，而本项目明令 Handler 不得拿到
        // 原始特征 RCW。CreatedObject 里放的是纯数据 FeatureAdapterArtifact，
        // 正是为此——它不能用来选中特征。
        _features[feature.FeatureId] = featureObject;

        return Passed(
            feature,
            operation,
            geometryChangeValidated: true,
            volumeBeforeCubicMeters: volumeBefore,
            volumeAfterCubicMeters: volumeAfter);
    }

    private double MeasureSolidVolume()
    {
        const int swSolidBody = 0;
        var bodiesValue = _com.TryInvoke(_model, "GetBodies2", swSolidBody, false);
        if (bodiesValue is null)
        {
            return 0d;
        }

        var bodies = bodiesValue is Array array
            ? array.Cast<object?>().Where(item => item is not null).Cast<object>().ToArray()
            : [bodiesValue];
        var volume = 0d;
        foreach (var body in bodies)
        {
            Own(body);
            var properties = _com.TryInvoke(body, "GetMassProperties", 1d);
            if (properties is not Array values || values.Length <= 3)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    "Solid-body mass properties are unavailable for geometry validation.");
            }

            var bodyVolume = Convert.ToDouble(values.GetValue(3), CultureInfo.InvariantCulture);
            if (!double.IsFinite(bodyVolume) || bodyVolume <= 0d)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    $"Solid-body volume is invalid: {bodyVolume:R}.");
            }

            volume += bodyVolume;
        }

        return volume;
    }

    private void EnsureRebuildAndErrorCode(object featureObject)
    {
        EnsureModelRebuild();

        var errorArguments = new object?[] { false };
        var errorCode = _com.TryInvokeWithArgs(featureObject, "GetErrorCode2", errorArguments);
        if (errorCode is null)
        {
            // Some late-bound RCWs do not marshal the GetErrorCode2(out bool)
            // argument. SOLIDWORKS keeps GetErrorCode as the documented,
            // superseded no-by-ref compatibility member.
            errorCode = _com.TryInvoke(featureObject, "GetErrorCode");
            if (errorCode is null)
            {
                throw Stage(
                    PartFamilyFailureStages.FeatureResultInvalid,
                    "Feature GetErrorCode2 and GetErrorCode returned null.");
            }
        }

        var error = Convert.ToInt32(errorCode, CultureInfo.InvariantCulture);
        var warning =
            errorArguments[0] is bool value &&
            value;
        if (error != 0 || warning)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureResultInvalid,
                $"Feature GetErrorCode2 returned error={error}, warning={warning}.");
        }
    }

    private void EnsureModelRebuild()
    {
        if (!_com.TryInvokeBool(_model, "ForceRebuild3", false))
        {
            throw Stage(PartFamilyFailureStages.FeatureResultInvalid, "ForceRebuild3(false) did not return true.");
        }
    }

    private void SelectDependencySketch(SolidWorksOperation operation) =>
        SelectSketch(FindDependencySketch(operation));

    private SketchComArtifact FindDependencySketch(SolidWorksOperation operation)
    {
        foreach (var dependency in operation.DependsOn.Reverse())
        {
            if (_sketches.TryGetValue(dependency, out var sketch))
            {
                return sketch;
            }
        }

        if (operation.Parameters.TryGetValue("sketch_id", out var sketchId) &&
            _sketches.TryGetValue(sketchId, out var referencedSketch))
        {
            return referencedSketch;
        }

        throw Stage(
            PartFamilyFailureStages.FeatureArtifactMissing,
            $"No dependency sketch artifact is available for operation {operation.OperationId}.");
    }

    private void SelectSketch(SketchComArtifact sketch)
    {
        ClearSelection();
        if (!_com.TryInvokeBool(sketch.Feature, "Select2", false, 0) ||
            !HasVerifiedSelection())
        {
            throw Stage(PartFamilyFailureStages.FeatureArtifactMissing, "Dependency sketch selection failed.");
        }
    }

    private object ActivateSketchForFeatureCut(SketchComArtifact sketch)
    {
        SelectSketch(sketch);
        var sketchManager = Own(_com.TryGetProperty(_model, "SketchManager"))
            ?? throw Stage(
                PartFamilyFailureStages.FeatureArtifactMissing,
                "SketchManager is unavailable while activating a cut sketch.");
        _com.Invoke(sketchManager, "InsertSketch", true);
        var activeSketch =
            _com.TryInvoke(_model, "GetActiveSketch2") ??
            _com.TryGetProperty(sketchManager, "ActiveSketch");
        if (activeSketch is null)
        {
            throw Stage(
                PartFamilyFailureStages.FeatureArtifactMissing,
                "Selected dependency sketch did not become active for FeatureCut4.");
        }

        Own(activeSketch);
        return sketchManager;
    }

    private void CloseActiveSketchIfNeeded(object sketchManager)
    {
        var activeSketch =
            _com.TryInvoke(_model, "GetActiveSketch2") ??
            _com.TryGetProperty(sketchManager, "ActiveSketch");
        if (activeSketch is not null)
        {
            _com.TryInvoke(sketchManager, "InsertSketch", true);
        }
    }

    private void SelectExactPlane(string requestedPlane)
    {
        if (!PlaneAliases.TryGetValue(requestedPlane, out var aliases))
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"Reference {requestedPlane} is not an authorized standard plane.");
        }

        var extension = Own(_com.TryGetProperty(_model, "Extension"));
        if (extension is not null)
        {
            foreach (var alias in aliases)
            {
                ClearSelection();
                if (_com.TryInvokeBool(
                        extension,
                        "SelectByID2",
                        alias,
                        "PLANE",
                        0d,
                        0d,
                        0d,
                        false,
                        0,
                        null,
                        0) &&
                    HasVerifiedSelection())
                {
                    return;
                }
            }
        }

        object? candidate = null;
        var current = Own(_com.TryInvoke(_model, "FirstFeature"));
        while (current is not null)
        {
            var name = _com.TryGetProperty(current, "Name")?.ToString();
            var typeName = _com.TryInvoke(current, "GetTypeName2")?.ToString();
            if (aliases.Contains(name ?? string.Empty, StringComparer.OrdinalIgnoreCase) &&
                string.Equals(typeName, "RefPlane", StringComparison.OrdinalIgnoreCase))
            {
                candidate = current;
                break;
            }

            current = Own(_com.TryInvoke(current, "GetNextFeature"));
        }

        ClearSelection();
        if (candidate is null ||
            !_com.TryInvokeBool(candidate, "Select2", false, 0) ||
            !HasVerifiedSelection())
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"Exact standard plane {requestedPlane} could not be selected.");
        }
    }

    private void ClearSelection()
    {
        try
        {
            _com.Invoke(_model, "ClearSelection2", true);
        }
        catch (Exception ex)
        {
            throw Stage(
                PartFamilyFailureStages.SketchExecutionFailed,
                $"ClearSelection2(true) failed: {ex.GetBaseException().Message}");
        }
    }

    private bool HasVerifiedSelection()
    {
        var selectionManager = Own(_com.TryGetProperty(_model, "SelectionManager"));
        if (selectionManager is null)
        {
            return false;
        }

        var countValue = _com.TryInvoke(selectionManager, "GetSelectedObjectCount2", -1);
        if (countValue is null ||
            Convert.ToInt32(countValue, CultureInfo.InvariantCulture) < 1)
        {
            return false;
        }

        return Own(_com.TryInvoke(selectionManager, "GetSelectedObject6", 1, -1)) is not null;
    }

    private FeatureHandlerExecutionResult Guard(
        string defaultStage,
        Func<FeatureHandlerExecutionResult> action)
    {
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return action();
        }
        catch (FeatureAdapterException ex)
        {
            return FeatureHandlerExecutionResult.Failed(ex.Stage, $"{ex.Stage}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FeatureHandlerExecutionResult.Failed(
                defaultStage,
                $"{defaultStage}: {ex.GetBaseException().Message}");
        }
    }

    private FeatureHandlerExecutionResult Passed(
        FeatureDefinition feature,
        SolidWorksOperation operation,
        bool geometryChangeValidated,
        double? volumeBeforeCubicMeters = null,
        double? volumeAfterCubicMeters = null) =>
        FeatureHandlerExecutionResult.Passed(
            [
                $"feature_adapter_success:{AdapterId}@{AdapterVersion}:{feature.FeatureId}",
                "feature_result_non_null:true",
                "feature_rebuild_passed:true"
            ],
            new FeatureAdapterArtifact(
                operation.OperationId,
                feature.FeatureId,
                feature.FeatureType,
                AdapterId,
                AdapterVersion,
                ResultObjectValidated: true,
                RebuildPassed: true,
                geometryChangeValidated,
                volumeBeforeCubicMeters,
                volumeAfterCubicMeters));

    private object? Own(object? value)
    {
        if (value is not null && _ownedReferenceSet.Add(value))
        {
            _ownedReferences.Add(value);
        }

        return value;
    }

    private bool OwnReturned(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is Array array)
        {
            var found = false;
            foreach (var item in array)
            {
                found |= Own(item) is not null;
            }

            return found;
        }

        Own(value);
        return true;
    }

    private static object?[] BlindCutArguments(double depthMeters) =>
    [
        true, false, true, 0, 0, depthMeters, depthMeters,
        false, false, false, false, OneDegreeInRadians, OneDegreeInRadians,
        false, false, false, false, false, true, true, true, false, false,
        0, 0, false, false
    ];

    private static void RequireEmptyCollection(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var json) || string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var empty = root.ValueKind switch
        {
            JsonValueKind.Array => root.GetArrayLength() == 0,
            JsonValueKind.Object => !root.EnumerateObject().Any(),
            JsonValueKind.Null => true,
            _ => false
        };
        if (!empty)
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"Sketch {name} are outside the authorized adapter profile.");
        }
    }

    private static double Coordinate(
        IReadOnlyDictionary<string, string> values,
        string name)
    {
        if (!values.TryGetValue(name, out var text) ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value))
        {
            throw Stage(
                PartFamilyFailureStages.SketchGeometryCreateFailed,
                $"{name} must be a finite number.");
        }

        return value * MmToMeters;
    }

    private static double OptionalCoordinate(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        values.ContainsKey(name) ? Coordinate(values, name) : 0d;

    private static double CircleRadius(IReadOnlyDictionary<string, string> values)
    {
        if (values.ContainsKey("radius_mm"))
        {
            return RequiredPositive(values, "radius_mm");
        }

        return RequiredPositiveAlias(
                   values,
                   "diameter_mm",
                   "hole_diameter_mm",
                   "outer_diameter_mm",
                   "inner_diameter_mm",
                   "bolt_hole_diameter_mm") /
               2d;
    }

    private static double RequiredPositive(
        IReadOnlyDictionary<string, string> values,
        string name) =>
        RequiredPositiveAlias(values, name);

    private static void RequireBlind(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("direction", out var direction) &&
            !direction.Equals("blind", StringComparison.OrdinalIgnoreCase))
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"direction={direction} is not authorized; only blind is supported.");
        }

        if (values.TryGetValue("through_all", out var throughAllText) &&
            bool.TryParse(throughAllText, out var throughAll) &&
            throughAll)
        {
            throw Stage(
                PartFamilyFailureStages.InvalidFeatureParameter,
                "through_all=true is not authorized; only a positive blind depth is supported.");
        }
    }

    private static double RequiredPositiveAlias(
        IReadOnlyDictionary<string, string> values,
        params string[] names)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var text) &&
                double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                double.IsFinite(value) &&
                value > 0)
            {
                return value;
            }
        }

        throw Stage(
            PartFamilyFailureStages.InvalidFeatureParameter,
            $"{string.Join("/", names)} must contain a finite positive number.");
    }

    private static FeatureAdapterException Stage(string stage, string message) =>
        new(stage, message);

    private sealed record SketchComArtifact(
        object Feature,
        IReadOnlySet<string> EntityTypes,
        IReadOnlyList<double> CircleDiametersMm,
        string OperationId,
        string ReferencePlane,
        object? Axis = null,
        string? AxisId = null,
        IReadOnlyDictionary<string, string>? AxisParameters = null);

    private enum VolumeChangeExpectation
    {
        Increase,
        Decrease,

        /// <summary>
        /// 只要求体积发生变化，不限方向。圆角/倒角作用在凸边时去料、
        /// 作用在凹边时加料，方向由边的凸凹性决定而非特征类型决定，
        /// 因此断言固定方向会产生假失败。
        /// </summary>
        Changed
    }

    private sealed class FeatureAdapterException(string stage, string message) : Exception(message)
    {
        public string Stage { get; } = stage;
    }
}
