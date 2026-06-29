using DomainSchemas;
using System.Text;
using System.Text.RegularExpressions;

namespace QualityGate;

public sealed class MarkdownChineseValidator
{
    private const string ExemptMarker = "markdown-lang: exempt";
    private const string WarningMarker = "markdown-lang: warning";

    private static readonly string[] SkippedDirectoryNames =
    [
        ".git",
        "bin",
        "obj",
        "output"
    ];

    private static readonly Regex InlineCodePattern = new("`[^`]*`", RegexOptions.Compiled);
    private static readonly Regex UrlPattern = new(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex WindowsPathPattern = new(@"[A-Za-z]:\\[^\s`]+", RegexOptions.Compiled);
    private static readonly Regex SlashPathPattern = new(@"(?:(?:src|docs|tests|references|output|examples|python_tools)/[^\s`]+|(?:[\w.-]+/)+[\w.-]+\.(?:cs|md|json|yaml|yml|csproj|sln|dll|py|txt|xml|html))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HtmlCommentPattern = new(@"<!--.*?-->", RegexOptions.Compiled);

    private readonly MarkdownChineseValidatorOptions _options;

    public MarkdownChineseValidator()
        : this(new MarkdownChineseValidatorOptions())
    {
    }

    public MarkdownChineseValidator(MarkdownChineseValidatorOptions options)
    {
        _options = options;
    }

    public MarkdownLanguageReport Validate(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return new MarkdownLanguageReport(
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { $"Markdown 根目录不存在：{rootDirectory}" },
                Array.Empty<string>(),
                "Failed");
        }

        var failedFiles = new List<string>();
        var warnings = new List<string>();
        var issues = new List<string>();
        var englishHeavySections = new List<string>();
        string[] files;

        try
        {
            files = Directory
                .EnumerateFiles(rootDirectory, "*.md", SearchOption.AllDirectories)
                .Where(file => !ShouldSkip(file, rootDirectory))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MarkdownLanguageReport(
                0,
                0,
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { $"Markdown 文件扫描失败：{ex.Message}" },
                Array.Empty<string>(),
                "Failed");
        }

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootDirectory, file).Replace('\\', '/');
            string markdown;
            try
            {
                markdown = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failedFiles.Add(relativePath);
                issues.Add($"{relativePath} Markdown 文件读取失败：{ex.Message}");
                continue;
            }

            if (IsExempt(markdown, relativePath))
            {
                continue;
            }

            var warningOnly = IsWarningOnly(markdown);
            var sections = ExtractProseSections(markdown);
            var fileFailed = false;
            var fileWarning = false;

            foreach (var section in sections)
            {
                var cleaned = CleanForLanguageCheck(section.Text);
                if (string.IsNullOrWhiteSpace(cleaned))
                {
                    continue;
                }

                var chineseCount = CountChineseCharacters(cleaned);
                var englishCount = CountEnglishLetters(cleaned);
                if (IsEnglishHeavy(chineseCount, englishCount))
                {
                    var location = $"{relativePath}:{section.StartLine}";
                    englishHeavySections.Add(location);
                    if (warningOnly)
                    {
                        fileWarning = true;
                        warnings.Add($"{location} 普通说明段落英文比例过高，按文件标记记录为 Warning。");
                    }
                    else
                    {
                        fileFailed = true;
                        issues.Add($"{location} 普通说明段落英文比例过高。");
                    }
                }
            }

            if (fileFailed)
            {
                failedFiles.Add(relativePath);
            }
            else if (!fileWarning && sections.Count == 0)
            {
                warnings.Add($"{relativePath} 未发现可检查的普通说明段落。");
            }
        }

        var finalStatus = failedFiles.Count > 0
            ? "Failed"
            : warnings.Count > 0 ? "Warning" : "Passed";

