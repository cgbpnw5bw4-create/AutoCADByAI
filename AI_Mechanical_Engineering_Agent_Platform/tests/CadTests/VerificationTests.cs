using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using CadKernel;
using Xunit;

namespace CadTests
{
    /// <summary>仅解析/文件复制与拒绝路径; 不对有效清单执行 Execute, 不启动 SolidWorks。</summary>
    [Collection("Run")]
    public class VerificationTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cadverification_" + Guid.NewGuid().ToString("N"));

        public VerificationTests() { Directory.CreateDirectory(root); }

        VerifyExpectations Valid()
        {
            var e = new VerifyExpectations();
            e.Parts.Add(new VerifyPart
            {
                File = "models/block.SLDPRT",
                Configurations =
                {
                    new VerifyConfiguration
                    {
                        Name = "Base", BodyCount = 1, VolumeM3 = .00008, VolumeToleranceM3 = 1e-12,
                        Oracle = "0.08 * 0.05 * 0.02 m³",
                        Dimensions = { new VerifyDimension { FullName = "Width@Profile", ValueM = .08, ToleranceM = 1e-9 } }
                    }
                }
            });
            e.Steps.Add(new VerifyStep { File = "exports/block.step", BodyCount = 1, VolumeM3 = .00008, VolumeToleranceM3 = 1e-12, Oracle = "0.08 * 0.05 * 0.02 m³" });
            return e;
        }

