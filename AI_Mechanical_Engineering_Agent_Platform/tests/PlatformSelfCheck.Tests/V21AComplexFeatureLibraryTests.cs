using DomainSchemas;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

/// <summary>
/// V2.1-A 复杂特征库。默认路径不启动 SolidWorks：这里只覆盖注册、参数校验、
/// dry-run 计划编译，以及"无证据必须拒绝真实执行"这条红线。
/// </summary>
public sealed class V21AComplexFeatureLibraryTests
{
    internal const string FilletEdgeCriteriaJson =
        """
        {"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":10,"expected_count":2}
        """;

    // 倒角刻意取另一侧孔口（y=0）。同一零件的两组孔口只差定位点，
    // 用不同的判据才能证明"按位置区分"是真的，而不是碰巧只有两条圆边。
    internal const string ChamferEdgeCriteriaJson =
        """
        {"kind":"circle","adjacent_surface_kinds":["cylinder","plane"],"anchor_y_mm":0,"expected_count":2}
        """;

    /// <summary>方向边判据：板顶面沿 X 的那条 100 mm 棱，锚点唯一。</summary>
    internal const string DirectionEdgeCriteriaJson =
        """
        {"kind":"line","length_mm":100,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":30,"expected_count":1}
        """;

    /// <summary>轴边判据：中心 Ø10 孔的顶面孔口，其法向即阵列轴。</summary>
    internal const string AxisEdgeCriteriaJson =
        """
        {"kind":"circle","radius_mm":5,"anchor_x_mm":0,"anchor_y_mm":10,"anchor_z_mm":0,"expected_count":1}
        """;

    /// <summary>本轮新增的全部复杂特征。</summary>
    private static readonly string[] ComplexFeatureTypes =
    [
        FeatureTypes.Fillet,
        FeatureTypes.Chamfer,
        FeatureTypes.LinearPattern,
        FeatureTypes.CircularPattern,
        FeatureTypes.Mirror
    ];

    [Theory]
    [InlineData(FeatureTypes.Fillet)]
    [InlineData(FeatureTypes.Chamfer)]
    [InlineData(FeatureTypes.LinearPattern)]
    [InlineData(FeatureTypes.CircularPattern)]
    [InlineData(FeatureTypes.Mirror)]
    public void ComplexFeatureHandlersAreRegisteredWithNonEmptySchema(string featureType)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();

