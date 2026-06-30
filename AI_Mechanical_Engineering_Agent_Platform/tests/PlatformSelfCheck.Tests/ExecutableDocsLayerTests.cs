using System.Text.Json;
using PlatformCore;

namespace PlatformSelfCheck.Tests;

public sealed class ExecutableDocsLayerTests
{
    [Theory]
    [InlineData("docs/index.md")]
    [InlineData("docs/project_execution_standard.md")]
    [InlineData("docs/module_document_standard.md")]
    [InlineData("docs/step_execution_standard.md")]
    [InlineData("docs/failure_repair_standard.md")]
    [InlineData("docs/codex_execution_protocol.md")]
    [InlineData("docs/claude_review_protocol.md")]
    [InlineData("docs/version_stage_index.md")]
    [InlineData("docs/codex_agent_team_guide.md")]
    [InlineData("AGENTS.md")]
    [InlineData(".codex/config.example.toml")]
    [InlineData(".agents/skills/solidworks-api-repair/SKILL.md")]
    [InlineData(".agents/skills/markdown-docs-standard/SKILL.md")]
    [InlineData(".agents/skills/quality-review/SKILL.md")]
    [InlineData("src/Modules/CADModeling/execution.md")]
    [InlineData("src/Modules/CADModeling/failure_repair.md")]
    [InlineData("src/Modules/CADModeling/api_evidence.md")]
    [InlineData("src/Modules/CADModeling/review_checklist.md")]
    [InlineData("src/Workers/SolidWorks/execution.md")]
    [InlineData("src/Workers/SolidWorks/failure_repair.md")]
    [InlineData("src/Workers/SolidWorks/api_evidence.md")]
    [InlineData("src/Workers/SolidWorks/review_checklist.md")]
    public void RequiredExecutableDocumentationExists(string relativePath)
    {
        Assert.True(File.Exists(Path.Combine(FindProjectRoot(), Normalize(relativePath))), relativePath);
    }

    [Theory]
    [InlineData(".codex/agents/project-manager.toml", "project_manager", "read-only")]
    [InlineData(".codex/agents/code-mapper.toml", "code_mapper", "read-only")]
    [InlineData(".codex/agents/api-researcher.toml", "api_researcher", "read-only")]
    [InlineData(".codex/agents/quality-gate.toml", "quality_gate", "read-only")]
    [InlineData(".codex/agents/cad-worker.toml", "cad_worker", "workspace-write")]
    [InlineData(".codex/agents/docs-writer.toml", "docs_writer", "workspace-write")]
    public void CodexAgentConfigurationUsesExpectedNameAndSandbox(string relativePath, string name, string sandboxMode)
    {
        var text = File.ReadAllText(Path.Combine(FindProjectRoot(), Normalize(relativePath)));

        Assert.Contains($"name = \"{name}\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"sandbox_mode = \"{sandboxMode}\"", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AgentsMarkdownDistinguishesCodexAgentRuntimeAgentAndModule()
    {
        var text = File.ReadAllText(Path.Combine(FindProjectRoot(), "AGENTS.md"));

        Assert.Contains("Codex Agent", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runtime Agent", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Module", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不能替代 `src/Modules`", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不能直接调用 `Worker`", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CodexAgentTeamGuideDoesNotReplaceProjectModules()
    {
        var text = File.ReadAllText(Path.Combine(FindProjectRoot(), "docs", "codex_agent_team_guide.md"));

        Assert.Contains("不能替代 `src/Modules`", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不能直接执行真实 CAD", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SolidWorks API 失败流程", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelfCheckReportContainsExecutableDocsLayerFields()
    {
        var root = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_docs_layer", Guid.NewGuid().ToString("N"));
        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, root);
            var reportJson = await File.ReadAllTextAsync(Path.Combine(outputRoot, "reports", "platform_self_check_report.json"));
            using var document = JsonDocument.Parse(reportJson);
            var rootElement = document.RootElement;

            Assert.True(report.ExecutableDocsLayerEnabled);
            Assert.True(report.DocsIndexExists);
            Assert.True(report.ProjectExecutionStandardExists);
            Assert.True(report.ModuleDocumentStandardExists);
            Assert.True(report.StepExecutionStandardExists);
            Assert.True(report.FailureRepairStandardExists);
            Assert.True(report.CodexExecutionProtocolExists);
            Assert.True(report.ClaudeReviewProtocolExists);
            Assert.True(report.VersionStageIndexExists);
            Assert.True(report.CodexAgentTeamGuideExists);
            Assert.True(report.AgentsMdExists);
            Assert.True(report.CodexAgentsConfigured);
            Assert.True(report.CodexConfigExampleExists);
            Assert.True(report.CodexProjectManagerAgentExists);
            Assert.True(report.CodexCodeMapperAgentExists);
            Assert.True(report.CodexApiResearcherAgentExists);
            Assert.True(report.CodexCadWorkerAgentExists);
            Assert.True(report.CodexQualityGateAgentExists);
            Assert.True(report.CodexDocsWriterAgentExists);
            Assert.True(report.CodexAgentsDoNotReplaceProjectModules);
            Assert.True(report.CodexAgentsRespectWorkerBoundaries);
            Assert.True(report.AgentsSkillsDirectoryExists);
            Assert.True(report.SolidWorksApiRepairSkillExists);
            Assert.True(report.MarkdownDocsStandardSkillExists);
            Assert.True(report.QualityReviewSkillExists);
            Assert.True(report.CadModelingExecutionDocExists);
            Assert.True(report.CadModelingFailureRepairDocExists);
            Assert.True(report.CadModelingApiEvidenceDocExists);
            Assert.True(report.CadModelingReviewChecklistExists);
            Assert.True(report.SolidWorksWorkerExecutionDocExists);
            Assert.True(report.SolidWorksWorkerFailureRepairDocExists);
            Assert.True(report.SolidWorksWorkerApiEvidenceDocExists);
            Assert.True(report.SolidWorksWorkerReviewChecklistExists);
            Assert.True(report.MarkdownChineseCheckPassed);
            Assert.Equal("Passed", report.FinalStatus);

            Assert.True(rootElement.TryGetProperty("executable_docs_layer_enabled", out _));
            Assert.True(rootElement.TryGetProperty("codex_agents_configured", out _));
            Assert.True(rootElement.TryGetProperty("solidworks_worker_api_evidence_doc_exists", out _));
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static string Normalize(string relativePath) =>
        relativePath.Replace('/', Path.DirectorySeparatorChar);

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate project root.");
    }
}