        [Fact]
        public void ExpectedDefinitionRoundTripsAndCanBeReusedWithoutRewriting()
        {
            var e = Valid();
            Verification.SaveExpectations(root, e);
            string file = Path.Combine(root, Verification.ExpectationsFileName);
            string before = File.ReadAllText(file);
            var loaded = Verification.Parse(before);
            Assert.Empty(Verification.Validate(loaded, root));
            Assert.Equal("Base", Assert.Single(Assert.Single(loaded.Parts).Configurations).Name);
            Assert.Equal(.00008, loaded.Steps[0].VolumeM3);
            var timestamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, timestamp);
            Verification.SaveExpectations(root, e);
            Assert.Equal(before, File.ReadAllText(file));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(file));
        }

        [Fact]
        public void DifferentExpectationsCannotOverwriteExistingDefinition()
        {
            var e = Valid();
            Verification.SaveExpectations(root, e);
            string file = Path.Combine(root, Verification.ExpectationsFileName);
            string before = File.ReadAllText(file);
            e.Parts[0].Configurations[0].VolumeM3 = .0001;
            Assert.Throws<IOException>(() => Verification.SaveExpectations(root, e));
            Assert.Equal(before, File.ReadAllText(file));
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("null")]
        [InlineData("{\"schema_version\":1,\"schema_version\":2}")]
        [InlineData("{\"schema_version\":1,\"passed\":true}")]
        [InlineData("{\"schema_version\":1,\"parts\":[{\"file\":\"a.SLDPRT\",\"file\":\"b.SLDPRT\"}]}")]
        public void ParseRejectsMissingVersionDuplicateAndUnknownFields(string json)
        {
            Assert.Throws<JsonException>(() => Verification.Parse(json));
        }

        [Fact]
        public void ManifestRequiresNativeStepConfigurationGeometryAndAnalyticBasis()
        {
            Assert.NotEmpty(Verification.Validate(new VerifyExpectations(), root));
            var e = Valid();
            e.SchemaVersion = 2;
            e.Parts[0].Configurations[0].Name = null;
            e.Parts[0].Configurations[0].BodyCount = null;
            e.Parts[0].Configurations[0].Oracle = null;
            e.Steps.Clear();
            var errors = Verification.Validate(e, root);
            Assert.Contains(errors, x => x.Contains("schema_version"));
            Assert.Contains(errors, x => x.Contains("STEP"));
            Assert.Contains(errors, x => x.Contains("配置名"));
            Assert.Contains(errors, x => x.Contains("实体数"));
            Assert.Contains(errors, x => x.Contains("oracle"));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(.001)]
        public void InvalidOrUnboundedVolumeToleranceIsRejected(double tolerance)
        {
            var e = Valid();
            e.Parts[0].Configurations[0].VolumeToleranceM3 = tolerance;
            Assert.Contains(Verification.Validate(e, root), x => x.Contains("体积及绝对允差"));
        }

        [Fact]
        public void DuplicateArtifactsConfigurationsAndDimensionsAreRejected()
        {
            var e = Valid();
            e.Parts.Add(e.Parts[0]);
            e.Parts[0].Configurations.Add(e.Parts[0].Configurations[0]);
            e.Parts[0].Configurations[0].Dimensions.Add(new VerifyDimension { FullName = "Width@Profile", ValueM = .08, ToleranceM = 1e-9 });
            var errors = Verification.Validate(e, root);
            Assert.Contains(errors, x => x.Contains("文件重复"));
            Assert.Contains(errors, x => x.Contains("配置名"));
            Assert.Contains(errors, x => x.Contains("尺寸参数名"));
        }

        [Theory]
        [InlineData("../outside.SLDPRT")]
        [InlineData("models/../../outside.SLDPRT")]
        [InlineData("models//block.SLDPRT")]
        [InlineData("./models/block.SLDPRT")]
        [InlineData("C:outside.SLDPRT")]
        [InlineData("models/block.SLDPRT:stream")]
        [InlineData("\\\\server\\share\\block.SLDPRT")]
        public void ArtifactPathsCannotEscapeRunOrUseAlternateStreams(string path)
        {
            Assert.Throws<ArgumentException>(() => Verification.ResolvePath(root, path));
        }

        [Fact]
        public void ExistingArtifactCheckDoesNotAssumeFilesWereGenerated()
        {
            Assert.Empty(Verification.Validate(Valid(), root));
            Assert.Equal(2, Verification.Validate(Valid(), root, requireFiles: true).Count(x => x.Contains("文件不存在")));
        }

        [Fact]
        public void CopyHashChecksCoverBothSourceAndIndependentCopy()
        {
            string source = Path.Combine(root, "source"), copies = Path.Combine(root, "copies");
            Directory.CreateDirectory(Path.Combine(source, "models"));
            File.WriteAllText(Path.Combine(source, "models", "block.SLDPRT"), "saved bytes");
            var result = Verification.CopyForReadback(source, "models/block.SLDPRT", copies, "native_part");
            Assert.Equal(result.SourceSha256Before, result.CopySha256Before);
            Verification.CheckUnchanged(result);
            Assert.Empty(result.Errors);
            Assert.Equal(result.SourceSha256Before, result.SourceSha256After);
            Assert.Equal(result.CopySha256Before, result.CopySha256After);
            File.WriteAllText(result.Copy, "modified copy");
            Verification.CheckUnchanged(result);
            Assert.Contains("复验副本 SHA-256 改变", result.Errors);
            File.WriteAllText(result.Source, "modified source");
            Verification.CheckUnchanged(result);
            Assert.Contains("源文件 SHA-256 改变", result.Errors);
        }

        [Fact]
        public void AlreadyActiveSavedConfigurationSkipsShowConfiguration()
        {
            Verification.SelectSavedConfiguration(new[] { "Base", "Wide" }, "Base", () => "Base",
                _ => throw new InvalidOperationException("不应切换已激活配置"));
        }

        [Fact]
        public void MissingSavedConfigurationIsRejectedEvenWhenCurrentNameMatches()
        {
            Assert.Throws<IOException>(() => Verification.SelectSavedConfiguration(new[] { "Wide" }, "Base", () => "Base",
                _ => throw new InvalidOperationException("不应调用切换")));
            Assert.Throws<IOException>(() => Verification.SelectSavedConfiguration(new[] { "base" }, "Base", () => "Base",
                _ => throw new InvalidOperationException("配置列表须精确匹配")));
        }

        [Fact]
        public void FalseConfigurationSwitchReturnIsRejected()
        {
            int calls = 0;
            Assert.Throws<IOException>(() => Verification.SelectSavedConfiguration(new[] { "Base", "Wide" }, "Wide", () => "Base",
                _ => { calls++; return false; }));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void TrueConfigurationSwitchWithWrongActiveNameIsRejected()
        {
            int calls = 0;
            Assert.Throws<IOException>(() => Verification.SelectSavedConfiguration(new[] { "Base", "Wide" }, "Wide", () => "Base",
                _ => { calls++; return true; }));
            Assert.Equal(1, calls);
        }

        [Fact]
        public void SuccessfulConfigurationSwitchAlsoRequiresMatchingActiveName()
        {
            string active = "Base";
            int calls = 0;
            Verification.SelectSavedConfiguration(new[] { "Base", "Wide" }, "Wide", () => active,
                name => { calls++; active = name; return true; });
            Assert.Equal("Wide", active);
            Assert.Equal(1, calls);
        }

        [Fact]
        public void ComparisonRejectsNanInfinityAndMeasurementsOutsideTolerance()
        {
            Assert.True(Verification.WithinTolerance(1.25, 1, .25));
            Assert.False(Verification.WithinTolerance(1.251, 1, .25));
            Assert.False(Verification.WithinTolerance(double.NaN, 1, .25));
            Assert.False(Verification.WithinTolerance(1, double.PositiveInfinity, .25));
            Assert.False(Verification.WithinTolerance(1, 1, double.PositiveInfinity));
            Assert.False(Verification.WithinTolerance(1, 1, 0));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MissingOrInvalidExpectationsCreateIncompleteQaWithoutCadOrSourceWrites(bool invalid)
        {
            string source = Path.Combine(root, "output", "legacy");
            Directory.CreateDirectory(source);
            string manifest = Path.Combine(source, "run.json");
            File.WriteAllText(manifest, "{\"task\":\"legacy\",\"project\":\"legacy\",\"status\":\"passed\"}");
            string before = Verification.Hash(manifest);
            if (invalid) File.WriteAllText(Path.Combine(source, Verification.ExpectationsFileName), "{\"schema_version\":1,\"parts\":[],\"steps\":[]}");
            Run.Init(root);
            Assert.Equal(2, Verification.Execute(source));
            Assert.Equal(before, Verification.Hash(manifest));
            Assert.False(Directory.Exists(Path.Combine(root, ".cad")));
            Assert.NotEqual(source, Run.Dir);
            using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Run.Checks, "verification.json")));
            Assert.Equal("INCOMPLETE", report.RootElement.GetProperty("status").GetString());
            Assert.False(report.RootElement.GetProperty("source_run_status_used").GetBoolean());
            Assert.False(report.RootElement.GetProperty("session_exited_normally").GetBoolean());
            Assert.Empty(report.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("failed", Run.Manifest.Status);
        }

        public void Dispose()
        {
            Run.CloseLog();
            string full = Path.GetFullPath(root);
            string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.GetDirectoryName(full) != temp || !Path.GetFileName(full).StartsWith("cadverification_", StringComparison.Ordinal))
                throw new IOException("拒绝清理测试目录以外的路径");
            Directory.Delete(full, true);
        }
    }
}
