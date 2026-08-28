using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features;

/// <summary>
/// Exclusive V2.0-D COM read boundary for post-rebuild geometry. It returns
/// serializable measurements only; ModelUpdateService and GeometryValidator
/// never receive an RCW, a ModelDoc2, or a raw COM object.
/// </summary>
public interface ISolidWorksGeometryReader
{
    SolidWorksGeometryReadResult Read(object model, CancellationToken cancellationToken = default);
}

public sealed record SolidWorksGeometryReadResult(
    MeasuredGeometry? Geometry,
    string? FailureStage,
    IReadOnlyList<string> Issues)
{
    public bool IsSuccess => Geometry is not null && string.IsNullOrWhiteSpace(FailureStage) && Issues.Count == 0;
}

public sealed class RealSolidWorksGeometryReader : ISolidWorksGeometryReader
{
    private const int SwSolidBody = 0;
    private const double MetersToMillimeters = 1000d;
    private readonly ISolidWorksComFacade _com;
    private readonly List<object> _ownedReferences = [];
    private readonly HashSet<object> _ownedReferenceSet = new(ReferenceEqualityComparer.Instance);

    public RealSolidWorksGeometryReader(ISolidWorksComFacade com)
    {
        _com = com ?? throw new ArgumentNullException(nameof(com));
    }

    public SolidWorksGeometryReadResult Read(object model, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_com.TryInvokeBool(model, "ForceRebuild3", false))
            {
                return new SolidWorksGeometryReadResult(
                    new MeasuredGeometry(
                        RebuildPassed: false,
                        BoundingBox: null,
                        ExactExtents: null,
                        BodyCount: null,
                        VolumeCubicMillimeters: null,
                        MassKilograms: null,
                        MassPropertyVolumeCubicMillimeters: null,
                        ReadIssues: Array.Empty<string>(),
                        GeometryEvidenceSourceRevision: GeometryValidationEvidencePolicy.ComputeSourceRevision()),
                    PartFamilyFailureStages.RebuildFailed,
                    ["rebuild_failed: ForceRebuild3(false) returned false."]);
            }