        Assert.True(registry.TryGetHandler(featureType, out var handler));
        Assert.NotNull(handler);
        Assert.Equal(featureType, handler.FeatureType, ignoreCase: true);
        Assert.NotEmpty(handler.ParameterSchema);
        Assert.False(string.IsNullOrWhiteSpace(handler.ApiEvidence.ApiName));
    }

    [Theory]
    [InlineData(FeatureTypes.Fillet)]
    [InlineData(FeatureTypes.Chamfer)]
    [InlineData(FeatureTypes.LinearPattern)]
    [InlineData(FeatureTypes.CircularPattern)]
    [InlineData(FeatureTypes.Mirror)]
    public void ComplexFeatureDryRunCompilesAnOperation(string featureType)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        Assert.True(registry.TryGetHandler(featureType, out var handler));

        var validation = handler.Validate(ValidFeature(featureType));
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Issues));

        var plan = handler.BuildPlan(
            ValidFeature(featureType),
            new FeatureHandlerBuildPlanContext($"op-{featureType}", "TopPlane", []));

        Assert.Null(plan.FailureStage);
        Assert.NotNull(plan.Operation);
        Assert.Equal(featureType, plan.Operation!.Parameters["feature_type"], ignoreCase: true);
    }

    [Fact]
    public void EveryHandlerDeclaringNoEvidenceIsBlockedFromRealExecution()
    {
        // 判据对象取自注册表里"自报 unverified"的 Handler，而不是写死的清单：
        // 每补一块真机证据就要改一次清单，正是"保护随迁移消失"的老毛病。
        // V2.1-A 五个复杂特征全部取证后，这里的对象自然变成 revolve_boss。
        var registry = FeatureHandlerRegistry.CreateDefault();
        var unverified = registry.GetAll()
            .Where(handler => string.Equals(
                handler.ApiEvidence.Status,
                FeatureApiEvidenceStatuses.Unverified,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(unverified);
        foreach (var handler in unverified)
        {
            Assert.False(
                handler.ApiEvidence.AllowsRealExecution,
                $"{handler.FeatureType} declares unverified evidence but allows real execution.");

            // 未取证的 Feature 不得携带 diagnostic 绑定，否则会伪装成已取证。
            Assert.True(string.IsNullOrWhiteSpace(handler.ApiEvidence.DiagnosticRunPath));
            Assert.True(string.IsNullOrWhiteSpace(handler.ApiEvidence.SourceRevision));
            Assert.NotEmpty(handler.ApiEvidence.KnownFailureModes);
        }
    }

    [Theory]
    [InlineData(FeatureTypes.Fillet)]
    [InlineData(FeatureTypes.Chamfer)]
    [InlineData(FeatureTypes.LinearPattern)]
    [InlineData(FeatureTypes.CircularPattern)]
    [InlineData(FeatureTypes.Mirror)]
    public void VerifiedComplexFeatureCarriesRealExecutionEvidenceAfterItsDiagnostic(string featureType)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        Assert.True(registry.TryGetHandler(featureType, out var handler));

        // 已取证的 Feature 必须携带完整绑定，而不是只把状态改成 verified。
        Assert.Equal(FeatureApiEvidenceStatuses.Verified, handler.ApiEvidence.Status, ignoreCase: true);
        Assert.True(handler.ApiEvidence.AllowsRealExecution);
        Assert.False(string.IsNullOrWhiteSpace(handler.ApiEvidence.EvidenceId));
        Assert.False(string.IsNullOrWhiteSpace(handler.ApiEvidence.DiagnosticRunPath));
        Assert.False(string.IsNullOrWhiteSpace(handler.ApiEvidence.SourceRevision));
        Assert.Equal("31.5.0", handler.ApiEvidence.SolidWorksVersion);

        var evidence = handler.ValidateEvidenceForRealExecution(ValidFeature(featureType));
        Assert.True(evidence.IsValid, string.Join(Environment.NewLine, evidence.Issues));
    }

    [Theory]
    [InlineData(FeatureTypes.Fillet, "radius_mm", "0")]
    [InlineData(FeatureTypes.Fillet, "radius_mm", "NaN")]
    [InlineData(FeatureTypes.Fillet, "edge_selection", "top_outer_edges")]
    [InlineData(FeatureTypes.Fillet, "edge_selection", "{}")]
    [InlineData(FeatureTypes.Chamfer, "angle_deg", "90")]
    [InlineData(FeatureTypes.Chamfer, "distance_mm", "-1")]
    [InlineData(FeatureTypes.Chamfer, "edge_selection", "top_outer_edges")]
    [InlineData(FeatureTypes.Chamfer, "edge_selection", "{}")]
    [InlineData(FeatureTypes.LinearPattern, "instance_count", "1")]
    [InlineData(FeatureTypes.LinearPattern, "direction", "diagonal")]
    [InlineData(FeatureTypes.LinearPattern, "direction_selection", "along_length")]
    [InlineData(FeatureTypes.CircularPattern, "angle_deg", "361")]
    [InlineData(FeatureTypes.CircularPattern, "axis", "w")]
    [InlineData(FeatureTypes.CircularPattern, "axis_selection", "center_hole")]
    [InlineData(FeatureTypes.Mirror, "mirror_plane", "SomeFace")]
    public void ComplexFeatureRejectsOutOfProfileParameters(
        string featureType,
        string parameterName,
        string invalidValue)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        Assert.True(registry.TryGetHandler(featureType, out var handler));

        var parameters = new Dictionary<string, string>(
            ValidParameters(featureType),
            StringComparer.OrdinalIgnoreCase)
        {
            [parameterName] = invalidValue
        };

        var validation = handler.Validate(Feature(featureType, parameters));

        Assert.False(validation.IsValid);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, validation.FailureStage);
    }

    [Theory]
    [InlineData(FeatureTypes.Fillet)]
    [InlineData(FeatureTypes.Chamfer)]
    [InlineData(FeatureTypes.LinearPattern)]
    [InlineData(FeatureTypes.CircularPattern)]
    [InlineData(FeatureTypes.Mirror)]
    public void ComplexFeatureRejectsUnknownParameters(string featureType)
    {
        var registry = FeatureHandlerRegistry.CreateDefault();
        Assert.True(registry.TryGetHandler(featureType, out var handler));

        var parameters = new Dictionary<string, string>(
            ValidParameters(featureType),
            StringComparer.OrdinalIgnoreCase)
        {
            ["undeclared_parameter"] = "1"
        };

        var validation = handler.Validate(Feature(featureType, parameters));

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void UnknownFeatureTypeIsRejectedByEveryComplexHandler()
    {
        var registry = FeatureHandlerRegistry.CreateDefault();

        Assert.False(registry.TryGetHandler("no_such_feature", out _));

        foreach (var featureType in ComplexFeatureTypes)
        {
            Assert.True(registry.TryGetHandler(featureType, out var handler));
            var validation = handler.Validate(
                new FeatureDefinition("alien", "no_such_feature", ValidParameters(featureType)));

            Assert.False(validation.IsValid);
            Assert.Equal(PartFamilyFailureStages.UnsupportedFeatureType, validation.FailureStage);
        }
    }

    private static FeatureDefinition ValidFeature(string featureType) =>
        Feature(featureType, ValidParameters(featureType));

    private static FeatureDefinition Feature(
        string featureType,
        IReadOnlyDictionary<string, string> parameters) =>
        new(
            $"{featureType}_1",
            featureType,
            parameters,
            referencedFeatures: ["seed_boss"]);

    private static IReadOnlyDictionary<string, string> ValidParameters(string featureType) =>
        featureType switch
        {
            FeatureTypes.Fillet => new Dictionary<string, string>
            {
                ["radius_mm"] = "3",
                // 结构化判据：顶面孔口（圆边 + 圆柱面/平面相交 + y=10），恰好两条。
                ["edge_selection"] = FilletEdgeCriteriaJson
            },
            FeatureTypes.Chamfer => new Dictionary<string, string>
            {
                ["distance_mm"] = "2",
                ["angle_deg"] = "45",
                ["edge_selection"] = ChamferEdgeCriteriaJson
            },
            FeatureTypes.LinearPattern => new Dictionary<string, string>
            {
                ["direction"] = "x",
                ["direction_selection"] = DirectionEdgeCriteriaJson,
                ["instance_count"] = "4",
                ["spacing_mm"] = "20",
                ["seed_feature"] = "seed_boss"
            },
            FeatureTypes.CircularPattern => new Dictionary<string, string>
            {
                ["axis"] = "y",
                ["axis_selection"] = AxisEdgeCriteriaJson,
                ["instance_count"] = "6",
                ["angle_deg"] = "360",
                ["seed_feature"] = "seed_boss"
            },
            FeatureTypes.Mirror => new Dictionary<string, string>
            {
                ["mirror_plane"] = "FrontPlane",
                ["target_features"] = "seed_boss"
            },
            _ => throw new ArgumentOutOfRangeException(nameof(featureType))
        };
}
