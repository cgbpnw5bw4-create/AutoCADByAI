namespace SolidWorksDrawingDimensionSmokeRunner;

public sealed record DrawingDimensionRunnerOptions(
    string OutputRoot,
    string? SourceDrawingPath,
    bool Visible)
{
    public static DrawingDimensionRunnerOptions Parse(string[] args)
    {
        var output = Path.Combine("output", "solidworks", "real", "plate_basic_4holes_drawing_dimensions");
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
                    sourceDrawingPath = args[++index];
                    break;
                case "--hidden":
                    visible = false;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run --project tools/SolidWorksDrawingDimensionSmokeRunner -- --source-drawing <plate_basic_4holes.SLDDRW> --output output/solidworks/real/plate_basic_4holes_drawing_dimensions [--hidden]");
                    Environment.Exit(0);
                    break;
            }
        }

        return new DrawingDimensionRunnerOptions(output, sourceDrawingPath, visible);
    }
}
