using System.Globalization;

namespace DomainSchemas;

/// <summary>
/// 曲面类别。用于按相邻面性质区分边，例如"孔口"是圆柱面与平面的交线。
/// </summary>
public static class SurfaceKinds
{
    public const string Plane = "plane";
    public const string Cylinder = "cylinder";
    public const string Cone = "cone";
    public const string Sphere = "sphere";
    public const string Other = "other";
}

public static class EdgeKinds
{
    public const string Line = "line";
    public const string Circle = "circle";
    public const string Bcurve = "bcurve";
    public const string Other = "other";
}

/// <summary>
/// 一条实测边。由 GeometryReader 从真实模型枚举得到，纯数据，不含 COM 对象。
/// <para>
/// 定位点 <see cref="AnchorXMm"/> 等：开放边取两端点中点，闭合圆边取圆心。
/// 这是因为闭合圆边没有起止顶点（本机 SOLIDWORKS 2023 探查确认）。
/// </para>
/// </summary>
public sealed record MeasuredEdge(
    int Index,
    string Kind,
    double LengthMm,
    double AnchorXMm,
    double AnchorYMm,
    double AnchorZMm,
    double? RadiusMm,
    IReadOnlyList<string> AdjacentSurfaceKinds,
    EdgeDirection? Direction = null);

/// <summary>
/// 一条边所定义的方向，单位向量，模型坐标系。
/// <para>
/// 直线边取端点连线方向；圆边取圆所在平面的法向，也就是它的轴。
/// 两者是同一个概念的两种来源——"这条边指向哪儿"——因此共用一个字段：
/// 线性阵列要的方向和圆周阵列要的轴，都可以由同一套边判据解出来，
/// 不必为每种特征再造一套引用模型。
/// </para>
/// <para>
/// 方向来自当次拓扑，正负可能随建模变化。执行器须将它与声明方向做有符号
/// 点积比较，再设置 API 的翻转选项；边方向与 API 默认方向的映射须经真机取证。
/// </para>
/// </summary>
public sealed record EdgeDirection(double X, double Y, double Z);

/// <summary>主轴方向和位置的纯规则；不读取 CAD，也不授予 API 执行能力。</summary>
public static class PrincipalAxisRules
{
    private const double ParallelCosine = 0.999d;

    public static bool TryParse(string? text, out EdgeDirection axis)
    {
        axis = text?.ToLowerInvariant() switch
        {
            "x" or "+x" => new(1, 0, 0),
            "-x" => new(-1, 0, 0),
            "y" or "+y" => new(0, 1, 0),
            "-y" => new(0, -1, 0),
            "z" or "+z" => new(0, 0, 1),
            "-z" => new(0, 0, -1),
            _ => new(0, 0, 0)
        };
        return axis.X != 0 || axis.Y != 0 || axis.Z != 0;
    }

    public static bool TryResolveFlip(EdgeDirection? measured, string? declaredAxis, out bool flipDirection)
    {
        flipDirection = false;
        if (measured is null || !TryParse(declaredAxis, out var declared) ||
            !double.IsFinite(measured.X) || !double.IsFinite(measured.Y) || !double.IsFinite(measured.Z)) return false;

        // 先缩放再归一化，避免有限大向量平方溢出后被误判为有效方向。
        var scale = Math.Max(Math.Abs(measured.X), Math.Max(Math.Abs(measured.Y), Math.Abs(measured.Z)));
        if (scale == 0) return false;
        var x = measured.X / scale;
        var y = measured.Y / scale;
        var z = measured.Z / scale;
        var length = Math.Sqrt(x * x + y * y + z * z);
        var signedCosine = (x * declared.X + y * declared.Y + z * declared.Z) / length;
        if (!double.IsFinite(signedCosine) || Math.Abs(signedCosine) < ParallelCosine) return false;
        flipDirection = signedCosine < 0;
        return true;
    }

    public static bool HasExplicitOriginAxisPosition(EdgeSelectionCriteria criteria, EdgeDirection axis) =>
        IsPrincipalAxis(axis) &&
        (axis.X != 0 || criteria.AnchorXMm == 0d) &&
        (axis.Y != 0 || criteria.AnchorYMm == 0d) &&
        (axis.Z != 0 || criteria.AnchorZMm == 0d);

