using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace CadKernel
{
    /// <summary>当次运行的调用与恢复证据。只追加 sidecar，不参与或改变回归基准。</summary>
    public static class RuntimeEvents
    {
        static readonly object Sync = new object();
        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static void Record(string api, string status, string document = null, Exception error = null,
            int? attempt = null, string recovery = null, bool forcedCleanup = false, string message = null,
            int? pid = null, long? startUtcTicks = null)
        {
            string dir = Run.Dir;
            if (dir == null || !Directory.Exists(dir)) return;
            var row = new RuntimeEvent
            {
                AtUtc = DateTime.UtcNow.ToString("o"),
                Stage = Run.Manifest.Steps.LastOrDefault(s => s.Status == "running")?.Name ?? "session",
                Api = api, Status = status, Document = document,
                Pid = pid ?? Sw.ProcessId, StartUtcTicks = startUtcTicks ?? Sw.ProcessStartUtcTicks,
                Hresult = error == null ? null : "0x" + error.HResult.ToString("X8"),
                Attempt = attempt, Recovery = recovery, ForcedCleanup = forcedCleanup,
                Message = message ?? error?.Message,
            };
            lock (Sync) File.AppendAllText(Path.Combine(dir, "runtime_events.jsonl"), JsonSerializer.Serialize(row, Json) + Environment.NewLine, new UTF8Encoding(false));
        }

        sealed class RuntimeEvent
        {
            public string AtUtc { get; set; }
            public string Stage { get; set; }
            public string Api { get; set; }
            public string Status { get; set; }
            public string Document { get; set; }
            public int? Pid { get; set; }
            public long? StartUtcTicks { get; set; }
            public string Hresult { get; set; }
            public int? Attempt { get; set; }
            public string Recovery { get; set; }
            public bool ForcedCleanup { get; set; }
            public string Message { get; set; }
        }
    }
}
