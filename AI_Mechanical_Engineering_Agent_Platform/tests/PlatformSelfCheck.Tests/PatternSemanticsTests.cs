using DomainSchemas;
using SolidWorksWorker.Features;
using SolidWorksWorker.Features.Pattern;

namespace PlatformSelfCheck.Tests;

public sealed class PatternSemanticsTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("seed;other")]
    [InlineData("seed,other")]
    [InlineData("seed;")]
    [InlineData(",seed")]
    public void PatternRejectsEmptyOrMultipleSeeds(string seed)
    {
        foreach (var handler in Handlers())
        {
            var feature = Feature(handler.FeatureType, seed);
            AssertInvalid(handler.Validate(feature));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PatternRequiresExactlyOneMatchingReferencedFeature(int shape)
    {
        string[] references = shape switch
        {
            0 => [], 1 => ["other"], 2 => ["seed", "other"], _ => ["seed", "seed"]
        };
        foreach (var handler in Handlers())
        {
            AssertInvalid(handler.Validate(Feature(handler.FeatureType) with { ReferencedFeatures = references }));
        }
    }

    [Fact]
    public void SingleSeedReferenceEstablishesDependencyWithoutDuplicateDeclaration()
    {
        foreach (var handler in Handlers())
        {
            var feature = Feature(handler.FeatureType);
            var referenceOnly = feature with { Dependencies = [] };
            Assert.True(handler.Validate(referenceOnly).IsValid);
            Assert.Contains("seed", referenceOnly.DependsOn);
            Assert.True(handler.Validate(feature with { Dependencies = ["other"] }).IsValid);
            AssertInvalid(handler.Validate(feature with { FeatureId = "seed" }));
            Assert.True(handler.Validate(feature with { Dependencies = ["seed", "other"] }).IsValid);
            Assert.True(handler.Validate(feature with { ReferencedFeatures = ["SEED"], Dependencies = ["SEED"] }).IsValid);
        }
    }

    [Theory]
    [InlineData("x")]
    [InlineData("y")]
    [InlineData("z")]
    [InlineData("+x")]
    [InlineData("-x")]
    [InlineData("+y")]
    [InlineData("-y")]
    [InlineData("+z")]
    [InlineData("-z")]
    public void BothHandlersAcceptSignedPrincipalDirections(string direction)
    {
        foreach (var handler in Handlers())
        {
            var feature = Feature(handler.FeatureType);
            var parameters = feature.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
            parameters[handler.FeatureType == FeatureTypes.LinearPattern ? "direction" : "axis"] = direction;
            Assert.True(handler.Validate(feature with { Parameters = parameters }).IsValid);
        }
    }

    [Theory]
    [InlineData(1d, "x", false)]
    [InlineData(1d, "+x", false)]
    [InlineData(-1d, "+x", true)]
    [InlineData(1d, "-x", true)]
    [InlineData(-1d, "-x", false)]
    [InlineData(1e300d, "+x", false)]
    public void SignedDotResolvesOppositeMeasuredDirections(double measuredX, string declared, bool expectedFlip)
    {
        Assert.True(PrincipalAxisRules.TryResolveFlip(new(measuredX, 0, 0), declared, out var flip));
        Assert.Equal(expectedFlip, flip);
    }

    [Theory]
    [InlineData(0d, 0d, 0d)]
    [InlineData(0d, 1d, 0d)]
    [InlineData(1d, 1d, 0d)]
    [InlineData(double.NaN, 0d, 0d)]
    [InlineData(double.PositiveInfinity, 0d, 0d)]
    [InlineData(1d, double.NegativeInfinity, 0d)]
    public void InvalidOrNonparallelMeasuredDirectionFailsClosed(double x, double y, double z)
    {
        Assert.False(PrincipalAxisRules.TryResolveFlip(new(x, y, z), "+x", out _));
    }

    [Fact]
    public void UnknownAxisAndMissingMeasuredDirectionFailClosed()
    {
        Assert.False(PrincipalAxisRules.TryResolveFlip(null, "y", out _));
        Assert.False(PrincipalAxisRules.TryResolveFlip(new(1, 0, 0), "xy", out _));
    }

    [Theory]
    [InlineData("{\"kind\":\"circle\",\"radius_mm\":5}")]
    [InlineData("{\"kind\":\"circle\",\"anchor_x_mm\":0}")]
    [InlineData("{\"kind\":\"circle\",\"anchor_x_mm\":20,\"anchor_z_mm\":0}")]
    public void CircularPatternRejectsMissingOrOffsetAxisPosition(string selection)
    {
        var feature = Feature(FeatureTypes.CircularPattern);
        var parameters = feature.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        parameters["axis_selection"] = selection;
        AssertInvalid(new CircularPatternHandler().Validate(feature with { Parameters = parameters }));
    }

    [Fact]
    public void CircularAxisRequiresOnlyTwoTransverseCoordinatesAndVerifiesMeasuredCoaxiality()
    {
        var criteria = new EdgeSelectionCriteria(Kind: EdgeKinds.Circle, AnchorXMm: 0, AnchorZMm: 0);
        Assert.True(PrincipalAxisRules.HasExplicitOriginAxisPosition(criteria, new(0, -1, 0)));
        var edge = new MeasuredEdge(0, EdgeKinds.Circle, 10, 0, 50, 0, 5, [], new(0, 1, 0));
        Assert.True(PrincipalAxisRules.IsOnOriginAxis(edge, new(0, 1, 0), 0.05));
        Assert.False(PrincipalAxisRules.IsOnOriginAxis(edge with { AnchorXMm = 20 }, new(0, 1, 0), 0.05));
        Assert.False(PrincipalAxisRules.IsOnOriginAxis(edge with { AnchorXMm = 0.06 }, new(0, 1, 0), 100));
        Assert.False(PrincipalAxisRules.IsOnOriginAxis(edge with { AnchorZMm = double.NaN }, new(0, 1, 0), 0.05));
    }

    [Fact]
    public void NonfinitePositionCannotPassEdgeSelectionThroughNanComparison()
    {
        var edge = new MeasuredEdge(0, EdgeKinds.Circle, 10, double.NaN, 10, 0, 5, []);
        var result = EdgeSelectionResolver.Resolve([edge], new(Kind: EdgeKinds.Circle, AnchorXMm: 0));
        Assert.False(result.IsResolved);
        var badCriteria = EdgeSelectionResolver.Resolve([], new(Kind: EdgeKinds.Circle, AnchorXMm: double.NaN));
        Assert.Equal(PartFamilyFailureStages.EdgeSelectionInvalidCriteria, badCriteria.FailureStage);
    }

    private static FeatureHandlerBase[] Handlers() => [new LinearPatternHandler(), new CircularPatternHandler()];

    private static FeatureDefinition Feature(string type, string seed = "seed") =>
        new("pattern", type,
            type == FeatureTypes.LinearPattern
                ? new Dictionary<string, string>
                {
                    ["seed_feature"] = seed, ["direction"] = "x", ["instance_count"] = "3", ["spacing_mm"] = "15",
                    ["direction_selection"] = V21AComplexFeatureLibraryTests.DirectionEdgeCriteriaJson
                }
                : new Dictionary<string, string>
                {
                    ["seed_feature"] = seed, ["axis"] = "y", ["instance_count"] = "3", ["angle_deg"] = "90",
                    ["axis_selection"] = "{\"kind\":\"circle\",\"anchor_x_mm\":0,\"anchor_y_mm\":0,\"anchor_z_mm\":0}"
                },
            dependencies: ["seed"], referencedFeatures: ["seed"]);

    private static void AssertInvalid(FeatureHandlerValidationResult validation)
    {
        Assert.False(validation.IsValid);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, validation.FailureStage);
    }
}
