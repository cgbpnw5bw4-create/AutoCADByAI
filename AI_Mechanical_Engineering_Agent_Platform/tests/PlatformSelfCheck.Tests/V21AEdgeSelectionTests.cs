using DomainSchemas;

namespace PlatformSelfCheck.Tests;

/// <summary>
/// 边选择解析。夹具是**真实探查数据**：本机 SOLIDWORKS 2023 打开
/// evidence/solidworks/20260824_072219_4629586/model.SLDPRT（100×60×10 板，两个 Ø10 通孔），
/// 通过 IBody2.GetEdges 枚举得到的 16 条边。因此这些测试验证的是真实拓扑，
/// 而不是想象出来的形状；同时不需要启动 SolidWorks。
/// </summary>
public sealed class V21AEdgeSelectionTests
{
    /// <summary>探查实测：16 条边 = 12 条直线 + 4 条孔口圆。</summary>
    private static IReadOnlyList<MeasuredEdge> ProbedPlateEdges() =>
    [
        // 四条竖直角边，长度等于板厚 10 mm
        new(0, EdgeKinds.Line, 10.00d, 50.00d, 5.00d, -30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(1, EdgeKinds.Line, 10.00d, -50.00d, 5.00d, -30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(2, EdgeKinds.Line, 10.00d, -50.00d, 5.00d, 30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(3, EdgeKinds.Line, 10.00d, 50.00d, 5.00d, 30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        // 顶面四边 y=10
        new(4, EdgeKinds.Line, 100.00d, 0.00d, 10.00d, 30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(5, EdgeKinds.Line, 60.00d, -50.00d, 10.00d, 0.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(6, EdgeKinds.Line, 100.00d, 0.00d, 10.00d, -30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(7, EdgeKinds.Line, 60.00d, 50.00d, 10.00d, 0.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        // 底面四边 y=0
        new(8, EdgeKinds.Line, 100.00d, 0.00d, 0.00d, 30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(9, EdgeKinds.Line, 60.00d, -50.00d, 0.00d, 0.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(10, EdgeKinds.Line, 100.00d, 0.00d, 0.00d, -30.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        new(11, EdgeKinds.Line, 60.00d, 50.00d, 0.00d, 0.00d, null, [SurfaceKinds.Plane, SurfaceKinds.Plane]),
        // 孔口圆：周长 31.42 = pi*10，半径 5，圆柱面与平面相交
        new(12, EdgeKinds.Circle, 31.42d, -20.00d, 10.00d, 0.00d, 5.00d, [SurfaceKinds.Cylinder, SurfaceKinds.Plane]),
        new(13, EdgeKinds.Circle, 31.42d, -20.00d, 0.00d, 0.00d, 5.00d, [SurfaceKinds.Plane, SurfaceKinds.Cylinder]),
        new(14, EdgeKinds.Circle, 31.42d, 20.00d, 10.00d, 0.00d, 5.00d, [SurfaceKinds.Cylinder, SurfaceKinds.Plane]),
        new(15, EdgeKinds.Circle, 31.42d, 20.00d, 0.00d, 0.00d, 5.00d, [SurfaceKinds.Plane, SurfaceKinds.Cylinder])
    ];

    [Fact]
    public void TopHoleRimsResolveToExactlyTwoEdges()
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                RadiusMm: 5.00d,
                AdjacentSurfaceKinds: [SurfaceKinds.Cylinder, SurfaceKinds.Plane],
                AnchorYMm: 10.00d,
                ExpectedCount: 2));

        Assert.True(result.IsResolved, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal([12, 14], result.Matches.Select(edge => edge.Index));
    }

    [Fact]
    public void AdjacentSurfaceComparisonIsOrderInsensitive()
    {
        // 底面孔口的相邻面顺序是 plane+cylinder，顶面是 cylinder+plane。
        // 判据不应该因为书写顺序不同而漏选。
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                AdjacentSurfaceKinds: [SurfaceKinds.Cylinder, SurfaceKinds.Plane],
                AnchorYMm: 0.00d,
                ExpectedCount: 2));

        Assert.True(result.IsResolved, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal([13, 15], result.Matches.Select(edge => edge.Index));
    }

    [Fact]
    public void SingleHoleRimResolvesWhenAnchoredByPosition()
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                AnchorXMm: -20.00d,
                AnchorYMm: 10.00d,
                ExpectedCount: 1));

        Assert.True(result.IsResolved, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal(12, Assert.Single(result.Matches).Index);
    }

    [Fact]
    public void VerticalCornerEdgesResolveByThicknessLength()
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Line,
                LengthMm: 10.00d,
                ExpectedCount: 4));

        Assert.True(result.IsResolved, string.Join(Environment.NewLine, result.Issues));
        Assert.Equal([0, 1, 2, 3], result.Matches.Select(edge => edge.Index));
    }

    [Fact]
    public void AmbiguousCriteriaRefuseToGuessInsteadOfPickingTheFirstMatch()
    {
        // 这是整套机制存在的理由：判据匹配到四条孔口圆，却声明只要一条。
        // SolidWorks 的 FeatureFillet3 在选错边时同样返回非空 IFeature，
        // 产出圆角打在错误位置的零件。因此必须拒绝执行，而不是取第一条。
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                RadiusMm: 5.00d,
                ExpectedCount: 1));

        Assert.False(result.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionAmbiguous, result.FailureStage);
        Assert.Equal(4, result.Matches.Count);
        Assert.Contains(result.Issues, issue => issue.Contains("refusing to guess", StringComparison.Ordinal));
    }

    [Fact]
    public void CriteriaMatchingNothingFailsClosed()
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(Kind: EdgeKinds.Line, LengthMm: 999.00d));

        Assert.False(result.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionNotFound, result.FailureStage);
        Assert.Empty(result.Matches);
    }

    [Fact]
    public void UnconstrainedCriteriaAreRejectedRatherThanMatchingEverything()
    {
        var result = EdgeSelectionResolver.Resolve(ProbedPlateEdges(), new EdgeSelectionCriteria());

        Assert.False(result.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionInvalidCriteria, result.FailureStage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveExpectedCountIsRejected(int expectedCount)
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(Kind: EdgeKinds.Line, ExpectedCount: expectedCount));

        Assert.False(result.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionInvalidCriteria, result.FailureStage);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-0.1d)]
    [InlineData(double.NaN)]
    public void NonPositiveOrNonFiniteToleranceIsRejected(double tolerance)
    {
        var result = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(Kind: EdgeKinds.Line, ToleranceMm: tolerance));

        Assert.False(result.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionInvalidCriteria, result.FailureStage);
    }

    [Fact]
    public void ToleranceIsAppliedRatherThanExactEquality()
    {
        // 实测周长 31.42 是四舍五入值，真实值 pi*10 = 31.4159...
        // 判据必须能用容差匹配，否则真实数据永远选不中。
        var resolved = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                LengthMm: 31.4159d,
                ToleranceMm: 0.01d,
                AnchorYMm: 10.00d,
                ExpectedCount: 2));

        Assert.True(resolved.IsResolved, string.Join(Environment.NewLine, resolved.Issues));

        var tooTight = EdgeSelectionResolver.Resolve(
            ProbedPlateEdges(),
            new EdgeSelectionCriteria(
                Kind: EdgeKinds.Circle,
                LengthMm: 31.4159d,
                ToleranceMm: 0.0001d,
                AnchorYMm: 10.00d,
                ExpectedCount: 2));

        Assert.False(tooTight.IsResolved);
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionNotFound, tooTight.FailureStage);
    }
}
