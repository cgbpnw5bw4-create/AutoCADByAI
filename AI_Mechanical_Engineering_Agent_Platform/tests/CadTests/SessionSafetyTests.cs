using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using CadKernel;
using Xunit;

namespace CadTests
{
    [Collection("Run")]
    public class SessionSafetyTests : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "cad_session_" + Guid.NewGuid().ToString("N"));
        public SessionSafetyTests() { Directory.CreateDirectory(root); }
        public void Dispose() { Run.CloseLog(); Directory.Delete(root, true); }

        [Fact]
        public void SameTitleDoesNotMaskWrongSavedDocument()
        {
            Assert.False(Sw.SameDocument(Path.Combine(root, "a", "part.sldprt"), "part", Path.Combine(root, "b", "part.sldprt"), "part"));
            Assert.False(Sw.SameDocument("", "part", Path.Combine(root, "part.sldprt"), "part"));
            Assert.True(Sw.SameDocument("", "part", "", "part"));
            Assert.False(Sw.SameDocument("", "part", "", "other"));
        }

        [Fact]
        public void RestoreReadbackFailureRetainsRecoveryRecordAndRetryRestoresAll()
        {
            string file = Path.Combine(root, "prefs.txt");
            File.WriteAllText(file, "True,False");
            var state = new Dictionary<int, bool> { { 10, false }, { 20, true } };
            Assert.Throws<IOException>(() => Sw.RestoreToggles(file, new[] { 10, 20 },
                (k,v) => { if (k == 10) state[k] = v; }, k => state[k]));
            Assert.Equal("True,False", File.ReadAllText(file));
            Sw.RestoreToggles(file, new[] { 10, 20 }, (k,v) => state[k] = v, k => state[k]);
            Assert.True(state[10]); Assert.False(state[20]); Assert.False(File.Exists(file));
        }

        [Theory]
        [InlineData("True")]
        [InlineData("True,bad")]
        public void InvalidPreferenceRecordDoesNotPartiallyApply(string record)
        {
            string file = Path.Combine(root, "prefs.txt"); File.WriteAllText(file, record);
            bool applied = false;
            Assert.Throws<IOException>(() => Sw.RestoreToggles(file, new[] { 10, 20 }, (k,v) => applied = true, _ => false));
            Assert.False(applied); Assert.Equal(record, File.ReadAllText(file));
        }

        [Fact]
        public void SetterFailureKeepsAllOriginalValues()
        {
            string file = Path.Combine(root, "prefs.txt"); File.WriteAllText(file, "True,False");
            Assert.Throws<COMException>(() => Sw.RestoreToggles(file, new[] { 10, 20 },
                (k,v) => throw new COMException("lost", unchecked((int)0x800706BE)), _ => true));
            Assert.Equal("True,False", File.ReadAllText(file));
        }

        [Fact]
        public void RuntimeFailureKeepsHresultStageAndRecoveryWithoutTurningIntoCheckPass()
        {
            Run.Init(root); Run.Begin("trace", "trace", "trace", null);
            var error = new COMException("lost session", unchecked((int)0x800706BE));
            Assert.Throws<COMException>(() => Run.Step("工程图", () =>
            {
                RuntimeEvents.Record("GetPolylines7", "failed", "drawing.slddrw", error, 2, "restart_and_redraw", true);
                throw error;
            }));
            string[] lines = File.ReadAllLines(Path.Combine(Run.Dir, "runtime_events.jsonl"));
            using var row = JsonDocument.Parse(lines[0]);
            Assert.Equal("0x800706BE", row.RootElement.GetProperty("hresult").GetString());
            Assert.Equal("工程图", row.RootElement.GetProperty("stage").GetString());
            Assert.Equal(2, row.RootElement.GetProperty("attempt").GetInt32());
            Assert.True(row.RootElement.GetProperty("forced_cleanup").GetBoolean());
            Assert.Empty(Run.Manifest.Checks);
            Assert.Equal("failed", Run.Manifest.Steps[0].Status);
            using var failure = JsonDocument.Parse(lines[1]);
            Assert.Equal("工程图", failure.RootElement.GetProperty("stage").GetString());
            Assert.Equal("0x800706BE", failure.RootElement.GetProperty("hresult").GetString());
        }
    }
}
