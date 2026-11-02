using System;
using System.IO;
using Xunit;

namespace CadTests
{
    /// <summary>只在临时输入目录注入文件系统故障, 不连接 SolidWorks。</summary>
    public class ImportCommitTests
    {
        [Fact]
        public void SuccessfulCommitReplacesGeometryAndJsonTogether()
        {
            using var f = new InputFixture();
            CadCli.Program.CommitImport(f.Task, f.Stage, "new import");
            Assert.Equal("new geometry", File.ReadAllText(Path.Combine(f.Geometry, "new.x_t")));
            Assert.False(File.Exists(Path.Combine(f.Geometry, "old.x_t")));
            Assert.Equal("new import", File.ReadAllText(f.Import));
            Assert.False(Directory.Exists(f.Stage));
            Assert.Empty(Directory.GetDirectories(f.Task, ".geometry_backup_*"));
            Assert.Empty(Directory.GetFiles(f.Task, ".import_*"));
            f.AssertProtected();
        }

        [Fact]
        public void NewTaskCommitDoesNotRequireOldGeometryOrJson()
        {
            using var f = new InputFixture(oldInput: false);
            CadCli.Program.CommitImport(f.Task, f.Stage, "new import");
            Assert.Equal("new geometry", File.ReadAllText(Path.Combine(f.Geometry, "new.x_t")));
            Assert.Equal("new import", File.ReadAllText(f.Import));
            f.AssertProtected();
        }

