using DomainSchemas;

namespace PlatformSelfCheck.Tests;

public sealed class V22DAxialSectionGeometryTests
{
    private static readonly AxialSectionGeometry[] ShaftSections =
    [
        new(0, 110, 40), new(110, 150, 32), new(150, 180, 24)
    ];

    private static readonly AxialSectionGeometry[] JacketSections = [new(0, 180, 140, 120)];

    [Fact]
    public void CorrectSteppedShaftPassesIndependentEnvelopeCylinderAndEndCircleChecks()
    {
        var measured = Shaft();
        Assert.Empty(AxialSectionGeometryValidator.Validate(ShaftSections, measured));
        Assert.True(PartGeometryValidator.Validate(new(1, ShaftVolume, 0.01, ShaftSections), measured).IsValid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ThroughJacketPassesOnEitherDirectionOfTheMeasuredPrincipalAxis(int direction)
    {
        var measured = Jacket(direction);
        Assert.Empty(AxialSectionGeometryValidator.Validate(JacketSections, measured));
        Assert.True(PartGeometryValidator.Validate(new(1, JacketVolume, 0.01, JacketSections), measured).IsValid);
    }

    [Fact]
    public void ReorderedStepsFailEvenWhenVolumeBodyCountAndOuterEnvelopeAreUnchanged()
    {
        // 后两段互换位置但保留各自长度，体积完全相同，必须依靠台阶圆边位置拒绝。
        var measured = Shaft() with
        {
            Edges = Circles(0, 1, [(0, 40), (110, 40), (110, 24), (140, 24), (140, 32), (180, 32)])
        };
        Assert.Equal(ShaftVolume, measured.VolumeCubicMillimeters);
        var result = PartGeometryValidator.Validate(new(1, ShaftVolume, 0.01, ShaftSections), measured);
        Assert.False(result.IsValid);
        Assert.Equal(PartFamilyFailureStages.ParameterGeometryMismatch, result.FailureStage);
        Assert.Contains(result.Issues, issue => issue.Contains("台阶", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("cylinder")]
    [InlineData("circle")]
    [InlineData("envelope")]
    public void OffAxisCylinderCircleOrEnvelopeIsRejected(string changed)
    {
        var measured = Shaft();
        measured = changed switch
        {
            "cylinder" => measured with { Cylinders = measured.Cylinders!.Select(c => c with { OriginYmm = 0.2 }).ToArray() },
            "circle" => measured with { Edges = measured.Edges!.Select(e => e with { AnchorYMm = 0.2 }).ToArray() },
            _ => measured with { ExactExtents = measured.ExactExtents! with { MinYmm = -19.8, MaxYmm = 20.2 } }
        };
        Assert.NotEmpty(AxialSectionGeometryValidator.Validate(ShaftSections, measured));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JacketBlindHoleOrMissingExitCircleCannotPassAsThroughHole(bool blindHole)
    {
        var measured = Jacket(1);
        var edges = measured.Edges!.ToList();
        var exit = edges.FindIndex(e => e.RadiusMm == 60 && e.AnchorYMm == 180);
        Assert.True(exit >= 0);
        if (blindHole) edges[exit] = edges[exit] with { AnchorYMm = 160 };
        else edges.RemoveAt(exit);
        measured = measured with { Edges = edges };
        Assert.NotEmpty(AxialSectionGeometryValidator.Validate(JacketSections, measured));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownCylinderDiameterOrExtraCircularEdgeIsRejected(bool cylinder)
    {
        var measured = Shaft();
        measured = cylinder
            ? measured with { Cylinders = [.. measured.Cylinders!, new(18, 0, 0, 0, 1, 0, 0)] }
            : measured with { Edges = [.. measured.Edges!, Circle(20, 0, 1, 90, 18)] };
        Assert.NotEmpty(AxialSectionGeometryValidator.Validate(ShaftSections, measured));
    }

    [Theory]
    [InlineData("section")]
    [InlineData("extent")]
    [InlineData("cylinder_origin")]
    [InlineData("cylinder_axis")]
    [InlineData("diameter")]
    [InlineData("circle_center")]
    [InlineData("circle_radius")]
    [InlineData("circle_normal")]
    public void NonFiniteDimensionsAndMeasuredGeometryFailClosed(string changed)
    {
        var measured = Shaft();
        var sections = ShaftSections;
        measured = changed switch
        {
            "extent" => measured with { ExactExtents = measured.ExactExtents! with { MaxXmm = double.NaN } },
            "cylinder_origin" => measured with { Cylinders = [measured.Cylinders![0] with { OriginXmm = double.PositiveInfinity }, .. measured.Cylinders.Skip(1)] },
            "cylinder_axis" => measured with { Cylinders = [measured.Cylinders![0] with { AxisX = double.NaN }, .. measured.Cylinders.Skip(1)] },
            "diameter" => measured with { Cylinders = [measured.Cylinders![0] with { DiameterMm = double.PositiveInfinity }, .. measured.Cylinders.Skip(1)] },
            "circle_center" => measured with { Edges = [measured.Edges![0] with { AnchorXMm = double.NaN }, .. measured.Edges.Skip(1)] },
            "circle_radius" => measured with { Edges = [measured.Edges![0] with { RadiusMm = double.PositiveInfinity }, .. measured.Edges.Skip(1)] },
            "circle_normal" => measured with { Edges = [measured.Edges![0] with { Direction = new(double.NaN, 0, 0) }, .. measured.Edges.Skip(1)] },
            _ => measured
        };
        if (changed == "section") sections = [ShaftSections[0] with { EndMm = double.NaN }, .. ShaftSections.Skip(1)];
        Assert.NotEmpty(AxialSectionGeometryValidator.Validate(sections, measured));
    }

    [Theory]
    [InlineData("rebuild")]
    [InlineData("body_count")]
    [InlineData("extents")]
    [InlineData("cylinders")]
    [InlineData("circles")]
    [InlineData("adjacency")]
    [InlineData("oblique_axis")]
    public void MissingOrUnverifiedGeometricEvidenceCannotPass(string changed)
    {
        var measured = Shaft();
        measured = changed switch
        {
            "rebuild" => measured with { RebuildPassed = false },
            "body_count" => measured with { BodyCount = 2 },
            "extents" => measured with { ExactExtents = null },
            "cylinders" => measured with { Cylinders = [] },
            "circles" => measured with { Edges = [] },
            "adjacency" => measured with { Edges = measured.Edges!.Select(e => e with { AdjacentSurfaceKinds = [SurfaceKinds.Cylinder] }).ToArray() },
            _ => measured with { Cylinders = measured.Cylinders!.Select(c => c with { AxisX = 1, AxisY = 1 }).ToArray() }
        };
        Assert.NotEmpty(AxialSectionGeometryValidator.Validate(ShaftSections, measured));
    }

    private static double ShaftVolume => Math.PI / 4d * (40 * 40 * 110 + 32 * 32 * 40 + 24 * 24 * 30);
    private static double JacketVolume => Math.PI / 4d * (140 * 140 - 120 * 120) * 180;

    private static MeasuredGeometry Shaft() => new(
        true, null, new(0, -20, -20, 180, 20, 20), 1, ShaftVolume, null, ShaftVolume,
        Cylinders: [new(40, 0, 0, 0, 1, 0, 0), new(32, 110, 0, 0, 1, 0, 0), new(24, 150, 0, 0, 1, 0, 0)],
        Edges: Circles(0, 1, [(0, 40), (110, 40), (110, 32), (150, 32), (150, 24), (180, 24)]));

    private static MeasuredGeometry Jacket(int direction) => new(
        true, null, new(-70, direction > 0 ? 0 : -180, -70, 70, direction > 0 ? 180 : 0, 70), 1, JacketVolume, null, JacketVolume,
        Cylinders: [new(140, 0, 0, 0, 0, direction, 0), new(120, 0, 0, 0, 0, direction, 0)],
        Edges: Circles(1, direction, [(0, 140), (180, 140), (0, 120), (180, 120)]));

    private static MeasuredEdge[] Circles(int axis, int direction, (double Position, double Diameter)[] circles) =>
        circles.Select((circle, index) => Circle(index, axis, direction, circle.Position, circle.Diameter)).ToArray();

    private static MeasuredEdge Circle(int index, int axis, int direction, double position, double diameter) => new(
        index, EdgeKinds.Circle, Math.PI * diameter,
        axis == 0 ? position * direction : 0, axis == 1 ? position * direction : 0, 0, diameter / 2d,
        [SurfaceKinds.Cylinder, SurfaceKinds.Plane], new(axis == 0 ? direction : 0, axis == 1 ? direction : 0, 0));
}
