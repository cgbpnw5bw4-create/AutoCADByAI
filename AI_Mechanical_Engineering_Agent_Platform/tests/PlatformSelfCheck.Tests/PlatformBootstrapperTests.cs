using System.Text.Json;
using DomainSchemas;
using PlatformCore;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class PlatformBootstrapperTests
{
    [Fact]
    public void DefaultBootstrapperRegistersOnlyChiefEngineerAsPublicAgent()
    {
        var platform = PlatformBootstrapper.CreateDefault();

        var publicAgents = platform.AgentRegistry.GetPublicAgents().ToArray();
        var internalAgents = platform.AgentRegistry.GetInternalAgents().Select(agent => agent.Id).ToArray();

        Assert.Single(publicAgents);
        Assert.Equal("chief-engineer", publicAgents[0].Id);
        Assert.Equal("机械总工程师", publicAgents[0].Name);
        Assert.Contains("mechanical-designer", internalAgents);
        Assert.Contains("cad-modeler", internalAgents);
        Assert.Contains("drawing-engineer", internalAgents);
        Assert.Contains("drawing-reviewer", internalAgents);
        Assert.Contains("error-diagnosis", internalAgents);
    }

    [Fact]
    public void AgentDirectoryServiceExposesOnlyPublicAgents()
    {
        var platform = PlatformBootstrapper.CreateDefault();
        var directory = new AgentDirectoryService(platform.AgentRegistry);

        var visibleAgents = directory.GetVisibleAgents().ToArray();

        Assert.Single(visibleAgents);
        Assert.Equal("chief-engineer", visibleAgents[0].Id);
        Assert.Equal("@机械总工程师", visibleAgents[0].Mention);
        Assert.Equal("Public", visibleAgents[0].Visibility);
    }

    [Fact]
    public void GatekeeperRejectsFailedReviewReportAndBuildsRejectReport()
    {
        var gatekeeper = new DefaultGatekeeper(new GateDecisionPolicy(), new RejectReportBuilder());
        var review = new ReviewReport(
            ReviewId: "review-001",
            ReviewerId: "drawing-reviewer",
            IsPassed: false,
            Score: 0.42,
            Issues: ["缺少关键尺寸", "标题栏信息不完整"],
            RequiresHumanApproval: false,
            HasFatalError: false);

        var result = gatekeeper.Evaluate(review);

        Assert.Equal(GateDecisionResult.Rejected, result.Decision.Result);
        Assert.NotNull(result.RejectReport);
        Assert.Contains("缺少关键尺寸", result.RejectReport!.Reasons);
    }

    [Fact]
    public async Task SelfCheckRunnerCreatesPassedReportWithGatewayDirectory()
    {
        var platform = PlatformBootstrapper.CreateDefault();
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_tests", Guid.NewGuid().ToString("N"));

        var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot);

        var reportPath = Path.Combine(outputRoot, "reports", "platform_self_check_report.json");
        Assert.True(File.Exists(reportPath));
        Assert.Equal("Passed", report.FinalStatus);
        Assert.Equal("2.0-b", report.SchemaVersion);
        Assert.StartsWith("platform-self-check-", report.RunId, StringComparison.Ordinal);
        Assert.NotEqual(default, report.GeneratedAt);
        Assert.False(string.IsNullOrWhiteSpace(report.SourceRevision));
        Assert.True(report.SolidWorksComFacadeInjectionSupported);
        Assert.True(report.SolidWorksRealAcceptanceProtocolExists);
        Assert.True(report.SolidWorksLatestRealOutputsReportSupported);
        Assert.True(report.V16TestADocumented);
        Assert.True(report.RealCadE2eCliEntryExists);
        Assert.True(report.RealCadE2eStructuredInputSupported);
        Assert.True(report.RealCadE2eUsesChiefEngineerOrchestrator);
        Assert.True(report.RealCadE2eUsesWorkflowEngine);
        Assert.True(report.RealCadE2eUsesSolidWorksRouter);
        Assert.True(report.RealCadE2eCanInvokeRealWorker);
        Assert.True(report.RealCadE2ePassesQualityGate);
        Assert.True(report.RealCadE2eDefaultDisabled);
        Assert.False(report.RealCadE2eRequiresRequestConfirmation);
        Assert.False(report.RealCadE2eRequiresEnvConfirmation);
        Assert.True(report.RealCadE2eReportSupported);
        Assert.True(report.RealCadE2eDeliverableSemanticsSupported);
        Assert.True(report.V17VersionStageDocumented);
        Assert.True(report.RealCadE2eLocalAuthorizationProfileSupported);
        Assert.False(report.RealCadE2eLocalAuthorizationDefaultDisabled);
        Assert.True(report.SolidWorksLocalInteractiveDefaultEnabled);
        Assert.True(report.SolidWorksDisableEnvSupported);
        Assert.True(report.SolidWorksCiExecutionDisabled);
        Assert.True(report.SolidWorksUnitTestExecutionDisabled);
        Assert.True(report.SolidWorksDryRunDisablesRealExecution);
        Assert.True(report.SolidWorksVisibleDefaultTrue);
        Assert.True(report.SolidWorksExecutionEnvironmentProbeSupported);
        Assert.True(report.LegacyEnableFlagNotRequired);
        Assert.True(report.LegacyRequestConfirmationNotRequired);
        Assert.True(report.GenericCadModelSpecV2Supported);
        Assert.True(report.SketchDefinitionSupported);
        Assert.True(report.SketchConstraintsSupported);
        Assert.True(report.FeatureDefinitionSupported);
        Assert.True(report.FeatureGraphSupported);
        Assert.True(report.FeatureGraphCycleDetected);
        Assert.True(report.MissingFeatureDependencyRejected);
        Assert.True(report.BuildPlanCompilerSupported);
        Assert.True(report.PlateUsesGenericFeatureGraph);
        Assert.True(report.FlangeUsesGenericFeatureGraph);
        Assert.True(report.ShaftUsesGenericFeatureGraph);
        Assert.True(report.NoPartSpecificLogicInRealWorker);
        Assert.True(report.V20ADocumented);
        Assert.Equal("chief-engineer", Assert.Single(report.PublicAgents).Id);
        Assert.Equal("chief-engineer", Assert.Single(report.GatewayVisibleAgents).Id);
        Assert.Contains(report.RegisteredWorkers, worker => worker.Name == "FakeSolidWorksWorker");
        Assert.Contains(report.RegisteredWorkers, worker => worker.Name == "FakeAutoCADWorker");

        using var stream = File.OpenRead(reportPath);
        using var document = await JsonDocument.ParseAsync(stream);
        Assert.Equal("Passed", document.RootElement.GetProperty("final_status").GetString());
        Assert.Equal("2.0-b", document.RootElement.GetProperty("schema_version").GetString());
        Assert.Equal(report.RunId, document.RootElement.GetProperty("run_id").GetString());
        Assert.False(document.RootElement.TryGetProperty("solidworks_com_false_success_tests_added", out _));
    }
}
