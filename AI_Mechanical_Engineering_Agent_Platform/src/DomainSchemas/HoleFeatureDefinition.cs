using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomainSchemas;

public static class HoleTypes
{
    public const string Simple = "SimpleHole";
    public const string Counterbore = "CounterboreHole";
    public const string Countersink = "CountersinkHole";
    public const string Tapped = "TappedHole";
    public static IReadOnlyList<string> All { get; } = [Simple, Counterbore, Countersink, Tapped];
}

// position 使用参考面的局部毫米坐标；z_mm 必须为 0。
public sealed record HolePosition(
    [property: JsonPropertyName("x_mm")] double Xmm,
    [property: JsonPropertyName("y_mm")] double Ymm,
    [property: JsonPropertyName("z_mm")] double Zmm = 0);

// 标准线格式使用 ToParameters；本纯 DTO 不替代既有 FeatureDefinition 字典。
public sealed record HoleFeatureDefinition(
    string HoleType, double DiameterMm, double? DepthMm, bool ThroughAll,
    IReadOnlyList<HolePosition> Position, string ReferenceFace, int Quantity,
    string? PatternReference = null, double? CounterboreDiameterMm = null,
    double? CounterboreDepthMm = null, double? CountersinkDiameterMm = null,
    double? CountersinkAngleDeg = null, string? ThreadStandard = null,
    string? ThreadSize = null, double? ThreadPitch = null, double? ThreadDepthMm = null,
    double? TapDrillDiameterMm = null)
{
    public double OuterDiameterMm => CounterboreDiameterMm ?? CountersinkDiameterMm ?? DiameterMm;
    public double CountersinkHeightMm => CountersinkDiameterMm is { } outer && CountersinkAngleDeg is { } angle
        ? (outer - DiameterMm) / (2 * Math.Tan(angle * Math.PI / 360)) : 0;

    public IReadOnlyDictionary<string, string> ToParameters()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["hole_type"] = HoleType, ["diameter_mm"] = N(DiameterMm),
            ["through_all"] = ThroughAll ? "true" : "false",
            ["position"] = JsonSerializer.Serialize(Position), ["reference_face"] = ReferenceFace,
            ["quantity"] = Quantity.ToString(CultureInfo.InvariantCulture)
        };
        void Number(string key, double? value) { if (value.HasValue) values[key] = N(value.Value); }
        void Text(string key, string? value) { if (value is not null) values[key] = value; }
        Number("depth_mm", DepthMm); Number("counterbore_diameter_mm", CounterboreDiameterMm);
        Number("counterbore_depth_mm", CounterboreDepthMm); Number("countersink_diameter_mm", CountersinkDiameterMm);
        Number("countersink_angle_deg", CountersinkAngleDeg); Number("thread_pitch", ThreadPitch);
        Number("thread_depth_mm", ThreadDepthMm); Number("tap_drill_diameter_mm", TapDrillDiameterMm);
        Text("pattern_reference", PatternReference); Text("thread_standard", ThreadStandard); Text("thread_size", ThreadSize);
        return values;
    }
    private static string N(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

public sealed record HoleValidationResult(HoleFeatureDefinition? Definition, string? FailureStage, IReadOnlyList<string> Issues)
{
    public bool IsValid => Definition is not null && FailureStage is null;
}

public sealed class HoleValidator
{
    private static readonly IReadOnlyDictionary<string, (double Diameter, double Pitch, double Drill)> Threads =
        new Dictionary<string, (double, double, double)>(StringComparer.OrdinalIgnoreCase)
        { ["M3"] = (3, .5, 2.5), ["M4"] = (4, .7, 3.3), ["M5"] = (5, .8, 4.2), ["M6"] = (6, 1, 5),
          ["M8"] = (8, 1.25, 6.75), ["M10"] = (10, 1.5, 8.5), ["M12"] = (12, 1.75, 10.25), ["M16"] = (16, 2, 14) };
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    { "hole_type", "diameter_mm", "depth_mm", "through_all", "position", "reference_face", "quantity", "pattern_reference",
      "feature_id", "feature_type", "sketch_id", "referenced_sketches", "target_reference" };
    private static readonly IReadOnlyDictionary<string, string[]> Specific = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [HoleTypes.Simple] = [], [HoleTypes.Counterbore] = ["counterbore_diameter_mm", "counterbore_depth_mm"],
        [HoleTypes.Countersink] = ["countersink_diameter_mm", "countersink_angle_deg"],
        [HoleTypes.Tapped] = ["thread_standard", "thread_size", "thread_pitch", "thread_depth_mm", "tap_drill_diameter_mm"]
    };

    public HoleValidationResult Validate(FeatureDefinition feature)
    {
        var p = feature.Parameters;
        var type = HoleTypes.All.FirstOrDefault(t => t.Equals(p.GetValueOrDefault("hole_type"), StringComparison.OrdinalIgnoreCase));
        if (type is null) return Fail(PartFamilyFailureStages.UnsupportedHoleType, "hole_type 未注册。");
        if (p.Keys.Any(k => !Common.Contains(k) && !Specific[type].Contains(k, StringComparer.OrdinalIgnoreCase)))
            return Invalid("参数不属于所选孔类型，禁止普通孔携带攻丝或沉孔语义。");
        try
        {
            double? Number(string key) => p.TryGetValue(key, out var text)
                ? double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) : null;
            var diameter = Number("diameter_mm") ?? double.NaN;
            var depth = Number("depth_mm");
            var through = p.TryGetValue("through_all", out var throughText) && bool.Parse(throughText);
            var qty = p.TryGetValue("quantity", out var qtyText) ? int.Parse(qtyText, CultureInfo.InvariantCulture) : 1;
            if (!Positive(diameter) || (!through && !Positive(depth)) || (depth.HasValue && !Positive(depth)))
                return Invalid("diameter_mm 必须有限且大于 0；盲孔必须有正 depth_mm。");
            var face = p.GetValueOrDefault("reference_face");
            if (string.IsNullOrWhiteSpace(face)) return Fail(PartFamilyFailureStages.HoleReferenceFaceMissing, "reference_face 缺失。");
            if (!p.TryGetValue("position", out var positions)) return Invalid("position 缺失。");
            using var doc = JsonDocument.Parse(positions);
            var elements = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement.EnumerateArray().ToArray() : [doc.RootElement];
            var points = elements.Select(e => new HolePosition(e.GetProperty("x_mm").GetDouble(),
                e.GetProperty("y_mm").GetDouble(), e.TryGetProperty("z_mm", out var z) ? z.GetDouble() : 0)).ToArray();
            if (qty < 1 || qty > 1000 || points.Length != qty || points.Any(v =>
                !double.IsFinite(v.Xmm) || !double.IsFinite(v.Ymm) || !double.IsFinite(v.Zmm) || Math.Abs(v.Zmm) > 1e-9))
                return Invalid("quantity 必须为 1..1000 且等于显式孔位数量；局部坐标必须有限且 z_mm=0。");
            var hole = new HoleFeatureDefinition(type, diameter, depth, through, points, face, qty,
                p.GetValueOrDefault("pattern_reference"), Number("counterbore_diameter_mm"), Number("counterbore_depth_mm"),
                Number("countersink_diameter_mm"), Number("countersink_angle_deg"), p.GetValueOrDefault("thread_standard"),
                p.GetValueOrDefault("thread_size"), Number("thread_pitch"), Number("thread_depth_mm"), Number("tap_drill_diameter_mm"));
            if (type == HoleTypes.Counterbore && (!Positive(hole.CounterboreDiameterMm) || hole.CounterboreDiameterMm <= diameter ||
                !Positive(hole.CounterboreDepthMm) || depth.HasValue && hole.CounterboreDepthMm >= depth))
                return Invalid("沉孔直径必须大于主孔，沉孔深度必须为正且小于孔深。");
            if (type == HoleTypes.Countersink && (!Positive(hole.CountersinkDiameterMm) || hole.CountersinkDiameterMm <= diameter ||
                !Positive(hole.CountersinkAngleDeg) || hole.CountersinkAngleDeg >= 180 || !Positive(hole.CountersinkHeightMm) ||
                depth.HasValue && hole.CountersinkHeightMm >= depth))
                return Invalid("沉头直径必须大于主孔、夹角必须在 0..180 度开区间，锥段不得穿过孔底。");
            if (type == HoleTypes.Tapped &&
                (!string.Equals(hole.ThreadStandard, "ISO_METRIC", StringComparison.OrdinalIgnoreCase) ||
                 !Threads.TryGetValue(hole.ThreadSize ?? "", out var thread) || !Positive(hole.ThreadPitch) ||
                 Math.Abs(hole.ThreadPitch.GetValueOrDefault() - thread.Pitch) > 1e-9 || !Positive(hole.ThreadDepthMm) ||
                 !Positive(hole.TapDrillDiameterMm) || hole.TapDrillDiameterMm >= thread.Diameter ||
                 Math.Abs(hole.TapDrillDiameterMm.GetValueOrDefault() - thread.Drill) > 1e-9 ||
                 Math.Abs(hole.TapDrillDiameterMm.GetValueOrDefault() - diameter) > 1e-9 ||
                 depth.HasValue && hole.ThreadDepthMm > depth))
                return Invalid("攻丝孔需要受支持 ISO_METRIC 粗牙规格及匹配螺距；diameter_mm 必须等于底孔径且小于公称径，螺纹深度不得超孔深。");
            for (var i = 0; i < points.Length; i++)
                for (var j = i + 1; j < points.Length; j++)
                    if (Math.Sqrt(Math.Pow(points[i].Xmm - points[j].Xmm, 2) + Math.Pow(points[i].Ymm - points[j].Ymm, 2)) <= hole.OuterDiameterMm)
                        return Invalid("孔位重复、相交或外缘相切。");
            return new(hole, null, []);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or JsonException or KeyNotFoundException or InvalidOperationException)
        { return Invalid($"孔参数不能解析：{ex.Message}"); }
    }

    public HoleValidationResult Validate(FeatureDefinition feature, CADModelSpec spec)
    {
        if (!spec.Unit.Equals("mm", StringComparison.OrdinalIgnoreCase)) return Invalid("孔定义只支持 mm 单位。");
        var validation = Validate(feature);
        if (!validation.IsValid) return validation;
        var h = validation.Definition!;
        if (!TryResolveFace(feature, spec, h, out var face))
            return Fail(PartFamilyFailureStages.HoleReferenceFaceMissing, "reference_face 必须引用直接依赖的单一盲拉伸实体 start_face/end_face，且轮廓边界可证明。");
        var length = h.ThroughAll ? face!.Depth : h.DepthMm!.Value;
        if ((!h.ThroughAll && length >= face!.Depth) || h.CounterboreDepthMm >= face!.Depth ||
            h.CountersinkHeightMm >= face.Depth || h.ThreadDepthMm > Math.Min(length, face.Depth))
            return Invalid("盲孔必须保留孔底材料；沉孔、锥段或螺纹深度超出实体厚度。");
        var radius = h.OuterDiameterMm / 2;
        if (h.Position.Any(p => face.IsCircle
            ? Math.Sqrt(Math.Pow(p.Xmm - face.CenterX, 2) + Math.Pow(p.Ymm - face.CenterY, 2)) + radius >= face.Width / 2
            : Math.Abs(p.Xmm - face.CenterX) + radius >= face.Width / 2 || Math.Abs(p.Ymm - face.CenterY) + radius >= face.Height / 2))
            return Invalid("孔的完整外轮廓超出参考面材料边界或与边界相切。");
        if (h.PatternReference is not null)
        {
            var sketchId = h.PatternReference.StartsWith("sketch:", StringComparison.Ordinal) ? h.PatternReference[7..] : "";
            var placement = spec.Sketches.FirstOrDefault(s => s.SketchId == sketchId);
            if (placement is null || !feature.ReferencedSketches.Contains(sketchId) || placement.Entities.Count != h.Quantity || placement.Constraints.Count != 0)
                return Invalid("pattern_reference 必须为已声明引用的 sketch:<id>，其显式圆心点集必须等于 quantity/position。");
            var bossSketch = spec.Sketches.First(s => s.SketchId.Equals(spec.Features.First(f => f.FeatureId.Equals(h.ReferenceFace.Split(':')[0], StringComparison.OrdinalIgnoreCase)).SketchId, StringComparison.OrdinalIgnoreCase));
            if (!placement.ReferencePlane.Equals(bossSketch.ReferencePlane, StringComparison.OrdinalIgnoreCase))
                return Invalid("点集草图必须使用基础轮廓同一局部坐标平面。");
            var remaining = h.Position.ToList();
            foreach (var entity in placement.Entities)
            {
                if (entity.EntityType != SketchEntityTypes.Circle || !Read(entity.Parameters, "center_x_mm", out var x) ||
                    !Read(entity.Parameters, "center_y_mm", out var y) || !Read(entity.Parameters, "radius_mm", out var r) ||
                    Math.Abs(r * 2 - h.DiameterMm) > 1e-9) return Invalid("引用点集必须由与主孔径相符的圆组成。");
                var index = remaining.FindIndex(p => Math.Abs(p.Xmm - x) < 1e-9 && Math.Abs(p.Ymm - y) < 1e-9);
                if (index < 0) return Invalid("pattern_reference 与显式 position 不一致。");
                remaining.RemoveAt(index);
            }
        }
        foreach (var other in spec.Features.Where(f => f.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase) && !f.FeatureId.Equals(feature.FeatureId, StringComparison.OrdinalIgnoreCase)))
        {
            var otherResult = Validate(other);
            if (!otherResult.IsValid) return Invalid("同一实体包含不能解析的其他孔特征，无法证明材料范围。");
            var otherHole = otherResult.Definition!;
            if (otherHole.ReferenceFace.Split(':')[0] != h.ReferenceFace.Split(':')[0])
                return Invalid("孔所属实体引用不一致。");
            foreach (var a in h.Position)
                foreach (var b in otherHole.Position)
                    if (Math.Sqrt(Math.Pow(a.Xmm - b.Xmm, 2) + Math.Pow(a.Ymm - b.Ymm, 2)) <= (h.OuterDiameterMm + otherHole.OuterDiameterMm) / 2)
                        return Invalid("不同孔特征的外轮廓投影相交；对置面也必须保守拒绝未经证明的交叠。");
        }
        return validation;
    }

    public static bool TryResolveFace(FeatureDefinition feature, CADModelSpec spec, HoleFeatureDefinition hole, out HoleReferenceGeometry? face)
    {
        face = null;
        var parts = hole.ReferenceFace.Split(':');
        if (parts.Length != 2 || parts[1] is not ("start_face" or "end_face")) return false;
        var boss = spec.Features.FirstOrDefault(f => f.FeatureId.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (boss is null || !boss.FeatureType.Equals(FeatureTypes.ExtrudeBoss, StringComparison.OrdinalIgnoreCase) || !feature.DependsOn.Contains(boss.FeatureId, StringComparer.OrdinalIgnoreCase) ||
            boss.ReferencedSketches.Count != 1 || !Read(boss.Parameters, "depth_mm", out var depth) || depth <= 0 ||
            boss.Parameters.TryGetValue("direction", out var direction) && direction != "blind") return false;
        // 仅一个基础实体和直接孔特征：复杂布尔/阵列后的材料范围必须重新证明，不能按原包围盒放行。
        if (spec.Features.Any(f => !f.FeatureId.Equals(boss.FeatureId, StringComparison.OrdinalIgnoreCase) && !f.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase))) return false;
        var sketch = spec.Sketches.FirstOrDefault(s => s.SketchId.Equals(boss.SketchId, StringComparison.OrdinalIgnoreCase));
        if (sketch is null || sketch.Entities.Count != 1 || sketch.Constraints.Count != 0 || sketch.Dimensions.Count != 0) return false;
        if (!new[] { "TopPlane", "FrontPlane", "RightPlane" }.Contains(sketch.ReferencePlane, StringComparer.OrdinalIgnoreCase)) return false;
        var entity = sketch.Entities[0]; var p = entity.Parameters;
        if (entity.Construction || p.Keys.Any(k => !new[] { "center_x_mm", "center_y_mm", "length_mm", "width_mm", "height_mm", "radius_mm", "diameter_mm" }.Contains(k, StringComparer.OrdinalIgnoreCase))) return false;
        if (!ReadOptional(p, "center_x_mm", out var x) || !ReadOptional(p, "center_y_mm", out var y)) return false;
        if (p.ContainsKey("length_mm") && p.ContainsKey("width_mm") && p.ContainsKey("height_mm")) return false;
        if (entity.EntityType.Equals(SketchEntityTypes.Rectangle, StringComparison.OrdinalIgnoreCase) &&
            Read(p, p.ContainsKey("length_mm") ? "length_mm" : "width_mm", out var w) &&
            Read(p, p.ContainsKey("height_mm") ? "height_mm" : "width_mm", out var ht) && w > 0 && ht > 0)
            face = new(w, ht, depth, x, y, false);
        else if (entity.EntityType.Equals(SketchEntityTypes.Circle, StringComparison.OrdinalIgnoreCase) && Read(p, "radius_mm", out var r) && r > 0 && !p.ContainsKey("diameter_mm"))
            face = new(r * 2, r * 2, depth, x, y, true);
        return face is not null;
    }
    private static bool Read(IReadOnlyDictionary<string, string> p, string name, out double value) =>
        double.TryParse(p.GetValueOrDefault(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static bool ReadOptional(IReadOnlyDictionary<string, string> p, string name, out double value)
    { value = 0; return !p.ContainsKey(name) || Read(p, name, out value); }
    private static bool Positive(double? value) => value.HasValue && double.IsFinite(value.Value) && value > 0;
    private static HoleValidationResult Invalid(string detail) => Fail(PartFamilyFailureStages.InvalidHoleParameter, detail);
    private static HoleValidationResult Fail(string stage, string detail) => new(null, stage, [$"{stage}: {detail}"]);
}

public sealed record HoleReferenceGeometry(double Width, double Height, double Depth, double CenterX, double CenterY, bool IsCircle);
