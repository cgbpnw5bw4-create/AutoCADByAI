using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// 一条实测边及其 COM 句柄。
/// <para>
/// GeometryReader 只需要 <see cref="Measured"/> 这份纯数据；
/// FeatureAdapter 还需要 <see cref="ComEdge"/> 才能调用 Select4 选中它。
/// 两者共用同一次枚举，避免出现"读取器看到的边"与"实际选中的边"不一致。
/// </para>
/// </summary>
internal sealed record EnumeratedEdge(MeasuredEdge Measured, object ComEdge);

/// <summary>
/// 实体边枚举。测量逻辑只有这一份，读取器与适配器都走它。
/// <para>
/// 两条来自本机 SOLIDWORKS 2023 真机探查的设计约束：
/// </para>
/// <list type="bullet">
/// <item><c>IEdge.GetID()</c> 对所有边返回 0，不具区分度，因此不用于识别。</item>
/// <item>闭合圆边没有起止顶点，因此定位点对圆边取圆心、对直线边取两端点中点。</item>
/// </list>
/// </summary>
internal static class SolidWorksEdgeEnumerator
{
    private const double MetersToMillimeters = 1000d;

    public static IReadOnlyList<EnumeratedEdge> Enumerate(
        ISolidWorksComFacade com,
        IReadOnlyList<object> bodies)
    {
        ArgumentNullException.ThrowIfNull(com);
        ArgumentNullException.ThrowIfNull(bodies);

        var results = new List<EnumeratedEdge>();
        var index = 0;
        foreach (var body in bodies)
        {
            var raw = com.TryInvoke(body, "GetEdges");
            if (raw is null)
            {
                continue;
            }

            var items = raw is Array array
                ? array.Cast<object?>().Where(value => value is not null).Cast<object>().ToArray()
                : [raw];
            foreach (var edge in items)
            {
                var measured = TryMeasure(com, edge, index);
                if (measured is not null)
                {
                    results.Add(new EnumeratedEdge(measured, edge));
                    index++;
                }
            }
        }

        return results;
    }

    /// <summary>枚举当前模型的全部实体边。失败时返回空集合，不抛异常。</summary>
    public static IReadOnlyList<EnumeratedEdge> EnumerateModel(ISolidWorksComFacade com, object model)
    {
        ArgumentNullException.ThrowIfNull(com);
        ArgumentNullException.ThrowIfNull(model);

        var raw = com.TryInvoke(model, "GetBodies2", 0, false);
        if (raw is null)
        {
            return Array.Empty<EnumeratedEdge>();
        }

        var bodies = raw is Array array
            ? array.Cast<object?>().Where(value => value is not null).Cast<object>().ToArray()
            : [raw];
        return Enumerate(com, bodies);
    }

