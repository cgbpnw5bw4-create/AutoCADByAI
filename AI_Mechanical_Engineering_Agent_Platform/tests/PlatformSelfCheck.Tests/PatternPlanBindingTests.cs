using System.Text.Json;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class PatternPlanBindingTests
{
    [Theory]
    [InlineData("feature_pipeline_plate_linear_pattern.json")]
    [InlineData("feature_pipeline_plate_circular_pattern.json")]
    public void ExistingSampleRetainsOriginalSeedReferenceAcrossCompilationAndPreflight(string file)
    {
        var (spec, plan, operation) = Compile(file);
        var original = Assert.Single(spec.Features, IsPattern);
        var adapted = FeatureHandlerPlanAdapter.Adapt(operation);
        Assert.NotNull(adapted.Feature);
        Assert.Equal(original.ReferencedFeatures, adapted.Feature!.ReferencedFeatures);
        var result = DefinitionOnlyRegistry().ValidateForRealExecution(plan);
        Assert.True(result.IsPassed, string.Join(Environment.NewLine, result.Issues));

        var handler = FeatureHandlerRegistry.CreateDefault().Resolve(original).Handler!;
        var handlerPlan = handler.BuildPlan(original, new("direct-plan", string.Empty, operation.DependsOn));
        Assert.NotNull(handlerPlan.Operation);
        Assert.Equal(original.ReferencedFeatures, FeatureHandlerPlanAdapter.Adapt(handlerPlan.Operation!).Feature!.ReferencedFeatures);
    }

    [Theory]
    [InlineData("feature_pipeline_plate_linear_pattern.json")]
    [InlineData("feature_pipeline_plate_circular_pattern.json")]
    public void MissingReferenceMetadataCannotBeReconstructedFromSeedText(string file)
    {
        var (_, plan, operation) = Compile(file);
        var parameters = operation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        parameters.Remove("referenced_features");
        var changed = operation with { Parameters = parameters };
        Assert.Empty(FeatureHandlerPlanAdapter.Adapt(changed).Feature!.ReferencedFeatures);
        var result = DefinitionOnlyRegistry().ValidateForRealExecution(Replace(plan, operation, changed));
        Assert.False(result.IsPassed);
        Assert.Contains(result.Issues, issue => issue.Contains("referenced_features", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("\"seed_cut\"")]
    [InlineData("[null]")]
    [InlineData("[1]")]
    [InlineData("[\" \"]")]
    public void MalformedReferenceMetadataFailsClosedDuringAdaptation(string json)
    {
        var (_, _, operation) = Compile("feature_pipeline_plate_linear_pattern.json");
        var parameters = operation.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value);
        parameters["referenced_features"] = json;
        var adapted = FeatureHandlerPlanAdapter.Adapt(operation with { Parameters = parameters });
        Assert.Null(adapted.Feature);
        Assert.Equal(PartFamilyFailureStages.InvalidFeatureParameter, adapted.FailureStage);
    }

    [Theory]
    [InlineData("feature_pipeline_plate_linear_pattern.json", 0)]
    [InlineData("feature_pipeline_plate_linear_pattern.json", 1)]
    [InlineData("feature_pipeline_plate_linear_pattern.json", 2)]
    [InlineData("feature_pipeline_plate_linear_pattern.json", 3)]
    [InlineData("feature_pipeline_plate_circular_pattern.json", 0)]
    [InlineData("feature_pipeline_plate_circular_pattern.json", 1)]
    [InlineData("feature_pipeline_plate_circular_pattern.json", 2)]
    [InlineData("feature_pipeline_plate_circular_pattern.json", 3)]
    public void ReferenceMetadataCannotBypassDirectEarlierOperationDependency(string file, int mutation)
    {
        var (_, plan, operation) = Compile(file);
        var seed = operation.Parameters["seed_feature"];
        var seedOperation = Assert.Single(plan.Operations, candidate => candidate.Parameters.GetValueOrDefault("feature_id") == seed);
        if (mutation == 0)
        {
            plan = Replace(plan, operation, operation with { DependsOn = [] });
        }
        else if (mutation == 1)
        {
            // 用特征标识冒充 operation 标识，不能替代实际执行依赖。
            plan = Replace(plan, operation, operation with { DependsOn = [seed] });
        }
        else if (mutation == 2)
        {
            var operations = plan.Operations.ToList();
            operations.Remove(seedOperation);
            operations.Insert(operations.IndexOf(operation) + 1, seedOperation);
            plan = plan with { Operations = operations };
        }
        else
        {
            var operations = plan.Operations.ToList();
            operations.Insert(operations.IndexOf(operation), seedOperation with { OperationId = "duplicate-seed-operation" });
            plan = plan with { Operations = operations };
        }

        var result = DefinitionOnlyRegistry().ValidateForRealExecution(plan);
        Assert.False(result.IsPassed);
        Assert.Contains(result.Issues, issue => issue.Contains("pattern_seed_dependency_invalid", StringComparison.Ordinal));
    }

    private static (CADModelSpec Spec, SolidWorksBuildPlan Plan, SolidWorksOperation Pattern) Compile(string file)
    {
        var path = Path.Combine(PlatformPathResolver.FindProjectRoot(), "examples", file);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var spec = document.RootElement.GetProperty("cad_model_spec").Deserialize<CADModelSpec>(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true
        })!;
        var compiled = new BuildPlanCompiler().Compile("pattern-binding-contract", spec);
        Assert.NotNull(compiled.BuildPlan);
        var plan = compiled.BuildPlan!;
        return (spec, plan, Assert.Single(plan.Operations, operation =>
            operation.Parameters.TryGetValue("feature_type", out var type) &&
            type is FeatureTypes.LinearPattern or FeatureTypes.CircularPattern));
    }

    private static bool IsPattern(FeatureDefinition feature) => feature.FeatureType is FeatureTypes.LinearPattern or FeatureTypes.CircularPattern;

    private static SolidWorksBuildPlan Replace(SolidWorksBuildPlan plan, SolidWorksOperation original, SolidWorksOperation changed) =>
        plan with { Operations = plan.Operations.Select(operation => operation == original ? changed : operation).ToArray() };

    // 只隔离外部真机证据文件：参数校验和 Registry 图合同仍运行真实代码，不调用 CAD。
    private static FeatureHandlerRegistry DefinitionOnlyRegistry() => new(
        FeatureHandlerRegistry.CreateDefault().GetAll().Cast<FeatureHandlerBase>().Select(handler => new DefinitionOnlyHandler(handler)));

    private sealed class DefinitionOnlyHandler(FeatureHandlerBase inner) : FeatureHandlerBase
    {
        public override string FeatureType => inner.FeatureType;
        public override string OperationType => inner.OperationType;
        public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => inner.ParameterSchema;
        public override FeatureApiEvidence ApiEvidence => inner.ApiEvidence;
        public override FeatureHandlerValidationResult Validate(FeatureDefinition feature) => inner.Validate(feature);
        public override FeatureHandlerValidationResult ValidateEvidenceForRealExecution(FeatureDefinition feature) => inner.Validate(feature);
        public override Task<FeatureHandlerExecutionResult> ExecuteAsync(FeatureHandlerExecutionContext context, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("合同测试禁止 CAD 执行。");
    }
}
