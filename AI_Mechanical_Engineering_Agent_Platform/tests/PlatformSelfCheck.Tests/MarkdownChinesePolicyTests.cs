using PlatformCore;
using QualityGate;

namespace PlatformSelfCheck.Tests;

public sealed class MarkdownChinesePolicyTests
{
    [Fact]
    public void MarkdownStandardAndExternalReferenceDocumentsExist()
    {
        var root = FindProjectRoot();

        Assert.True(File.Exists(Path.Combine(root, "docs", "markdown_standard.md")));
        Assert.True(File.Exists(Path.Combine(root, "references", "external", "README.md")));
        Assert.True(File.Exists(Path.Combine(root, "references", "external", "solidworks-automation-skill-analysis.md")));
        Assert.True(File.Exists(Path.Combine(root, "THIRD_PARTY_NOTICES.md")));
    }

    [Fact]
    public void MarkdownChineseValidatorIgnoresEnglishInsideCodeBlocks()
    {
        var root = CreateTemporaryMarkdownRoot(new Dictionary<string, string>
        {
            ["README.md"] =
                """
                # 中文说明

                这是中文说明段落，用于解释平台边界和使用方式。

                ```csharp
                public sealed class EnglishIdentifier
                {
                    public string ExecuteAsync() => "This English text is code";
                }
                ```

                代码块中的英文标识符允许保留。
                """
        });

        try
        {
            var report = new MarkdownChineseValidator().Validate(root);

            Assert.Equal("Passed", report.FinalStatus);
            Assert.Equal(1, report.ScannedFiles);
            Assert.Equal(1, report.PassedFiles);
            Assert.Empty(report.FailedFiles);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkdownChineseValidatorDetectsEnglishHeavyExplanation()
    {
        var root = CreateTemporaryMarkdownRoot(new Dictionary<string, string>
        {
            ["docs/english.md"] =
                """
                # English Heavy Document

                This document is mostly English explanation text. It describes architecture,
                modules, workers, validators, reviewers, gateway behavior, runtime behavior,
                and self-check rules without enough Chinese explanation.
                """
        });

        try
        {
            var report = new MarkdownChineseValidator().Validate(root);

            Assert.Equal("Failed", report.FinalStatus);
            Assert.Single(report.FailedFiles);
            Assert.NotEmpty(report.EnglishHeavySections);
            Assert.Contains(report.Issues, issue => issue.Contains("英文比例过高", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkdownChineseValidatorSupportsConfigurableThresholds()
    {
        var root = CreateTemporaryMarkdownRoot(new Dictionary<string, string>
        {
            ["README.md"] =
                """
                # 中文说明

                This short English explanation is normally below the default threshold.
                """
        });

        try
        {
            var defaultReport = new MarkdownChineseValidator().Validate(root);
            var strictReport = new MarkdownChineseValidator(new MarkdownChineseValidatorOptions(MinEnglishLetters: 10)).Validate(root);

            Assert.Equal("Passed", defaultReport.FinalStatus);
            Assert.Equal("Failed", strictReport.FinalStatus);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkdownChineseValidatorSupportsFileLevelExemptAndWarningMarkers()
    {
        var root = CreateTemporaryMarkdownRoot(new Dictionary<string, string>
        {
            ["LICENSE.md"] =
                """
                <!-- markdown-lang: exempt -->
                # License

                This file intentionally keeps original English license text. This paragraph
                is long enough to be detected as English-heavy if the file marker is ignored.
                The validator must skip it because third-party license originals are allowed.
                """,
            ["docs/draft.md"] =
                """
                <!-- markdown-lang: warning -->
                # Draft

                This draft intentionally contains English explanation while a new document is
                being prepared. It should be reported as a warning instead of a failed file,
                so development can continue while the warning remains visible.
                """
        });

        try
        {
            var report = new MarkdownChineseValidator().Validate(root);

            Assert.Equal("Warning", report.FinalStatus);
            Assert.Empty(report.FailedFiles);
            Assert.Contains(report.Warnings, warning => warning.Contains("docs/draft.md", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(report.Issues, issue => issue.Contains("LICENSE.md", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarkdownChineseValidatorDoesNotTreatSlashSeparatedTermsAsPaths()
    {
        var root = CreateTemporaryMarkdownRoot(new Dictionary<string, string>
        {
            ["docs/cad.md"] =
                """
                # CAD/BIM/CAE Integration

                This module handles CAD/BIM/CAE integration and explains architecture behavior,
                workflow behavior, validation behavior, reviewer behavior, gateway behavior,
                runtime behavior, documentation behavior, and automation boundaries in English.
                """
        });

        try
        {
            var report = new MarkdownChineseValidator().Validate(root);

            Assert.Equal("Failed", report.FinalStatus);
            Assert.Single(report.FailedFiles);
            Assert.Contains(report.EnglishHeavySections, section => section.Contains("docs/cad.md", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SelfCheckReportContainsMarkdownChineseFields()
    {
        var root = FindProjectRoot();
        var platform = PlatformBootstrapper.CreateDefault(root);
        var outputRoot = Path.Combine(Path.GetTempPath(), "ai_me_self_check_markdown", Guid.NewGuid().ToString("N"));

        try
        {
            var report = await PlatformSelfCheckRunner.RunAsync(platform, outputRoot, root);

            Assert.True(report.MarkdownChineseStandardExists);
            Assert.True(report.MarkdownFilesScanned > 0);
            Assert.True(report.MarkdownChineseValidatorEnabled);
            Assert.True(report.MarkdownChineseCheckPassed);
            Assert.True(report.MarkdownLanguageReportGenerated);
            Assert.True(report.MarkdownEnglishExceptionsSupported);
            Assert.Equal("Passed", report.FinalStatus);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void SolidWorksWorkerReadmeKeepsDryRunBoundaryAndNoExternalScriptCopy()
    {
        var root = FindProjectRoot();
        var readme = File.ReadAllText(Path.Combine(root, "src", "Workers", "SolidWorks", "README.md"));

        Assert.Contains("dry-run skeleton", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("不得复制", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("scripts", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Worker", readme, StringComparison.Ordinal);
        Assert.Contains("QualityGate", readme, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleYamlDescriptionsUseChineseExplanatoryText()
    {
        var root = FindProjectRoot();
        var moduleYamlFiles = Directory.GetFiles(Path.Combine(root, "src", "Modules"), "module.yaml", SearchOption.AllDirectories);

        Assert.NotEmpty(moduleYamlFiles);
        foreach (var file in moduleYamlFiles)
        {
            var descriptionLine = File.ReadLines(file)
                .Single(line => line.StartsWith("description:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(descriptionLine, character => character >= '\u4e00' && character <= '\u9fff');
        }
    }

    [Fact]
    public void ProjectReadmeAndDocsAreMostlyChinese()
    {
        var root = FindProjectRoot();
        var report = new MarkdownChineseValidator().Validate(root);

        Assert.Equal("Passed", report.FinalStatus);
        Assert.True(report.ScannedFiles > 0);
        Assert.Empty(report.FailedFiles);
    }

    private static string CreateTemporaryMarkdownRoot(IReadOnlyDictionary<string, string> files)
    {
        var root = Path.Combine(Path.GetTempPath(), "markdown_chinese_validator", Guid.NewGuid().ToString("N"));
        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        return root;
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
