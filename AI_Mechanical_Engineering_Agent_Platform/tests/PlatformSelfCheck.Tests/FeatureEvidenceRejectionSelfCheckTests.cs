using System.Text.Json;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class FeatureEvidenceRejectionSelfCheckTests
{
    private static readonly System.Reflection.Assembly WorkerAssembly = typeof(FeatureHandlerRegistry).Assembly;
    private static FeatureDefinition Sample(string type) => type == FeatureTypes.RevolveBoss
        ? FeatureEvidenceRejectionSelfCheck.RevolveSample() : FeatureEvidenceRejectionSelfCheck.FilletSample();

    [Theory]
    [InlineData(FeatureTypes.RevolveBoss)]
    [InlineData(FeatureTypes.Fillet)]
    public void AllVerifiedRegistryStillObservesRealUnverifiedRejectionWithoutChangingProduction(string type)
    {
        var created = new List<FeatureHandlerRegistry>();
        var states = new List<string>();
        var result = FeatureEvidenceRejectionSelfCheck.Run(WorkerAssembly, [Sample(type)],
            (registry, plan) =>
            {
                var actual = (FeatureHandlerRegistry)registry;
                Assert.True(actual.TryGetHandler(type, out var handler));
                states.Add(handler.ApiEvidence.Status);
                return actual.ValidateForRealExecution(plan);
            },
            () =>
            {
                var registry = FeatureHandlerRegistry.CreateDefault();
                Assert.All(registry.GetAll(), handler => Assert.Equal("verified", handler.ApiEvidence.Status));
                created.Add(registry);
                return registry;
            });
        Assert.True(result.Passed, string.Join("; ", result.Issues));
        Assert.Equal(1, result.ObservedNegativeSamples);
        Assert.Equal(["verified", "unverified"], states);
        Assert.Empty(result.Issues);
        Assert.All(created.SelectMany(registry => registry.GetAll()), handler => Assert.Equal("verified", handler.ApiEvidence.Status));
        Assert.All(FeatureHandlerRegistry.CreateDefault().GetAll(), handler => Assert.Equal("verified", handler.ApiEvidence.Status));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("no_registry")]
    [InlineData("shared_evidence")]
    [InlineData("not_invoked")]
    [InlineData("missing_target")]
    [InlineData("no_negative_result")]
    [InlineData("invalid_parameters")]
    public void NoUsableNegativeObservationIsNullAndCannotPassTheCapabilityBaseline(string failure)
    {
        var samples = failure == "empty" ? Array.Empty<FeatureDefinition>() : [Sample(FeatureTypes.RevolveBoss)];
        if (failure == "invalid_parameters")
            samples = [new FeatureDefinition("bad", FeatureTypes.RevolveBoss, new Dictionary<string, string>())];
        var shared = FeatureHandlerRegistry.CreateDefault();
        var result = FeatureEvidenceRejectionSelfCheck.Run(WorkerAssembly, samples,
            (registry, plan) =>
            {
                if (failure == "not_invoked") return null;
                var actual = ((FeatureHandlerRegistry)registry).ValidateForRealExecution(plan);
                if (!actual.IsPassed && failure == "no_negative_result") return null;
                return !actual.IsPassed && failure == "missing_target" ? actual with { Features = [] } : actual;
            },
            () => failure == "no_registry" ? null : failure == "shared_evidence" ? shared : FeatureHandlerRegistry.CreateDefault());
        Assert.Null(result.Passed);
        Assert.Equal(0, result.ObservedNegativeSamples);
        Assert.NotEmpty(result.Issues);
        Assert.All(shared.GetAll(), handler => Assert.Equal("verified", handler.ApiEvidence.Status));

        var root = PlatformPathResolver.FindProjectRoot();
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, SelfCheckCapabilityRegressionGate.BaselineRelativePath)));
        var snapshot = baseline.RootElement.GetProperty("protected_capabilities").EnumerateObject()
            .ToDictionary(item => item.Name, _ => true);
        foreach (var field in new[] { "unverified_api_blocks_real_execution", "unverified_feature_blocks_execution" })
            FeatureEvidenceRejectionSelfCheck.AddObserved(snapshot, field, result.Passed);
        var gate = SelfCheckCapabilityRegressionGate.Evaluate(root, snapshot);
        Assert.False(gate.Passed);
        Assert.Empty(gate.RegressedFields);
        Assert.Contains(gate.ConfigurationErrors, error => error.Contains("unverified_api_blocks_real_execution") && error.Contains("not observed"));
        Assert.Contains(gate.ConfigurationErrors, error => error.Contains("unverified_feature_blocks_execution") && error.Contains("not observed"));
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("wrong_stage")]
    public void BrokenRegistryRejectionIsFalseEvenWhenAnotherSampleIsUnobserved(string failure)
    {
        var samples = new[] { Sample(FeatureTypes.Fillet), new FeatureDefinition("missing", "unregistered", new Dictionary<string, string>()) };
        var result = FeatureEvidenceRejectionSelfCheck.Run(WorkerAssembly, samples,
            (registry, plan) =>
            {
                var actualRegistry = (FeatureHandlerRegistry)registry;
                var actual = actualRegistry.ValidateForRealExecution(plan);
                actualRegistry.TryGetHandler(FeatureTypes.Fillet, out var handler);
                if (handler.ApiEvidence.Status != "unverified") return actual;
                return failure == "accepted"
                    ? actual with { IsPassed = true, FailureStage = null, Issues = [] }
                    : actual with { FailureStage = PartFamilyFailureStages.InvalidFeatureParameter };
            });
        Assert.False(result.Passed);
        Assert.Equal(1, result.ObservedNegativeSamples);
        Assert.Contains(result.Issues, issue => issue.Contains("未以 feature_api_unverified"));
        Assert.Contains(result.Issues, issue => issue.Contains("未观测负样本"));
    }

    [Fact]
    public void APassingNegativeSampleCannotHideAnotherUnobservedSample()
    {
        var result = FeatureEvidenceRejectionSelfCheck.Run(WorkerAssembly,
            [Sample(FeatureTypes.Fillet), new FeatureDefinition("missing", "unregistered", new Dictionary<string, string>())]);
        Assert.Null(result.Passed);
        Assert.Equal(1, result.ObservedNegativeSamples);
        Assert.Contains(result.Issues, issue => issue.Contains("未观测负样本"));
    }

    [Fact]
    public void ExceptionDuringNegativePreflightRestoresEvidenceAndDoesNotClaimSuccess()
    {
        FeatureHandlerRegistry? probeRegistry = null;
        var result = FeatureEvidenceRejectionSelfCheck.Run(WorkerAssembly, [Sample(FeatureTypes.RevolveBoss)],
            (registry, plan) =>
            {
                probeRegistry = (FeatureHandlerRegistry)registry;
                probeRegistry.TryGetHandler(FeatureTypes.RevolveBoss, out var handler);
                if (handler.ApiEvidence.Status == "unverified") throw new InvalidOperationException("注入预检异常");
                return probeRegistry.ValidateForRealExecution(plan);
            });
        Assert.Null(result.Passed);
        Assert.Equal(0, result.ObservedNegativeSamples);
        Assert.Contains(result.Issues, issue => issue.Contains("注入预检异常"));
        Assert.All(probeRegistry!.GetAll(), handler => Assert.Equal("verified", handler.ApiEvidence.Status));
    }

    [Fact]
    public async Task GlobalReportUsesBothNegativeObservationsAndSerializesUnknownAsNull()
    {
        var output = Path.Combine(Path.GetTempPath(), "high002-self-check", Guid.NewGuid().ToString("N"));
        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(PlatformBootstrapper.CreateDefault(), output);
            Assert.True(report.UnverifiedApiBlocksRealExecution);
            Assert.True(report.UnverifiedFeatureBlocksExecution);
            Assert.Equal(1, report.UnverifiedEvidenceNegativeSamples["unverified_api_blocks_real_execution"]);
            Assert.Equal(1, report.UnverifiedEvidenceNegativeSamples["unverified_feature_blocks_execution"]);
            Assert.Empty(report.UnverifiedEvidenceSelfCheckIssues);
            Assert.True(report.V20ECapabilityRegressionGatePassed);
            using var unknown = JsonDocument.Parse(JsonSerializer.Serialize(report with
            {
                UnverifiedApiBlocksRealExecution = null, UnverifiedFeatureBlocksExecution = null
            }));
            Assert.Equal(JsonValueKind.Null, unknown.RootElement.GetProperty("unverified_api_blocks_real_execution").ValueKind);
            Assert.Equal(JsonValueKind.Null, unknown.RootElement.GetProperty("unverified_feature_blocks_execution").ValueKind);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }
}