        return new MarkdownLanguageReport(
            files.Length,
            files.Length - failedFiles.Count,
            failedFiles,
            warnings,
            issues,
            englishHeavySections,
            finalStatus);
    }

    public bool SupportsEnglishExceptions()
    {
        var sample =
            """
            # 中文说明

            这里说明平台规则，`IAgent`、`AgentOutput`、`dotnet test` 和 `src/PlatformCore` 属于允许保留的英文标识符。

            ```powershell
            dotnet run --project src/Interfaces/CliHost -- self-check
            ```
            """;

        var tempRoot = Path.Combine(Path.GetTempPath(), "markdown_chinese_exception_check", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(Path.Combine(tempRoot, "README.md"), sample);
            return Validate(tempRoot).FinalStatus == "Passed";
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static bool ShouldSkip(string file, string rootDirectory)
    {
        var relativeParts = Path.GetRelativePath(rootDirectory, file)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        return relativeParts.Any(part => SkippedDirectoryNames.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    private bool IsExempt(string markdown, string relativePath) =>
        markdown.Contains(ExemptMarker, StringComparison.OrdinalIgnoreCase) ||
        _options.ExemptRelativePaths.Contains(relativePath);

    private static bool IsWarningOnly(string markdown) =>
        markdown.Contains(WarningMarker, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<MarkdownSection> ExtractProseSections(string markdown)
    {
        var sections = new List<MarkdownSection>();
        var builder = new StringBuilder();
        var inCodeBlock = false;
        var startLine = 1;
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                Flush();
                continue;
            }

            if (IsProseLine(trimmed))
            {
                if (builder.Length == 0)
                {
                    startLine = index + 1;
                }

                builder.AppendLine(trimmed);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return sections;

        void Flush()
        {
            if (builder.Length == 0)
            {
                return;
            }

            sections.Add(new MarkdownSection(startLine, builder.ToString()));
            builder.Clear();
        }
    }

    private static bool IsProseLine(string line)
    {
        if (line.StartsWith("|", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.StartsWith("---", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static string CleanForLanguageCheck(string text)
    {
        var cleaned = HtmlCommentPattern.Replace(text, " ");
        cleaned = InlineCodePattern.Replace(cleaned, " ");
        cleaned = UrlPattern.Replace(cleaned, " ");
        cleaned = WindowsPathPattern.Replace(cleaned, " ");
        cleaned = SlashPathPattern.Replace(cleaned, " ");
        return cleaned;
    }

    private static int CountChineseCharacters(string text) =>
        text.Count(character => character >= '\u4e00' && character <= '\u9fff');

    private static int CountEnglishLetters(string text) =>
        text.Count(character => (character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'));

    private bool IsEnglishHeavy(int chineseCount, int englishCount)
    {
        if (englishCount < _options.MinEnglishLetters)
        {
            return false;
        }

        if (chineseCount == 0)
        {
            return true;
        }

        var ratio = englishCount / (double)(englishCount + chineseCount);
        return ratio >= _options.EnglishRatioThreshold &&
               englishCount > chineseCount * _options.EnglishToChineseMultiplier;
    }

    private sealed record MarkdownSection(int StartLine, string Text);
}

public sealed class MarkdownChineseValidatorOptions
{
    public MarkdownChineseValidatorOptions(
        int MinEnglishLetters = 80,
        double EnglishRatioThreshold = 0.65,
        double EnglishToChineseMultiplier = 2.0,
        IReadOnlySet<string>? ExemptRelativePaths = null)
    {
        this.MinEnglishLetters = MinEnglishLetters;
        this.EnglishRatioThreshold = EnglishRatioThreshold;
        this.EnglishToChineseMultiplier = EnglishToChineseMultiplier;
        this.ExemptRelativePaths = ExemptRelativePaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public int MinEnglishLetters { get; }

    public double EnglishRatioThreshold { get; }

    public double EnglishToChineseMultiplier { get; }

    public IReadOnlySet<string> ExemptRelativePaths { get; }
}
