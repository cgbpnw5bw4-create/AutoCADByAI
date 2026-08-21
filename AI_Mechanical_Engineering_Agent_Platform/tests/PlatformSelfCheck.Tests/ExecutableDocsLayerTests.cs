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
    [InlineData("docs/codex_agent_registry.md")]
    [InlineData("docs/codex_agent_governance.md")]
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
    public void CodexAgentRegistryAndGovernanceDocumentCanonicalReusePolicy()
    {
        var root = FindProjectRoot();
        var registryText = File.ReadAllText(Path.Combine(root, "docs", "codex_agent_registry.md"));
        var governanceText = File.ReadAllText(Path.Combine(root, "docs", "codex_agent_governance.md"));
        var agentsText = File.ReadAllText(Path.Combine(root, "AGENTS.md"));
        var protocolText = File.ReadAllText(Path.Combine(root, "docs", "codex_execution_protocol.md"));

        foreach (var agent in CanonicalAgentNames)
        {
            Assert.Contains($"`{agent}`", registryText, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("不允许重复创建同职责 Agent", governanceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agents/skills/solidworks-api-repair/SKILL.md", governanceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agents/skills/quality-review/SKILL.md", governanceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".agents/skills/markdown-docs-standard/SKILL.md", governanceText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex Agent 复用规则", agentsText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("canonical agent", protocolText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveCodexAgentsOnlyContainCanonicalAgentsWithoutDuplicates()
    {
        var agentDirectory = Path.Combine(FindProjectRoot(), ".codex", "agents");
        var activeAgentFiles = Directory
            .GetFiles(agentDirectory, "*.toml", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .ToArray();
        var activeAgentNames = Directory
            .GetFiles(agentDirectory, "*.toml", SearchOption.TopDirectoryOnly)
            .Select(path => ExtractAgentName(File.ReadAllText(path)))
            .ToArray();

        Assert.Equal(CanonicalAgentFiles.Length, activeAgentFiles.Length);
        Assert.True(activeAgentFiles.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(CanonicalAgentFiles));
        Assert.True(activeAgentNames.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(CanonicalAgentNames));
        Assert.Equal(activeAgentNames.Length, activeAgentNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(activeAgentFiles, file => file?.Contains("api_researcher", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(activeAgentFiles, file => file?.Contains("code_mapper", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(activeAgentFiles, file => file?.Contains("quality_gate", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(activeAgentFiles, file => file?.Contains("docs_writer", StringComparison.OrdinalIgnoreCase) == true);
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
            Assert.True(report.CodexAgentRegistryExists);
            Assert.True(report.CodexAgentGovernanceDocExists);
            Assert.True(report.AgentsMdExists);
            Assert.True(report.CodexAgentsConfigured);
            Assert.True(report.CodexAgentRegistryListsCanonicalAgents);
            Assert.True(report.CodexNoDuplicateActiveAgents);
            Assert.True(report.CodexAgentReusePolicyDocumented);
            Assert.True(report.CodexAgentNewRequirementsGoToSkillsOrDocs);
            Assert.True(report.CodexActiveAgentCountIsExpected);
            Assert.True(report.CodexOnlyCanonicalAgentsActive);
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

            Assert.True(rootElement.TryGetProperty("executable_docs_layer_enabled", out _));
            Assert.True(rootElement.TryGetProperty("codex_agents_configured", out _));
            Assert.True(rootElement.TryGetProperty("codex_agent_registry_exists", out _));
            Assert.True(rootElement.TryGetProperty("codex_agent_governance_doc_exists", out _));
            Assert.True(rootElement.TryGetProperty("codex_only_canonical_agents_active", out _));
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

    private static readonly string[] CanonicalAgentNames =
    [
        "project_manager",
        "code_mapper",
        "api_researcher",
        "cad_worker",
        "quality_gate",
        "docs_writer"
    ];

    private static readonly string[] CanonicalAgentFiles =
    [
        "project-manager.toml",
        "code-mapper.toml",
        "api-researcher.toml",
        "cad-worker.toml",
        "quality-gate.toml",
        "docs-writer.toml"
    ];

    private static string ExtractAgentName(string toml)
    {
        foreach (var line in toml.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            return trimmed[(separatorIndex + 1)..].Trim().Trim('"');
        }

        return string.Empty;
    }

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
