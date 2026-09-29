namespace DomainSchemas;

/// <summary>对真实圆柱面、圆边及极值包络进行独立验收，不读取 COM 或使用建模返回参数。</summary>
public static class AxialSectionGeometryValidator
{
    public const double PositionToleranceMm = 0.05d;
    public const double DiameterToleranceMm = 0.02d;

    public static IReadOnlyList<string> Validate(IReadOnlyList<AxialSectionGeometry> input, MeasuredGeometry measured)
    {
        var issues = new List<string>();
        var sections = new List<AxialSectionGeometry>();
        foreach (var section in input)
        {
            if (!new[] { section.StartMm, section.EndMm, section.OuterDiameterMm, section.InnerDiameterMm }.All(double.IsFinite) ||
                section.StartMm < 0 || section.EndMm <= section.StartMm || section.OuterDiameterMm <= section.InnerDiameterMm ||
                section.InnerDiameterMm < 0 || Math.Abs(section.StartMm - (sections.LastOrDefault()?.EndMm ?? 0)) > 1e-9)
                return ["轴向截面定义无效或不连续。"];
            if (sections.Count > 0 && sections[^1].OuterDiameterMm == section.OuterDiameterMm && sections[^1].InnerDiameterMm == section.InnerDiameterMm)
                sections[^1] = sections[^1] with { EndMm = section.EndMm };
            else sections.Add(section);
        }
        if (sections.Count == 0 || !measured.RebuildPassed || measured.BodyCount != 1 || measured.ExactExtents is null ||
            measured.Cylinders is not { Count: > 0 } || measured.Edges is not { Count: > 0 })
            return ["缺少成功重建、单实体、精确包络、圆柱面或圆边实测数据。"];

        var first = measured.Cylinders[0];
        var firstAxis = new[] { first.AxisX, first.AxisY, first.AxisZ };
        var axis = Array.IndexOf(firstAxis.Select(Math.Abs).ToArray(), firstAxis.Max(Math.Abs));
        if (!Parallel(firstAxis, axis)) return ["圆柱轴线不是已验证的标准基准主轴。"];
        var box = measured.ExactExtents;
        var minimum = new[] { box.MinXmm, box.MinYmm, box.MinZmm };
        var maximum = new[] { box.MaxXmm, box.MaxYmm, box.MaxZmm };
        var length = sections[^1].EndMm;
        var outer = sections.Max(s => s.OuterDiameterMm);
        if (!minimum.Concat(maximum).All(double.IsFinite)) return ["精确包络含非有限数。"];
        var positive = Near(minimum[axis], 0, PositionToleranceMm) && Near(maximum[axis], length, PositionToleranceMm);
        var negative = Near(maximum[axis], 0, PositionToleranceMm) && Near(minimum[axis], -length, PositionToleranceMm);
        if (!positive && !negative) issues.Add("轴向原点或总长度不符合参数。");
        for (var i = 0; i < 3; i++)
            if (i != axis && (!Near(minimum[i], -outer / 2, PositionToleranceMm) || !Near(maximum[i], outer / 2, PositionToleranceMm)))
                issues.Add("径向精确包络不符合最大直径及原点轴线。");

        var diameters = sections.SelectMany(s => s.InnerDiameterMm > 0 ? new[] { s.OuterDiameterMm, s.InnerDiameterMm } : new[] { s.OuterDiameterMm }).Distinct().ToArray();
        foreach (var cylinder in measured.Cylinders)
        {
            var origin = new[] { cylinder.OriginXmm, cylinder.OriginYmm, cylinder.OriginZmm };
            if (!origin.All(double.IsFinite) || !Parallel([cylinder.AxisX, cylinder.AxisY, cylinder.AxisZ], axis) ||
                origin.Where((_, i) => i != axis).Any(v => Math.Abs(v) > PositionToleranceMm) ||
                !diameters.Any(d => Near(d, cylinder.DiameterMm, DiameterToleranceMm)))
                issues.Add("存在偏心、方向错误、未知直径或非有限圆柱面。");
        }
        foreach (var diameter in diameters)
            if (!measured.Cylinders.Any(c => Near(c.DiameterMm, diameter, DiameterToleranceMm))) issues.Add($"缺少直径 {diameter:R} 的圆柱面。");

        var expectedCircles = sections.SelectMany(s =>
            (s.InnerDiameterMm > 0 ? new[] { s.OuterDiameterMm, s.InnerDiameterMm } : new[] { s.OuterDiameterMm })
                .SelectMany(d => new[] { (Position: s.StartMm, Diameter: d), (Position: s.EndMm, Diameter: d) })).Distinct().ToArray();
        var actualCircles = new List<(double Position, double Diameter)>();
        foreach (var edge in measured.Edges)
        {
            if (!edge.Kind.Equals(EdgeKinds.Circle, StringComparison.OrdinalIgnoreCase))
            {
                if (!edge.Kind.Equals(EdgeKinds.Line, StringComparison.OrdinalIgnoreCase)) issues.Add("出现受限轴向截面之外的非圆非直线边。");
                continue;
            }
            var point = new[] { edge.AnchorXMm, edge.AnchorYMm, edge.AnchorZMm };
            if (!point.All(double.IsFinite) || edge.RadiusMm is not > 0 || !double.IsFinite(edge.RadiusMm.Value) ||
                edge.Direction is null || !Parallel([edge.Direction.X, edge.Direction.Y, edge.Direction.Z], axis) ||
                point.Where((_, i) => i != axis).Any(v => Math.Abs(v) > PositionToleranceMm) ||
                !edge.AdjacentSurfaceKinds.Contains(SurfaceKinds.Cylinder) || !edge.AdjacentSurfaceKinds.Contains(SurfaceKinds.Plane))
            {
                issues.Add("圆口或台阶边缺少同轴、法向、圆柱面与端平面证据。");
                continue;
            }
            actualCircles.Add((point[axis] * (positive ? 1 : -1), edge.RadiusMm.Value * 2));
        }
        bool Matches((double Position, double Diameter) a, (double Position, double Diameter) b) =>
            Near(a.Position, b.Position, PositionToleranceMm) && Near(a.Diameter, b.Diameter, DiameterToleranceMm);
        if (expectedCircles.Any(e => !actualCircles.Any(a => Matches(e, a))) || actualCircles.Any(a => !expectedCircles.Any(e => Matches(e, a))))
            issues.Add("端口或台阶的轴向位置、直径集合不匹配；可能存在盲孔、错位或错误台阶次序。");
        return issues.Distinct().ToArray();
    }

    private static bool Near(double a, double b, double tolerance) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) <= tolerance;
    private static bool Parallel(double[] direction, int axis)
    {
        if (!direction.All(double.IsFinite)) return false;
        var norm = Math.Sqrt(direction.Sum(v => v * v));
        return double.IsFinite(norm) && norm > 0 && Math.Abs(direction[axis]) / norm > 0.999999 &&
               direction.Where((_, i) => i != axis).All(v => Math.Abs(v) / norm < 1e-6);
    }
}