    public static bool IsOnOriginAxis(MeasuredEdge edge, EdgeDirection axis, double toleranceMm) =>
        IsPrincipalAxis(axis) &&
        double.IsFinite(toleranceMm) && toleranceMm > 0 &&
        double.IsFinite(edge.AnchorXMm) && double.IsFinite(edge.AnchorYMm) && double.IsFinite(edge.AnchorZMm) &&
        // 用户可收紧选择容差，但不能扩大当前主轴同轴复核的 0.05 mm 上限。
        (axis.X != 0 || Math.Abs(edge.AnchorXMm) <= Math.Min(toleranceMm, 0.05d)) &&
        (axis.Y != 0 || Math.Abs(edge.AnchorYMm) <= Math.Min(toleranceMm, 0.05d)) &&
        (axis.Z != 0 || Math.Abs(edge.AnchorZMm) <= Math.Min(toleranceMm, 0.05d));

    private static bool IsPrincipalAxis(EdgeDirection axis) =>
        (Math.Abs(axis.X) == 1 && axis.Y == 0 && axis.Z == 0) ||
        (Math.Abs(axis.Y) == 1 && axis.X == 0 && axis.Z == 0) ||
        (Math.Abs(axis.Z) == 1 && axis.X == 0 && axis.Y == 0);
}

/// <summary>
/// 声明式边选择判据。零件族在 FeatureGraph 中声明"要哪条边"，
/// 执行时由 <see cref="EdgeSelectionResolver"/> 对实测拓扑求解。
/// <para>
/// 之所以不用 SolidWorks 自动生成的名称（如 <c>Edge1@Boss-Extrude1</c>），
/// 是因为那类名称在特征树变化后会漂移；也不用 <c>IEdge.GetID()</c>，
/// 因为本机探查确认它对所有边都返回 0，不具区分度。
/// </para>
/// </summary>
public sealed record EdgeSelectionCriteria(
    string? Kind = null,
    double? LengthMm = null,
    double? RadiusMm = null,
    IReadOnlyList<string>? AdjacentSurfaceKinds = null,
    double? AnchorXMm = null,
    double? AnchorYMm = null,
    double? AnchorZMm = null,
    double ToleranceMm = 0.05d,
    int ExpectedCount = 1);

public sealed record EdgeSelectionResult(
    bool IsResolved,
    string? FailureStage,
    IReadOnlyList<string> Issues,
    IReadOnlyList<MeasuredEdge> Matches)
{
    public static EdgeSelectionResult Resolved(IReadOnlyList<MeasuredEdge> matches) =>
        new(true, null, Array.Empty<string>(), matches);

    public static EdgeSelectionResult Failed(
        string stage,
        IReadOnlyList<MeasuredEdge> matches,
        params string[] issues) =>
        new(false, stage, issues, matches);
}

/// <summary>
/// 把声明式判据解析为确定的实测边集合。
/// <para>
/// <b>核心安全性质：匹配数不等于声明的 ExpectedCount 时一律 fail-closed。</b>
/// SolidWorks 的 Fillet / Chamfer / Pattern / Mirror 都只作用于"当前选择集"，
/// 选错边时 API 依然返回非空 IFeature，产出的是特征打在错误位置的零件。
/// 因此宁可拒绝执行，也不允许在选择不确定的情况下继续。
/// </para>
/// </summary>
public static class EdgeSelectionResolver
{
    public static EdgeSelectionResult Resolve(
        IReadOnlyList<MeasuredEdge> edges,
        EdgeSelectionCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.ExpectedCount < 1)
        {
            return EdgeSelectionResult.Failed(
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                Array.Empty<MeasuredEdge>(),
                "expected_count must be at least 1.");
        }

        if (criteria.ToleranceMm <= 0d || !double.IsFinite(criteria.ToleranceMm))
        {
            return EdgeSelectionResult.Failed(
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                Array.Empty<MeasuredEdge>(),
                "tolerance_mm must be a finite positive number.");
        }

        if (new[] { criteria.LengthMm, criteria.RadiusMm, criteria.AnchorXMm, criteria.AnchorYMm, criteria.AnchorZMm }
            .Any(value => value is { } number && !double.IsFinite(number)))
        {
            return EdgeSelectionResult.Failed(PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                Array.Empty<MeasuredEdge>(), "边选择的尺寸和定位坐标必须是有限数值。");
        }

