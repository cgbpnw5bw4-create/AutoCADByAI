using System.Runtime.InteropServices;

namespace SolidWorksWorker;

public interface ISolidWorksExecutionEnvironmentProbe
{
    SolidWorksExecutionEnvironmentProbeResult Probe();
}

public sealed record SolidWorksExecutionEnvironmentProbeResult(
    bool CanAttemptRealExecution,
    IReadOnlyList<string> Issues);

public sealed class SolidWorksExecutionEnvironmentProbe : ISolidWorksExecutionEnvironmentProbe
{
    public const string FailureStage = "real_execution_environment_unavailable";

    public SolidWorksExecutionEnvironmentProbeResult Probe()
    {
        var issues = new List<string>();

        if (!OperatingSystem.IsWindows())
        {
            issues.Add("real_execution_environment_unsupported_os: SolidWorks COM automation requires Windows.");
        }

        if (!Environment.UserInteractive)
        {
            issues.Add("real_execution_environment_headless: an interactive Windows desktop session is required.");
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Type.GetTypeFromProgID("SldWorks.Application", throwOnError: false) is null)
                {
                    issues.Add("solidworks_com_registration_missing: SldWorks.Application is not registered.");
                }
            }
            catch (Exception ex) when (
                ex is COMException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                issues.Add($"solidworks_com_registration_probe_failed: {ex.Message}");
            }
        }

        return new SolidWorksExecutionEnvironmentProbeResult(
            issues.Count == 0,
            issues);
    }
}
