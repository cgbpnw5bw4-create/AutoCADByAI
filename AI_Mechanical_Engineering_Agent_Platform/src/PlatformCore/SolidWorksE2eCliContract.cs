namespace PlatformCore;

/// <summary>
/// 受控 V1.7 CAD 端到端命令的共享语法。CLI 与 self-check 共用该合约，避免用源码文本证明入口存在。
/// </summary>
public static class SolidWorksE2eCliContract
{
    public const string CommandName = "run-cad-workflow";
    public const string InputOption = "--input";
    public const string CompleteDrawingPackageOperation = "build_complete_drawing_package";
    public const string PartFamilyReleasePackageOperation = "build_part_family_release_package";
    public const string ModelUpdateReleasePackageOperation = "rebuild_parameter_update";

    public static bool IsInvocation(IReadOnlyList<string> arguments) =>
        arguments.Count == 3 &&
        string.Equals(arguments[0], CommandName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(arguments[1], InputOption, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(arguments[2]);

    public static bool IsSupportedOperation(string? operation) =>
        string.Equals(operation, CompleteDrawingPackageOperation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(operation, PartFamilyReleasePackageOperation, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(operation, ModelUpdateReleasePackageOperation, StringComparison.OrdinalIgnoreCase);

    public static bool IsPartFamilyReleasePackage(string? operation) =>
        string.Equals(operation, PartFamilyReleasePackageOperation, StringComparison.OrdinalIgnoreCase);

    public static bool IsModelUpdateReleasePackage(string? operation) =>
        string.Equals(operation, ModelUpdateReleasePackageOperation, StringComparison.OrdinalIgnoreCase);
}
