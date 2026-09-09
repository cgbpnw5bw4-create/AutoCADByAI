namespace PlatformCore;

public static class CadDryRunCliContract
{
    public static bool IsInvocation(IReadOnlyList<string> args) => args.Count == 3 &&
        args[0].Equals("dry-run-cad", StringComparison.OrdinalIgnoreCase) && args[1] == "--input";
    public static IReadOnlyDictionary<string, string> ForceDryRun(IReadOnlyDictionary<string, string> context)
    {
        var forced = new Dictionary<string, string>(context, StringComparer.OrdinalIgnoreCase)
        {
            ["dry_run"] = "true", ["operation"] = "build_plate", ["solidworks_main_workflow"] = "true",
            ["generate_drawing"] = "false", ["generate_dimensions"] = "false",
            ["generate_title_block"] = "false", ["generate_release_package"] = "false"
        };
        return forced;
    }
}
