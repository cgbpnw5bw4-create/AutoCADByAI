namespace DomainSchemas;

// 仅存放独立 GeometryReader 读回的数值，不能从请求或非空 IFeature 构造成功快照。
public sealed record MeasuredHole(
    string FeatureId, string FeatureName, string HoleType, string ReferenceFace, HolePosition Position,
    double DiameterMm, double DepthMm, bool ThroughAll,
    double? CounterboreDiameterMm = null, double? CounterboreDepthMm = null,
    double? CountersinkDiameterMm = null, double? CountersinkAngleDeg = null,
    bool CoaxialStepsVerified = false, bool ConeSurfaceVerified = false,
    MeasuredTappedHole? Tapped = null);

public sealed record MeasuredTappedHole(
    string Representation, string ThreadStandard, string ThreadSize, double ThreadPitch,
    double ThreadDepthMm, double TapDrillDiameterMm, bool CosmeticThreadPresent, bool ModeledThreadPresent);

public sealed record HoleReportedDefinition(string FeatureId, string HoleType, IReadOnlyDictionary<string, string> Parameters);

public static class HoleGeometryValidation
{
    public static bool ReportDefinitionsMatch(CADModelSpec spec, IReadOnlyList<HoleReportedDefinition> reports)
    {
        var features = spec.Features.Where(IsExplicitHole).ToArray();
        if (features.Length == 0 || reports.Count != features.Length || reports.Select(r => r.FeatureId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != reports.Count)
            return false;
        foreach (var feature in features)
        {
            var match = reports.SingleOrDefault(r => r.FeatureId.Equals(feature.FeatureId, StringComparison.OrdinalIgnoreCase));
            var definition = new HoleValidator().Validate(feature).Definition;
            if (match is null || definition is null || match.HoleType != definition.HoleType) return false;
            var reported = new HoleValidator().Validate(new FeatureDefinition(feature.FeatureId, FeatureTypes.Hole, match.Parameters)).Definition;
            if (reported is null || !definition.ToParameters().OrderBy(p => p.Key, StringComparer.Ordinal)
                .SequenceEqual(reported.ToParameters().OrderBy(p => p.Key, StringComparer.Ordinal))) return false;
        }
        return true;
    }

    public static bool IsExplicitHole(FeatureDefinition feature) =>
        feature.FeatureType.Equals(FeatureTypes.Hole, StringComparison.OrdinalIgnoreCase) && feature.Parameters.ContainsKey("hole_type");

    public static void Validate(CADModelSpec spec, MeasuredGeometry measured,
        IReadOnlyList<string>? executedTypes, List<GeometryDeviation> deviations, List<string> passed,
        Action<string, string, string> fail)
    {
        var definitions = spec.Features.Where(IsExplicitHole).ToArray();
        if (definitions.Length == 0) return;
        var holes = measured.Holes;
        void Check(bool valid, string key, string detail, double? expected = null, double? actual = null)
        {
            deviations.Add(new(key, expected, actual, GeometryValidator.DimensionToleranceMm, valid, detail));
            if (valid) passed.Add(key); else fail(PartFamilyFailureStages.HoleGeometryValidationFailed, key, detail);
        }
        if (holes is null)
        {
            Check(false, "hole_measurements", "缺少独立逐孔实测值；非空 Feature、圆柱面数量或请求回显不能证明孔几何。");
            return;
        }
        var parsed = definitions.Select(f => (Feature: f, Result: new HoleValidator().Validate(f, spec))).ToArray();
        if (parsed.Any(p => !p.Result.IsValid))
        { Check(false, "hole_definition", "输入孔定义或引用不能验证。"); return; }
        Check(holes.Count == parsed.Sum(p => p.Result.Definition!.Quantity), "hole_count", "逐孔实测总数必须精确等于定义总数。",
            parsed.Sum(p => p.Result.Definition!.Quantity), holes.Count);
        Check(executedTypes?.Contains(FeatureTypes.Hole, StringComparer.OrdinalIgnoreCase) == true,
            "hole_handler_executed", "孔 Handler 必须在同次执行报告中存在。");
        foreach (var item in parsed)
        {
            var h = item.Result.Definition!; var id = item.Feature.FeatureId;
            var actuals = holes.Where(m => m.FeatureId.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            Check(actuals.Count == h.Quantity, $"hole_count:{id}", "不得用其他孔特征的读数补足当前孔数量。", h.Quantity, actuals.Count);
            HoleValidator.TryResolveFace(item.Feature, spec, h, out var face);
            var depth = h.ThroughAll ? face!.Depth : h.DepthMm!.Value;
            foreach (var position in h.Position)
            {
                var found = actuals.FindIndex(m => Near(m.Position.Xmm, position.Xmm) && Near(m.Position.Ymm, position.Ymm) && Near(m.Position.Zmm, position.Zmm));
                if (found < 0) { Check(false, $"hole_position:{id}", "缺少该实际孔位或实测孔位重复。"); continue; }
                var m = actuals[found]; actuals.RemoveAt(found);
                Check(string.Equals(m.HoleType, h.HoleType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.ReferenceFace, h.ReferenceFace, StringComparison.OrdinalIgnoreCase), $"hole_type_reference:{id}", "孔型与解析面必须一致。");
                Check(measured.Features?.Any(f => f.Name == m.FeatureName && f.ErrorCode == 0) == true,
                    $"hole_feature_exists:{id}", "同次实测特征树必须包含孔特征且无错误。");
                Check(Near(m.DiameterMm, h.DiameterMm, GeometryValidator.DiameterToleranceMm), $"hole_diameter:{id}", "实测主孔径。", h.DiameterMm, m.DiameterMm);
                Check(Near(m.DepthMm, depth) && m.ThroughAll == h.ThroughAll, $"hole_depth:{id}", "实测孔深与终止条件。", depth, m.DepthMm);
                if (h.HoleType == HoleTypes.Counterbore)
                {
                    Check(m.CoaxialStepsVerified && Near(m.CounterboreDiameterMm, h.CounterboreDiameterMm) && Near(m.CounterboreDepthMm, h.CounterboreDepthMm),
                        $"counterbore_geometry:{id}", "必须证明同轴台阶圆柱、沉孔径及深度。");
                }
                if (h.HoleType == HoleTypes.Countersink)
                {
                    Check(m.ConeSurfaceVerified && Near(m.CountersinkDiameterMm, h.CountersinkDiameterMm) && Near(m.CountersinkAngleDeg, h.CountersinkAngleDeg),
                        $"countersink_geometry:{id}", "必须证明锥面、入口直径和全角。");
                }
                if (h.HoleType == HoleTypes.Tapped)
                {
                    var t = m.Tapped;
                    Check(t is not null && t.Representation == "hole_wizard_tapped" && t.CosmeticThreadPresent && !t.ModeledThreadPresent &&
                        measured.Features?.Any(f => f.Name == m.FeatureName && f.TypeName.Equals("HoleWzd", StringComparison.OrdinalIgnoreCase)) == true &&
                        string.Equals(t.ThreadStandard, h.ThreadStandard, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(t.ThreadSize, h.ThreadSize, StringComparison.OrdinalIgnoreCase) &&
                        Near(t.ThreadPitch, h.ThreadPitch, 1e-6) && Near(t.ThreadDepthMm, h.ThreadDepthMm) && Near(t.TapDrillDiameterMm, h.TapDrillDiameterMm),
                        $"tapped_hole_metadata:{id}", "需要 Hole Wizard 攻丝特征及标准/规格/螺距/深度/底径读回；普通 Cut、仅 Cosmetic Thread 和真实螺旋建模螺纹不可混同。");
                }
            }
        }
    }

    public static double RemovedVolume(HoleFeatureDefinition h, double materialDepth)
    {
        var depth = h.ThroughAll ? materialDepth : h.DepthMm!.Value;
        var r = h.DiameterMm / 2;
        var volume = Math.PI * r * r * depth;
        if (h.HoleType == HoleTypes.Counterbore)
            volume += Math.PI * (Math.Pow(h.CounterboreDiameterMm!.Value / 2, 2) - r * r) * h.CounterboreDepthMm!.Value;
        if (h.HoleType == HoleTypes.Countersink)
        {
            var outer = h.CountersinkDiameterMm!.Value / 2;
            volume += Math.PI * h.CountersinkHeightMm * (outer * outer + outer * r - 2 * r * r) / 3;
        }
        // 最低攻丝合同是底孔 + Hole Wizard 元数据 + Cosmetic Thread，无螺旋切除体积。
        return volume * h.Quantity;
    }
    private static bool Near(double? a, double? b, double tolerance = GeometryValidator.DimensionToleranceMm) =>
        a.HasValue && b.HasValue && double.IsFinite(a.Value) && double.IsFinite(b.Value) && Math.Abs(a.Value - b.Value) <= tolerance;
}
