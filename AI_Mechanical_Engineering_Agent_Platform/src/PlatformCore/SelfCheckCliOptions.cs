namespace PlatformCore;

public static class SelfCheckCliOptions
{
    public static string ResolveOutputRoot(IReadOnlyList<string> args, string projectRoot)
    {
        if (args.Count == 1 && args[0].Equals("self-check", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(projectRoot, "output");
        if (args.Count == 3 && args[0].Equals("self-check", StringComparison.OrdinalIgnoreCase) &&
            args[1].Equals("--output", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(args[2]))
            return Path.GetFullPath(args[2]);
        throw new ArgumentException("用法：self-check [--output <输出根目录>]。相对目录以当前工作目录为准。");
    }
}
