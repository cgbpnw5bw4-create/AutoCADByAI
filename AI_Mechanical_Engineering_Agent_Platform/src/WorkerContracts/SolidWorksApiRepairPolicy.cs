namespace WorkerContracts;

/// <summary>
/// Shared, executable policy for the single bounded SolidWorks API repair attempt.
/// Diagnostic runners and self-check consume the same contract so retry capability
/// is proven by behavior instead of source-text matching.
/// </summary>
public static class SolidWorksApiRepairPolicy
{
    public const int MaxRepairAttempts = 1;

    public static bool CanAttempt(string? failureStage, bool repairAlreadyAttempted) =>
        string.Equals(failureStage, "cut_holes_failed", StringComparison.OrdinalIgnoreCase) &&
        !repairAlreadyAttempted;
}