            var boundingBox = ReadBoundingBox(model);
            var bodies = ReadBodies(model);
            var bodyMeasurements = ReadBodiesAndCylinders(bodies, cancellationToken);
            var exactExtents = ReadExactExtents(bodies);
            var featureMeasurements = ReadFeatureTree(model, cancellationToken);
            var massProperty = ReadOptionalMassProperty(model);
            var geometry = new MeasuredGeometry(
                RebuildPassed: true,
                BoundingBox: boundingBox,
                ExactExtents: exactExtents,
                BodyCount: bodies.Count,
                VolumeCubicMillimeters: bodyMeasurements.TotalVolumeCubicMillimeters,
                MassKilograms: massProperty.MassKilograms,
                MassPropertyVolumeCubicMillimeters: massProperty.VolumeCubicMillimeters,
                FeatureTypes: featureMeasurements.Select(feature => feature.TypeName).ToArray(),
                Features: featureMeasurements,
                CylindricalDiametersMm: bodyMeasurements.CylindricalDiametersMm,
                Cylinders: bodyMeasurements.Cylinders,
                Edges: ReadEdges(bodies),
                ReadIssues: Array.Empty<string>(),
                SolidWorksVersion: null,
                GeometryEvidenceSourceRevision: GeometryValidationEvidencePolicy.ComputeSourceRevision());
            return new SolidWorksGeometryReadResult(geometry, null, Array.Empty<string>());
        }
        catch (GeometryReadException exception)
        {
            return Failed(exception.Stage, exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or MissingMethodException or System.Reflection.TargetInvocationException or System.Runtime.InteropServices.COMException or FormatException or OverflowException)
        {
            return Failed(PartFamilyFailureStages.GeometryReadFailed, exception.GetBaseException().Message);
        }
        finally
        {
            ReleaseOwnedReferences();
        }
    }

    private GeometryBoundingBox ReadBoundingBox(object model)
    {
        var raw = _com.TryInvoke(model, "GetPartBox", true);
        if (raw is not Array values || values.Length < 6)
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, "GetPartBox(true) did not return six values.");
        }

        var coordinates = Enumerable.Range(0, 6)
            .Select(index => ToFiniteDouble(values.GetValue(index), "GetPartBox"))
            .Select(value => value * MetersToMillimeters)
            .ToArray();
        return new GeometryBoundingBox(
            coordinates[0], coordinates[1], coordinates[2],
            coordinates[3], coordinates[4], coordinates[5]);
    }

    /// <summary>
    /// 枚举实体边，供声明式边选择判据求解。测量逻辑集中在
    /// <see cref="SolidWorksEdgeEnumerator"/>，与 FeatureAdapter 共用同一份，
    /// 保证"读取器看到的边"与"真实选中的边"不会出现分歧。
    /// <para>
    /// 读取失败不抛异常：边信息是增量能力，缺失时返回空集合，
    /// 由上层判据以 edge_selection_not_found 显式失败，而不是让几何校验整体崩掉。
    /// </para>
    /// </summary>
    private IReadOnlyList<MeasuredEdge> ReadEdges(IReadOnlyList<object> bodies) =>
        SolidWorksEdgeEnumerator.Enumerate(_com, bodies)
            .Select(edge => edge.Measured)
            .ToArray();

    private IReadOnlyList<object> ReadBodies(object model)
    {
        var raw = _com.TryInvoke(model, "GetBodies2", SwSolidBody, false);
        if (raw is null)
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, "GetBodies2(swSolidBody, false) returned null.");
        }

        var bodies = raw is Array array
            ? array.Cast<object?>().Where(value => value is not null).Cast<object>().ToArray()
            : [raw];
        foreach (var body in bodies)
        {
            Own(body);
        }

        return bodies;
    }

    private BodyMeasurements ReadBodiesAndCylinders(
        IReadOnlyList<object> bodies,
        CancellationToken cancellationToken)
    {
        var totalVolumeCubicMillimeters = 0d;
        var cylindricalDiameters = new List<double>();
        var cylinders = new List<MeasuredCylinder>();
        foreach (var body in bodies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var massProperties = _com.TryInvoke(body, "GetMassProperties", 1d);
            if (massProperties is not Array values || values.Length <= 5)
            {
                throw Fail(PartFamilyFailureStages.GeometryReadFailed, "IBody2.GetMassProperties(1d) did not return volume and mass.");
            }

            var volumeCubicMeters = ToFiniteDouble(values.GetValue(3), "IBody2.GetMassProperties volume");
            if (volumeCubicMeters <= 0)
            {
                throw Fail(PartFamilyFailureStages.VolumeValidationFailed, "IBody2.GetMassProperties returned a non-positive volume.");
            }

            totalVolumeCubicMillimeters += volumeCubicMeters * 1_000_000_000d;
            var facesRaw = _com.TryInvoke(body, "GetFaces");
            if (facesRaw is null)
            {
                throw Fail(PartFamilyFailureStages.GeometryReadFailed, "IBody2.GetFaces returned null.");
            }

            var faces = facesRaw is Array faceArray
                ? faceArray.Cast<object?>().Where(value => value is not null).Cast<object>().ToArray()
                : [facesRaw];
            foreach (var face in faces)
            {
                Own(face);
                var surface = Own(_com.TryInvoke(face, "GetSurface"));
                if (surface is null)
                {
                    throw Fail(PartFamilyFailureStages.GeometryReadFailed, "IFace2.GetSurface returned null.");
                }

                var isCylinder = ReadBoolean(surface, "IsCylinder");
                if (!isCylinder)
                {
                    continue;
                }

                var cylinderParameters = _com.TryGetProperty(surface, "CylinderParams") as Array;
                if (cylinderParameters is null || cylinderParameters.Length < 7)
                {
                    throw Fail(PartFamilyFailureStages.GeometryReadFailed, "ISurface.CylinderParams did not return a radius.");
                }

                var radiusMeters = ToFiniteDouble(cylinderParameters.GetValue(6), "ISurface.CylinderParams[6]");
                if (radiusMeters <= 0)
                {
                    throw Fail(PartFamilyFailureStages.GeometryReadFailed, "ISurface.CylinderParams[6] returned a non-positive radius.");
                }

                var diameterMm = radiusMeters * 2d * MetersToMillimeters;
                cylindricalDiameters.Add(diameterMm);
                cylinders.Add(new MeasuredCylinder(
                    diameterMm,
                    ToFiniteDouble(cylinderParameters.GetValue(0), "ISurface.CylinderParams[0]") * MetersToMillimeters,
                    ToFiniteDouble(cylinderParameters.GetValue(1), "ISurface.CylinderParams[1]") * MetersToMillimeters,
                    ToFiniteDouble(cylinderParameters.GetValue(2), "ISurface.CylinderParams[2]") * MetersToMillimeters,
                    ToFiniteDouble(cylinderParameters.GetValue(3), "ISurface.CylinderParams[3]"),
                    ToFiniteDouble(cylinderParameters.GetValue(4), "ISurface.CylinderParams[4]"),
                    ToFiniteDouble(cylinderParameters.GetValue(5), "ISurface.CylinderParams[5]")));
            }
        }

        return new BodyMeasurements(totalVolumeCubicMillimeters, cylindricalDiameters, cylinders);
    }

    private GeometryBoundingBox ReadExactExtents(IReadOnlyList<object> bodies)
    {
        // GetExtremePoint is the most general API for a tight body envelope, but
        // some out-of-process RCWs do not expose its by-ref overload through
        // IDispatch. Vertices are real B-rep entities and provide an exact
        // envelope for the planar plate family; use them first so the reader
        // does not turn a valid real model into a false failure solely because
        // of the late-bound marshaler.
        var vertexExtents = TryReadVertexExtents(bodies);
        return vertexExtents ?? ReadExtremePointExtents(bodies);
    }

    private GeometryBoundingBox? TryReadVertexExtents(IReadOnlyList<object> bodies)
    {
        var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        var vertexCount = 0;
        foreach (var body in bodies)
        {
            var verticesRaw = _com.TryInvoke(body, "GetVertices");
            if (verticesRaw is null)
            {
                return null;
            }

            var vertices = verticesRaw is Array vertexArray
                ? vertexArray.Cast<object?>().Where(value => value is not null).Cast<object>().ToArray()
                : [verticesRaw];
            foreach (var vertex in vertices)
            {
                Own(vertex);
                var point = _com.TryInvoke(vertex, "GetPoint") as Array;
                if (point is null || point.Length < 3)
                {
                    return null;
                }

                for (var index = 0; index < 3; index++)
                {
                    var coordinate = ToFiniteDouble(point.GetValue(index), "IVertex.GetPoint") * MetersToMillimeters;
                    minimum[index] = Math.Min(minimum[index], coordinate);
                    maximum[index] = Math.Max(maximum[index], coordinate);
                }

                vertexCount++;
            }
        }

        return vertexCount > 0 &&
               !minimum.Any(value => !double.IsFinite(value)) &&
               !maximum.Any(value => !double.IsFinite(value))
            ? new GeometryBoundingBox(minimum[0], minimum[1], minimum[2], maximum[0], maximum[1], maximum[2])
            : null;
    }

    private GeometryBoundingBox ReadExtremePointExtents(IReadOnlyList<object> bodies)
    {
        var minimum = new[] { double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity };
        var maximum = new[] { double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity };
        var directions = new[]
        {
            (0, -1d), (0, 1d),
            (1, -1d), (1, 1d),
            (2, -1d), (2, 1d)
        };
        foreach (var body in bodies)
        {
            foreach (var (axis, direction) in directions)
            {
                var vector = new[] { 0d, 0d, 0d };
                vector[axis] = direction;
                var arguments = new object?[] { vector[0], vector[1], vector[2], 0d, 0d, 0d };
                var result = _com.TryInvokeWithArgs(body, "GetExtremePoint", arguments);
                if (!IsComTrue(result))
                {
                    throw Fail(
                        PartFamilyFailureStages.GeometryReadFailed,
                        $"IBody2.GetExtremePoint did not return true for direction ({vector[0]:R}, {vector[1]:R}, {vector[2]:R}); " +
                        $"returned {Describe(result)} and output arguments ({Describe(arguments[3])}, {Describe(arguments[4])}, {Describe(arguments[5])}).");
                }

                var value = new[]
                {
                    ToFiniteDouble(arguments[3], "GetExtremePoint x") * MetersToMillimeters,
                    ToFiniteDouble(arguments[4], "GetExtremePoint y") * MetersToMillimeters,
                    ToFiniteDouble(arguments[5], "GetExtremePoint z") * MetersToMillimeters
                };
                if (direction < 0)
                {
                    for (var index = 0; index < 3; index++)
                    {
                        minimum[index] = Math.Min(minimum[index], value[index]);
                    }
                }
                else
                {
                    for (var index = 0; index < 3; index++)
                    {
                        maximum[index] = Math.Max(maximum[index], value[index]);
                    }
                }
            }
        }

        if (minimum.Any(value => !double.IsFinite(value)) || maximum.Any(value => !double.IsFinite(value)))
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, "IBody2.GetExtremePoint did not produce finite extents.");
        }

        return new GeometryBoundingBox(
            minimum[0], minimum[1], minimum[2], maximum[0], maximum[1], maximum[2]);
    }

    private IReadOnlyList<MeasuredFeature> ReadFeatureTree(object model, CancellationToken cancellationToken)
    {
        var features = new List<MeasuredFeature>();
        var current = Own(_com.TryInvoke(model, "FirstFeature"));
        var guard = 0;
        while (current is not null && guard++ < 512)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = _com.TryGetProperty(current, "Name")?.ToString() ?? string.Empty;
            var typeName = _com.TryInvoke(current, "GetTypeName2")?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                features.Add(new MeasuredFeature(name, typeName));
            }

            current = Own(_com.TryInvoke(current, "GetNextFeature"));
        }

        if (guard >= 512)
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, "FeatureTree traversal exceeded its safety limit.");
        }

        return features;
    }

    private MassPropertyMeasurement ReadOptionalMassProperty(object model)
    {
        var extension = Own(_com.TryGetProperty(model, "Extension"));
        var massProperty = Own(_com.TryInvoke(extension, "CreateMassProperty2"));
        if (massProperty is null)
        {
            return new MassPropertyMeasurement(null, null);
        }

        var volume = TryFiniteDouble(_com.TryGetProperty(massProperty, "Volume"));
        var mass = TryFiniteDouble(_com.TryGetProperty(massProperty, "Mass"));
        return new MassPropertyMeasurement(
            volume is null ? null : volume.Value * 1_000_000_000d,
            mass);
    }

    private bool ReadBoolean(object target, string name)
    {
        var raw = _com.TryInvoke(target, name) ?? _com.TryGetProperty(target, name);
        if (raw is not bool value)
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, $"{name} did not return a Boolean value.");
        }

        return value;
    }

    private SolidWorksGeometryReadResult Failed(string stage, string issue) =>
        new(
            new MeasuredGeometry(
                RebuildPassed: false,
                BoundingBox: null,
                ExactExtents: null,
                BodyCount: null,
                VolumeCubicMillimeters: null,
                MassKilograms: null,
                MassPropertyVolumeCubicMillimeters: null,
                ReadIssues: [$"{stage}: {issue}"],
                GeometryEvidenceSourceRevision: GeometryValidationEvidencePolicy.ComputeSourceRevision()),
            stage,
            [$"{stage}: {issue}"]);

    private object? Own(object? value)
    {
        if (value is not null && _ownedReferenceSet.Add(value))
        {
            _ownedReferences.Add(value);
        }

        return value;
    }

    private void ReleaseOwnedReferences()
    {
        for (var index = _ownedReferences.Count - 1; index >= 0; index--)
        {
            try
            {
                _com.ReleaseComObject(_ownedReferences[index]);
            }
            catch
            {
                // Release must continue if an individual RCW has gone stale.
            }
        }

        _ownedReferences.Clear();
        _ownedReferenceSet.Clear();
    }

    private static GeometryReadException Fail(string stage, string message) => new(stage, message);

    private static double ToFiniteDouble(object? value, string source)
    {
        var converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (!double.IsFinite(converted))
        {
            throw Fail(PartFamilyFailureStages.GeometryReadFailed, $"{source} returned a non-finite numeric value.");
        }

        return converted;
    }

    private static double? TryFiniteDouble(object? value)
    {
        try
        {
            var converted = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return double.IsFinite(converted) ? converted : null;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            return null;
        }
    }

    private static bool IsComTrue(object? value) => value switch
    {
        bool boolean => boolean,
        sbyte numeric => numeric != 0,
        byte numeric => numeric != 0,
        short numeric => numeric != 0,
        ushort numeric => numeric != 0,
        int numeric => numeric != 0,
        uint numeric => numeric != 0,
        long numeric => numeric != 0,
        ulong numeric => numeric != 0,
        _ => false
    };

    private static string Describe(object? value) => value is null
        ? "null"
        : $"{value.GetType().FullName}:{value}";

    private sealed record BodyMeasurements(
        double TotalVolumeCubicMillimeters,
        IReadOnlyList<double> CylindricalDiametersMm,
        IReadOnlyList<MeasuredCylinder> Cylinders);

    private sealed record MassPropertyMeasurement(double? VolumeCubicMillimeters, double? MassKilograms);

    private sealed class GeometryReadException(string stage, string message) : Exception(message)
    {
        public string Stage { get; } = stage;
    }
}
