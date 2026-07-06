namespace SolidWorksDrawingTitleBlockSmokeRunner;

public sealed record DrawingTitleBlockRunnerOptions(
    string OutputRoot,
    string? SourceDrawingPath,
    bool Visible)
{
    public static DrawingTitleBlockRunnerOptions Parse(string[] args)
    {
        var output = Path.Combine("output", "solidworks", "real", "plate_basic_4holes_title_block");
        string? sourceDrawingPath = null;
        var visible = true;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--source-drawing" when index + 1 < args.Length:
                case "--source-dimensioned-drawing" when index + 1 < args.Length:
                    sourceDrawingPath = args[++index];
                    break;
                case "--hidden":
                    visible = false;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run --project tools/SolidWorksDrawingTitleBlockSmokeRunner -- --source-drawing <plate_basic_4holes_dimensioned.SLDDRW> --output output/solidworks/real/plate_basic_4holes_title_block [--hidden]");
                    Environment.Exit(0);
                    break;
            }
        }

        return new DrawingTitleBlockRunnerOptions(output, sourceDrawingPath, visible);
    }
}
