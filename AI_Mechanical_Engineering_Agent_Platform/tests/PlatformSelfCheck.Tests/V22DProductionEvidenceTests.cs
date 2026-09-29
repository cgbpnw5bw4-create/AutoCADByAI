using System.Text.Json;
using DomainSchemas;
using PlatformCore;
using SolidWorksWorker;
using SolidWorksWorker.Features;

namespace PlatformSelfCheck.Tests;

public sealed class V22DProductionEvidenceTests
{
    [Theory]
    [InlineData("real_cad_shaft_request.json")]
    [InlineData("real_cad_shaft_plain_request.json")]
    [InlineData("real_cad_jacket_request.json")]
    public void CurrentProductionSamplesRequirePhysicalCadEvidenceAndMatchingPlans(string input)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FeatureExecutionEvidencePolicy.ResolveEvidencePath("examples/" + input)));
        var spec = document.RootElement.GetProperty("cad_model_spec").Deserialize<CADModelSpec>()!;
        var definition = PartTypeRegistry.CreateDefault().GetDefinition(spec.PartType)!;
        var plan = definition.GenerateBuildPlan("production-evidence-regression", spec).BuildPlan!;
        var result = PartFamilyProductionEvidencePolicy.ValidateForRealExecution(plan, actualVersion: "31.5.0");
        Assert.True(result.IsValid, string.Join("; ", result.Issues));
        Assert.True(PartFamilyBuilderRegistry.CreateDefault().TryGetBuilder(spec.PartType, out var builder));
        Assert.True(builder.SupportsRealExecution);
        Assert.False(PartFamilyProductionEvidencePolicy.ValidateForRealExecution(plan, actualVersion: "unverified-version").IsValid);
    }

    [Fact]
    public async Task GlobalSelfCheckPassesWithCurrentShaftAndJacketEvidence()
    {
        var output = Path.Combine(Path.GetTempPath(), "v22d-self-check", Guid.NewGuid().ToString("N"));
        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(PlatformBootstrapper.CreateDefault(), output);
            Assert.Equal("Passed", report.FinalStatus);
            Assert.True(report.ShaftRealWorkflowSupported);
            Assert.True(report.JacketRealWorkflowSupported);
            Assert.True(report.JacketProductionEvidenceActive);
            Assert.False(report.V21ARealExecutionFrozen);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }
}