        [Fact]
        public void FailedOldDirectoryRenameDoesNotFallBackToDeletingOldFiles()
        {
            using var f = new InputFixture();
            Assert.Throws<IOException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                moveDirectory: (a, b) =>
                {
                    if (a == f.Geometry) throw new IOException("old directory is locked");
                    Directory.Move(a, b);
                }));
            f.AssertOldInput();
            f.AssertStagedGeometry();
            Assert.Empty(Directory.GetDirectories(f.Task, ".geometry_backup_*"));
        }

        [Fact]
        public void FailedGeometryMoveRestoresOldDirectoryAndJson()
        {
            using var f = new InputFixture();
            Assert.Throws<IOException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                moveDirectory: (a, b) =>
                {
                    if (a == f.Stage) throw new IOException("new geometry cannot be moved");
                    Directory.Move(a, b);
                }));
            f.AssertOldInput();
            f.AssertStagedGeometry();
        }

        [Fact]
        public void FailedJsonWriteLeavesBothOldInputsUntouched()
        {
            using var f = new InputFixture();
            Assert.Throws<IOException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                writeText: (p, text) =>
                {
                    File.WriteAllText(p, "partial new import");
                    throw new IOException("json write failed");
                }));
            f.AssertOldInput();
            f.AssertStagedGeometry();
        }

        [Fact]
        public void FailedJsonReplacementRestoresBothOldInputs()
        {
            using var f = new InputFixture();
            Assert.Throws<IOException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                replaceFile: (a, b, old) => throw new IOException("json replacement failed")));
            f.AssertOldInput();
            f.AssertStagedGeometry();
        }

        [Fact]
        public void FailureReportedAfterJsonReplacementStillRestoresBothOldInputs()
        {
            using var f = new InputFixture();
            Assert.Throws<IOException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                replaceFile: (a, b, old) =>
                {
                    File.Replace(a, b, old);
                    throw new IOException("failure reported after replacement");
                }));
            f.AssertOldInput();
            f.AssertStagedGeometry();
        }

        [Fact]
        public void FailedRollbackRetainsRecoverableOldGeometryAndReportsItsPath()
        {
            using var f = new InputFixture();
            var error = Assert.Throws<AggregateException>(() => CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                moveDirectory: (a, b) =>
                {
                    if (a == f.Stage || Path.GetFileName(a).StartsWith(".geometry_backup_", StringComparison.Ordinal))
                        throw new IOException("move failed");
                    Directory.Move(a, b);
                }));
            string backup = Assert.Single(Directory.GetDirectories(f.Task, ".geometry_backup_*"));
            Assert.Contains(backup, error.Message);
            f.AssertOldGeometry(backup);
            Assert.Equal("old import", File.ReadAllText(f.Import));
            f.AssertStagedGeometry();
            f.AssertProtected();
        }

        [Fact]
        public void FailedBackupCleanupDoesNotRollBackSuccessfulCommit()
        {
            using var f = new InputFixture();
            CadCli.Program.CommitImport(f.Task, f.Stage, "new import",
                deleteDirectory: p => throw new IOException("backup cleanup failed"));
            string backup = Assert.Single(Directory.GetDirectories(f.Task, ".geometry_backup_*"));
            f.AssertOldGeometry(backup);
            Assert.Equal("new geometry", File.ReadAllText(Path.Combine(f.Geometry, "new.x_t")));
            Assert.Equal("new import", File.ReadAllText(f.Import));
            f.AssertProtected();
        }

        [Fact]
        public void StageOutsideNamedTaskIsRejectedBeforeAnyInputChange()
        {
            using var f = new InputFixture();
            Assert.Throws<ArgumentException>(() => CadCli.Program.CommitImport(f.Task,
                Path.Combine(f.Root, ".geometry_importing"), "new import"));
            f.AssertOldInput();
            f.AssertStagedGeometry();
        }

        sealed class InputFixture : IDisposable
        {
            public readonly string Root, Task, Stage, Geometry, Import;

            public InputFixture(bool oldInput = true)
            {
                Root = Path.Combine(Path.GetTempPath(), "cad_import_commit_tests_" + Guid.NewGuid().ToString("N"));
                Task = Path.Combine(Root, "input", "task");
                Stage = Path.Combine(Task, ".geometry_importing");
                Geometry = Path.Combine(Task, "geometry");
                Import = Path.Combine(Task, "import.json");
                Directory.CreateDirectory(Stage);
                File.WriteAllText(Path.Combine(Stage, "new.x_t"), "new geometry");
                File.WriteAllText(Path.Combine(Task, "design.json"), "protected design");
                File.WriteAllText(Path.Combine(Task, "expected.json"), "protected baseline");
                if (oldInput)
                {
                    Directory.CreateDirectory(Path.Combine(Geometry, "nested"));
                    File.WriteAllText(Path.Combine(Geometry, "old.x_t"), "old geometry");
                    File.WriteAllText(Path.Combine(Geometry, "nested", "other.x_t"), "other old geometry");
                    File.WriteAllText(Import, "old import");
                }
            }

            public void AssertOldGeometry(string dir)
            {
                Assert.Equal("old geometry", File.ReadAllText(Path.Combine(dir, "old.x_t")));
                Assert.Equal("other old geometry", File.ReadAllText(Path.Combine(dir, "nested", "other.x_t")));
                Assert.Equal(2, Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length);
            }

            public void AssertOldInput()
            {
                AssertOldGeometry(Geometry);
                Assert.Equal("old import", File.ReadAllText(Import));
                AssertProtected();
            }

            public void AssertStagedGeometry()
            {
                Assert.Equal("new geometry", File.ReadAllText(Path.Combine(Stage, "new.x_t")));
            }

            public void AssertProtected()
            {
                Assert.Equal("protected design", File.ReadAllText(Path.Combine(Task, "design.json")));
                Assert.Equal("protected baseline", File.ReadAllText(Path.Combine(Task, "expected.json")));
            }

            public void Dispose()
            {
                string resolved = Path.GetFullPath(Root);
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(Path.GetDirectoryName(resolved), temp, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(resolved).StartsWith("cad_import_commit_tests_", StringComparison.Ordinal))
                    throw new IOException("拒绝清理测试临时目录以外的路径: " + resolved);
                if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
            }
        }
    }
}