        if (!HasAnyConstraint(criteria))
        {
            // 无约束判据会匹配全部边，等于放任选择集，必须拒绝。
            return EdgeSelectionResult.Failed(
                PartFamilyFailureStages.EdgeSelectionInvalidCriteria,
                Array.Empty<MeasuredEdge>(),
                "edge selection criteria must constrain at least one property.");
        }

        var matches = edges.Where(edge => Matches(edge, criteria)).ToArray();
        if (matches.Length == 0)
        {
            return EdgeSelectionResult.Failed(
                PartFamilyFailureStages.EdgeSelectionNotFound,
                matches,
                $"no edge matches the declared criteria ({Describe(criteria)}).");
        }

        if (matches.Length != criteria.ExpectedCount)
        {
            return EdgeSelectionResult.Failed(
                PartFamilyFailureStages.EdgeSelectionAmbiguous,
                matches,
                $"criteria ({Describe(criteria)}) matched {matches.Length} edges but declared " +
                $"expected_count={criteria.ExpectedCount}; refusing to guess.");
        }

        return EdgeSelectionResult.Resolved(matches);
    }

    private static bool HasAnyConstraint(EdgeSelectionCriteria criteria) =>
        !string.IsNullOrWhiteSpace(criteria.Kind) ||
        criteria.LengthMm is not null ||
        criteria.RadiusMm is not null ||
        criteria.AnchorXMm is not null ||
        criteria.AnchorYMm is not null ||
        criteria.AnchorZMm is not null ||
        (criteria.AdjacentSurfaceKinds?.Count ?? 0) > 0;

    private static bool Matches(MeasuredEdge edge, EdgeSelectionCriteria criteria)
    {
        if (!double.IsFinite(edge.LengthMm) || !double.IsFinite(edge.AnchorXMm) ||
            !double.IsFinite(edge.AnchorYMm) || !double.IsFinite(edge.AnchorZMm) ||
            (edge.RadiusMm is { } radiusValue && !double.IsFinite(radiusValue))) return false;

        if (!string.IsNullOrWhiteSpace(criteria.Kind) &&
            !edge.Kind.Equals(criteria.Kind, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (criteria.LengthMm is { } length &&
            Math.Abs(edge.LengthMm - length) > criteria.ToleranceMm)
        {
            return false;
        }

        if (criteria.RadiusMm is { } radius &&
            (edge.RadiusMm is null || Math.Abs(edge.RadiusMm.Value - radius) > criteria.ToleranceMm))
        {
            return false;
        }

        if (criteria.AnchorXMm is { } x && Math.Abs(edge.AnchorXMm - x) > criteria.ToleranceMm)
        {
            return false;
        }

        if (criteria.AnchorYMm is { } y && Math.Abs(edge.AnchorYMm - y) > criteria.ToleranceMm)
        {
            return false;
        }

        if (criteria.AnchorZMm is { } z && Math.Abs(edge.AnchorZMm - z) > criteria.ToleranceMm)
        {
            return false;
        }

        if (criteria.AdjacentSurfaceKinds is { Count: > 0 } required)
        {
            // 相邻面无序比较：cylinder+plane 与 plane+cylinder 视为同一种边。
            var actual = edge.AdjacentSurfaceKinds
                .Select(kind => kind.ToLowerInvariant())
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expected = required
                .Select(kind => kind.ToLowerInvariant())
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string Describe(EdgeSelectionCriteria criteria)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(criteria.Kind)) parts.Add($"kind={criteria.Kind}");
        if (criteria.LengthMm is { } l) parts.Add($"length_mm={l.ToString("R", CultureInfo.InvariantCulture)}");
        if (criteria.RadiusMm is { } r) parts.Add($"radius_mm={r.ToString("R", CultureInfo.InvariantCulture)}");
        if (criteria.AnchorXMm is { } x) parts.Add($"anchor_x_mm={x.ToString("R", CultureInfo.InvariantCulture)}");
        if (criteria.AnchorYMm is { } y) parts.Add($"anchor_y_mm={y.ToString("R", CultureInfo.InvariantCulture)}");
        if (criteria.AnchorZMm is { } z) parts.Add($"anchor_z_mm={z.ToString("R", CultureInfo.InvariantCulture)}");
        if (criteria.AdjacentSurfaceKinds is { Count: > 0 } faces)
        {
            parts.Add($"adjacent_surfaces={string.Join("+", faces)}");
        }

        return string.Join("; ", parts);
    }
}
