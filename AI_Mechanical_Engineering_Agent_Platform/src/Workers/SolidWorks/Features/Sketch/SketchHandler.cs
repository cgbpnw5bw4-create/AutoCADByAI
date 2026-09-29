using System.Text.Json;
using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Sketch;

public sealed class SketchHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureHandlerTypes.Sketch;

    public override string OperationType => "CreateSketch";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("reference_plane", "reference", true, "精确基准面或平面引用。"),
        new("entities", "SketchEntity[] JSON", true, "草图实体集合。"),
        new("constraints", "SketchConstraint[] JSON", false, "草图约束集合。"),
        new("dimensions", "dictionary JSON", false, "驱动尺寸集合。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "ISketchManager.InsertSketch / CreateLine / CreateCircle / CreateCenterRectangle",
        "SOLIDWORKS API Help (official Web Help)",
        [
            "InsertSketch(UpdateEditRebuild)",
            "CreateLine(X1,Y1,Z1,X2,Y2,Z2)",
            "CreateCircle(Xc,Yc,Zc,Xp,Yp,Zp)",
            "CreateCenterRectangle(Xc,Yc,Zc,Xp,Yp,Zp)"
        ],
        "Sketch entity object or null; InsertSketch has no feature-success contract.",
        [
            "An exact reference plane or face is selected.",
            "Coordinates are expressed in metres.",
            "The requested entity and constraint subset has evidence for the same parameter profile."
        ],
        FeatureApiEvidenceStatuses.Verified,
        [
            "TopFace and arbitrary reference resolution are not implemented.",
            "Arc, slot and constraint application lack handler-level evidence.",
            "A non-null entity alone does not prove a valid closed profile."
        ],
        [
            "V1.9 part-family diagnostics exercised selected subsets only.",
            "V2.0-C diagnostic verified TopPlane line, center rectangle and circle creation with non-null geometry and successful rebuild.",
            "Diagnostic SLDPRT and STEP identity is recorded in the bound diagnostic report; a per-run hash is deliberately not restated here because it cannot be known before the run that this claim is hashed into."
        ],
        EvidenceId: "v2.2-d-20260928-SketchHandler",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "standard_plane_top;line+center_rectangle+circle;empty_constraints;empty_dimensions;millimetres",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_2_d_final/20260928_014654_0264082/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e484715785d2ba21ea7ae3efbca5238f37a4fbc988cb37b9658603d33e22a048");

    public FeatureApiEvidence RightPlaneApiEvidence { get; } = new(
        "ISketchManager.CreateLine / CreateCenterLine / ISketchSegment.ConstructionGeometry",
        "SOLIDWORKS 官方帮助与本机 SDK",
        ["明确毫米坐标", "唯一构造轴，selection_mark=16"],
        "非空线段与草图 Feature；构造属性必须读回 true。",
        ["RightPlane 闭合半截面", "空 constraints/dimensions", "唯一 X 轴从原点到总长度"],
        FeatureApiEvidenceStatuses.Verified,
        ["开放、多轴、偏心轴、错误构造属性或缺少真实草图 Feature 时失败。"],
        ["V2.2-D 独立绑定 RightPlane 真机诊断；不能使用 TopPlane 证据授权此路径。"],
        EvidenceId: "v2.2-d-20260928-RightPlaneSketch",
        HandlerVersion: "2.0-c.2",
        ParameterProfile: "right_plane;closed_half_section_lines;single_construction_axis_x;axis_mark_16;empty_constraints;empty_dimensions;millimetres",
        SolidWorksVersion: "31.5.0",
        DiagnosticRunPath: "evidence/solidworks/v2_2_d_final/20260928_014622_7587652/feature_execution_report.json",
        SourceRevision: "feature-execution-source-sha256:e484715785d2ba21ea7ae3efbca5238f37a4fbc988cb37b9658603d33e22a048");

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(SketchHandler)}.");
        }

        if (string.IsNullOrWhiteSpace(feature.TargetReference))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.SketchReferenceMissing,
                $"{PartFamilyFailureStages.SketchReferenceMissing}: sketch {feature.FeatureId} has no reference plane.");
        }
        if (feature.TargetReference.Equals("RightPlane", StringComparison.OrdinalIgnoreCase))
            return RevolveProfileRules.Validate(feature);

        var unknownParameters = RejectUnknownParameters(
            feature,
            "sketch_id",
            "reference_plane",
            "entity_count",
            "constraint_count",
            "entities",
            "constraints",
            "dimensions",
            "x1_mm",
            "y1_mm",
            "x2_mm",
            "y2_mm",
            "center_x_mm",
            "center_y_mm",
            "length_mm",
            "width_mm",
            "height_mm",
            "radius_mm",
            "diameter_mm");
        if (!unknownParameters.IsValid)
        {
            return unknownParameters;
        }

        if (!feature.Parameters.TryGetValue("entities", out var entitiesJson))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: sketch {feature.FeatureId} has no serialized entities schema.");
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(entitiesJson) ?? [];
            var supported = new HashSet<string>(
                [SketchEntityTypes.Line, SketchEntityTypes.Rectangle, SketchEntityTypes.Circle],
                StringComparer.OrdinalIgnoreCase);
            if (entities.Length == 0 ||
                entities.Any(entity => !supported.Contains(entity.EntityType)))
            {
                return FeatureHandlerValidationResult.Failed(
                    PartFamilyFailureStages.UnsupportedSketchEntity,
                    $"{PartFamilyFailureStages.UnsupportedSketchEntity}: sketch {feature.FeatureId} contains no supported entity.");
            }
        }
        catch (JsonException ex)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: sketch {feature.FeatureId} entities JSON is invalid: {ex.Message}");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override FeatureHandlerValidationResult ValidateParameterProfileForRealExecution(
        FeatureDefinition feature)
    {
        var validation = Validate(feature);
        if (!validation.IsValid)
        {
            return validation;
        }
        if (feature.TargetReference?.Equals("RightPlane", StringComparison.OrdinalIgnoreCase) == true)
            return validation;

        if (!string.Equals(
                feature.TargetReference,
                "TopPlane",
                StringComparison.OrdinalIgnoreCase))
        {
            return EvidenceProfileBlocked(
                feature,
                $"reference_plane={feature.TargetReference ?? "missing"} is outside evidence profile {ApiEvidence.ParameterProfile}.");
        }

        if (!IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("constraints")) ||
            !IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("dimensions")))
        {
            return EvidenceProfileBlocked(
                feature,
                $"constraints or dimensions are outside evidence profile {ApiEvidence.ParameterProfile}.");
        }

        try
        {
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(
                feature.Parameters.GetValueOrDefault("entities") ?? "[]") ?? [];
            foreach (var entity in entities)
            {
                var allowed = entity.EntityType.ToLowerInvariant() switch
                {
                    SketchEntityTypes.Line =>
                        new[] { "x1_mm", "y1_mm", "x2_mm", "y2_mm" },
                    SketchEntityTypes.Rectangle =>
                        new[] { "center_x_mm", "center_y_mm", "length_mm", "width_mm", "height_mm" },
                    SketchEntityTypes.Circle =>
                        new[] { "center_x_mm", "center_y_mm", "radius_mm", "diameter_mm" },
                    _ => Array.Empty<string>()
                };
                var unknown = entity.Parameters.Keys
                    .Where(parameter => !allowed.Contains(parameter, StringComparer.OrdinalIgnoreCase))
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (unknown.Length > 0)
                {
                    return EvidenceProfileBlocked(
                        feature,
                        $"entity {entity.EntityId} contains parameters outside evidence profile: " +
                        $"{string.Join(", ", unknown)}.");
                }
            }
        }
        catch (JsonException ex)
        {
            return EvidenceProfileBlocked(feature, $"entities JSON is invalid: {ex.Message}");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return context.Adapter is null
            ? Task.FromResult(AdapterMissing(context.Feature))
            : context.Adapter.ExecuteSketchAsync(
                context.Feature,
                context.Operation,
                context.State,
                cancellationToken);
    }

    public override FeatureHandlerValidationResult ValidateEvidenceForRealExecution(FeatureDefinition feature)
    {
        var validation = ValidateParameterProfileForRealExecution(feature);
        return !validation.IsValid ? validation
            : FeatureExecutionEvidencePolicy.ValidateEvidence(this, feature,
                feature.TargetReference?.Equals("RightPlane", StringComparison.OrdinalIgnoreCase) == true ? RightPlaneApiEvidence : ApiEvidence);
    }

    public override FeatureHandlerReport GenerateReport(FeatureDefinition feature, FeatureHandlerExecutionResult result)
    {
        var evidence = feature.TargetReference?.Equals("RightPlane", StringComparison.OrdinalIgnoreCase) == true
            ? RightPlaneApiEvidence : ApiEvidence;
        return base.GenerateReport(feature, result) with
        {
            ApiEvidenceStatus = evidence.Status, EvidenceId = evidence.EvidenceId,
            EvidenceHandlerVersion = evidence.HandlerVersion, EvidenceParameterProfile = evidence.ParameterProfile,
            EvidenceSolidWorksVersion = evidence.SolidWorksVersion, EvidenceDiagnosticRunPath = evidence.DiagnosticRunPath,
            EvidenceSourceRevision = evidence.SourceRevision
        };
    }

    internal static bool IsEmptyJsonCollection(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.GetArrayLength() == 0,
                JsonValueKind.Object => !document.RootElement.EnumerateObject().Any(),
                JsonValueKind.Null => true,
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

// 此纯规则同时用于 Handler 前置校验与 Adapter 防御校验；不持有 COM 对象。
internal static class RevolveProfileRules
{
    private static readonly string[] Coordinates = ["x1_mm", "y1_mm", "x2_mm", "y2_mm"];
    private static readonly string[] Metadata = ["profile", "diameter_mm", "length_mm", "optional_step_diameters", "optional_step_lengths"];
    private static readonly string[] Common = ["feature_id", "feature_type", "sketch_id", "reference_plane", "target_reference"];

    public static bool IsCenterLine(FeatureDefinition feature) =>
        feature.Parameters.GetValueOrDefault("entity_type") == SketchEntityTypes.ConstructionCenterLine;

    public static FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        try
        {
            Require(!feature.Parameters.TryGetValue("reference_plane", out var plane) || plane.Equals("RightPlane", StringComparison.OrdinalIgnoreCase),
                "实体元数据基准面与 RightPlane 档案不一致。");
            if (IsCenterLine(feature))
            {
                RequireKeys(feature.Parameters, Common.Concat(Coordinates).Concat(["entity_id", "entity_type", "construction", "axis", "selection_mark"]));
                ValidateAxis(feature.Parameters, feature.Parameters.GetValueOrDefault("entity_id") ?? "", null);
                Require(feature.Parameters.GetValueOrDefault("construction") == "true", "中心线必须声明 construction=true。");
                return FeatureHandlerValidationResult.Passed();
            }

            RequireKeys(feature.Parameters, Common.Concat(Coordinates).Concat(Metadata)
                .Concat(["entity_count", "constraint_count", "entities", "constraints", "dimensions"]));
            Require(SketchHandler.IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("constraints")) &&
                SketchHandler.IsEmptyJsonCollection(feature.Parameters.GetValueOrDefault("dimensions")), "本档案只支持空约束和空尺寸。");
            var entities = JsonSerializer.Deserialize<SketchEntity[]>(feature.Parameters.GetValueOrDefault("entities") ?? "[]") ?? [];
            Require(entities.Length >= 5 && entities.All(entity => entity is not null) &&
                entities.Select(entity => entity.EntityId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == entities.Length,
                "轮廓实体不足或标识重复。");
            Require(!feature.Parameters.TryGetValue("entity_count", out var count) || int.Parse(count, CultureInfo.InvariantCulture) == entities.Length,
                "实体计数与序列化实体不一致。");
            Require(!feature.Parameters.TryGetValue("constraint_count", out var constraints) || constraints == "0", "本档案没有约束实体。");
            var axes = entities.Where(entity => entity.IsConstructionCenterLine).ToArray();
            Require(axes.Length == 1 && axes[0].Construction &&
                axes[0].EntityType == SketchEntityTypes.ConstructionCenterLine, "必须恰好包含一条明确的构造中心线。");
            var lines = entities.Where(entity => !entity.IsConstructionCenterLine).ToArray();
            Require(lines.All(entity => entity.EntityType == SketchEntityTypes.Line && !entity.Construction), "旋转轮廓仅允许普通直线。");
            var length = Number(feature.Parameters, "length_mm");
            var diameter = Number(feature.Parameters, "diameter_mm");
            Require(length > 0 && diameter > 0 && feature.Parameters.GetValueOrDefault("profile") == "closed_half_section", "缺少合法半截面元数据。");
            RequireKeys(axes[0].Parameters, Coordinates.Concat(["axis", "selection_mark"]));
            ValidateAxis(axes[0].Parameters, axes[0].EntityId, length);
            var points = new List<(double X1, double Y1, double X2, double Y2)>();
            foreach (var line in lines)
            {
                RequireKeys(line.Parameters, Coordinates.Concat(Metadata));
                foreach (var key in Metadata.Where(line.Parameters.ContainsKey))
                    Require(line.Parameters[key] == feature.Parameters.GetValueOrDefault(key), $"实体与草图元数据 {key} 不一致。");
                var p = (Number(line.Parameters, "x1_mm"), Number(line.Parameters, "y1_mm"),
                    Number(line.Parameters, "x2_mm"), Number(line.Parameters, "y2_mm"));
                Require(p.Item1 >= 0 && p.Item2 >= 0 && p.Item3 >= 0 && p.Item4 >= 0 &&
                    (p.Item1 != p.Item3 || p.Item2 != p.Item4), "轮廓不得跨轴、反向坐标或包含零长线。");
                points.Add(p);
            }
            for (var i = 0; i < points.Count; i++)
            {
                var next = points[(i + 1) % points.Count];
                Require(Equal(points[i].X2, next.X1) && Equal(points[i].Y2, next.Y1), "半截面必须逐段连续闭合。");
            }
            Require(Equal(points[0].X1, 0) && Equal(points[0].Y1, 0) && Equal(points[0].X2, 0) &&
                Equal(points[0].Y2, diameter / 2) &&
                Equal(points[^1].X1, length) && Equal(points[^1].Y1, 0) && Equal(points[^1].X2, 0) && Equal(points[^1].Y2, 0) &&
                Equal(points[^2].X1, length) && Equal(points[^2].X2, length) && points[^2].Y1 > 0 && Equal(points[^2].Y2, 0),
                "轮廓必须从原点向正半径展开并沿 X 轴闭合。");
            var sections = points.Skip(1).Take(points.Count - 3).ToArray();
            Require(sections.All(p => p.Y1 > 0 && p.Y2 > 0 &&
                ((Equal(p.Y1, p.Y2) && p.X2 > p.X1) || (Equal(p.X1, p.X2) && p.Y1 != p.Y2))), "半截面只允许正向轴段及径向台阶。");
            Require(!sections.Zip(sections.Skip(1), (left, right) =>
                Equal(left.X1, left.X2) && Equal(right.X1, right.X2)).Any(bothVertical => bothVertical),
                "相邻径向线不得折返或重叠。");
            var stepDiameters = List(feature.Parameters.GetValueOrDefault("optional_step_diameters"));
            var stepLengths = List(feature.Parameters.GetValueOrDefault("optional_step_lengths"));
            Require(stepDiameters.Length == stepLengths.Length && stepLengths.Sum() < length, "台阶列表必须配对且留有正的基本段。");
            var expectedDiameters = stepDiameters.Prepend(diameter).ToArray();
            var expectedLengths = stepLengths.Prepend(length - stepLengths.Sum()).ToArray();
            var horizontal = sections.Where(p => p.X2 > p.X1).ToArray();
            Require(horizontal.Length == expectedLengths.Length, "实际轴段数与声明台阶不一致。");
            for (var i = 0; i < horizontal.Length; i++)
                Require(Equal(horizontal[i].X2 - horizontal[i].X1, expectedLengths[i]) &&
                    Equal(horizontal[i].Y1 * 2, expectedDiameters[i]), "明确坐标与直径、轴向长度元数据不一致。");
            return FeatureHandlerValidationResult.Passed();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return FeatureHandlerValidationResult.Failed(PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {ex.Message}");
        }
    }

    private static void ValidateAxis(IReadOnlyDictionary<string, string> p, string id, double? length)
    {
        Require(!string.IsNullOrWhiteSpace(id) && p.GetValueOrDefault("axis") == id &&
            p.GetValueOrDefault("selection_mark") == "16" &&
            Equal(Number(p, "x1_mm"), 0) && Equal(Number(p, "y1_mm"), 0) && Equal(Number(p, "y2_mm"), 0) &&
            Number(p, "x2_mm") > 0 && (length is null || Equal(Number(p, "x2_mm"), length.Value)),
            "构造轴必须唯一、mark 16，并从原点沿 X 轴覆盖完整长度。");
    }

    private static void RequireKeys(IReadOnlyDictionary<string, string> p, IEnumerable<string> allowed) =>
        Require(!p.Keys.Except(allowed, StringComparer.OrdinalIgnoreCase).Any(), "存在本档案未定义的参数。");
    private static double Number(IReadOnlyDictionary<string, string> p, string key)
    {
        Require(p.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value),
            $"参数 {key} 必须为有限数字。");
        return double.Parse(p[key], CultureInfo.InvariantCulture);
    }
    private static double[] List(string? text)
    {
        double[] values = string.IsNullOrWhiteSpace(text) ? [] : text.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        Require(values.All(value => double.IsFinite(value) && value > 0), "台阶必须为有限正数。");
        return values;
    }
    private static bool Equal(double left, double right) => Math.Abs(left - right) <= 1e-9;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
