namespace PlatformCore;

/// <summary>
/// 受控 V1.7 CAD 端到端命令的共享语法。CLI 与 self-check 共用该合约，避免用源码文本证明入口存在。
/// </summary>
public static class SolidWorksE2eCliContract
{
    public const string CommandName = "run-cad-workflow";
    public const string InputOption = "--input";

    public static bool IsInvocation(IReadOnlyList<string> arguments) =>
        arguments.Count == 3 &&
        string.Equals(arguments[0], CommandName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(arguments[1], InputOption, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(arguments[2]);
}