    private static MeasuredEdge? TryMeasure(ISolidWorksComFacade com, object edge, int index)
    {
        var curve = com.TryInvoke(edge, "GetCurve");
        if (curve is null)
        {
            return null;
        }

        var isLine = com.TryInvokeBool(curve, "IsLine");
        var isCircle = com.TryInvokeBool(curve, "IsCircle");
        var isBcurve = com.TryInvokeBool(curve, "IsBcurve");
        var kind = isLine ? EdgeKinds.Line
            : isCircle ? EdgeKinds.Circle
            : isBcurve ? EdgeKinds.Bcurve
            : EdgeKinds.Other;

        double lengthMm;
        double anchorX;
        double anchorY;
        double anchorZ;
        double? radiusMm = null;
        EdgeDirection? direction = null;

        if (isCircle)
        {
            if (com.TryGetProperty(curve, "CircleParams") is not Array circle || circle.Length < 7)
            {
                return null;
            }

            anchorX = ToDouble(circle.GetValue(0)) * MetersToMillimeters;
            anchorY = ToDouble(circle.GetValue(1)) * MetersToMillimeters;
            anchorZ = ToDouble(circle.GetValue(2)) * MetersToMillimeters;

            // CircleParams 是 7 个 double：0-2 圆心、3-5 轴方向、6 半径。
            // 轴方向以前被丢掉了；圆周阵列要的"轴"就在这里，不必另建模型。
            direction = Normalize(
                ToDouble(circle.GetValue(3)),
                ToDouble(circle.GetValue(4)),
                ToDouble(circle.GetValue(5)));

            var radiusMeters = ToDouble(circle.GetValue(6));
            if (radiusMeters <= 0d)
            {
                return null;
            }

            radiusMm = radiusMeters * MetersToMillimeters;
            lengthMm = 2d * Math.PI * radiusMm.Value;
        }
        else
        {
            var start = ReadVertexPoint(com, edge, "GetStartVertex");
            var end = ReadVertexPoint(com, edge, "GetEndVertex");
            if (start is null || end is null)
            {
                return null;
            }

            anchorX = (start[0] + end[0]) / 2d * MetersToMillimeters;
            anchorY = (start[1] + end[1]) / 2d * MetersToMillimeters;
            anchorZ = (start[2] + end[2]) / 2d * MetersToMillimeters;
            lengthMm = Math.Sqrt(
                Math.Pow(end[0] - start[0], 2d) +
                Math.Pow(end[1] - start[1], 2d) +
                Math.Pow(end[2] - start[2], 2d)) * MetersToMillimeters;

            // 直线边的方向就是端点连线；线性阵列要的"方向"由此而来。
            direction = Normalize(
                end[0] - start[0],
                end[1] - start[1],
                end[2] - start[2]);
        }

        return new MeasuredEdge(
            index,
            kind,
            lengthMm,
            anchorX,
            anchorY,
            anchorZ,
            radiusMm,
            ReadAdjacentSurfaceKinds(com, edge),
            direction);
    }

    /// <summary>
    /// 归一化为单位向量。长度为零时返回 null——零向量不是方向，
    /// 与其返回一个看起来合法的 (0,0,0) 让下游误用，不如显式地没有。
    /// </summary>
    private static EdgeDirection? Normalize(double x, double y, double z)
    {
        var length = Math.Sqrt((x * x) + (y * y) + (z * z));
        if (!double.IsFinite(length) || length <= 1e-12d)
        {
            return null;
        }

        return new EdgeDirection(x / length, y / length, z / length);
    }

    private static double[]? ReadVertexPoint(ISolidWorksComFacade com, object edge, string accessor)
    {
        var vertex = com.TryInvoke(edge, accessor);
        if (vertex is null)
        {
            return null;
        }

        if (com.TryInvoke(vertex, "GetPoint") is not Array point || point.Length < 3)
        {
            return null;
        }

        return
        [
            ToDouble(point.GetValue(0)),
            ToDouble(point.GetValue(1)),
            ToDouble(point.GetValue(2))
        ];
    }

    private static IReadOnlyList<string> ReadAdjacentSurfaceKinds(ISolidWorksComFacade com, object edge)
    {
        if (com.TryInvoke(edge, "GetTwoAdjacentFaces") is not Array faces)
        {
            return Array.Empty<string>();
        }

        var kinds = new List<string>();
        foreach (var faceObject in faces)
        {
            if (faceObject is null)
            {
                continue;
            }

            var surface = com.TryInvoke(faceObject, "GetSurface");
            if (surface is null)
            {
                kinds.Add(SurfaceKinds.Other);
                continue;
            }

            kinds.Add(
                com.TryInvokeBool(surface, "IsPlane") ? SurfaceKinds.Plane
                : com.TryInvokeBool(surface, "IsCylinder") ? SurfaceKinds.Cylinder
                : com.TryInvokeBool(surface, "IsCone") ? SurfaceKinds.Cone
                : com.TryInvokeBool(surface, "IsSphere") ? SurfaceKinds.Sphere
                : SurfaceKinds.Other);
        }

        return kinds;
    }

    private static double ToDouble(object? value) =>
        value is null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
}
