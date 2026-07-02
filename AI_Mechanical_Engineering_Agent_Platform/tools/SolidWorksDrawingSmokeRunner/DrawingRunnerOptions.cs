namespace SolidWorksDrawingSmokeRunner;

public sealed record DrawingRunnerOptions(
    string OutputRoot,
    string? SourcePartPath,
    string? DrawingTemplatePath,
    bool Visible)
{
    public static DrawingRunnerOptions Parse(string[] args)
    {
        var output = Path.Combine("output", "solidworks", "real", "plate_basic_4holes_drawing");
        string? sourcePartPath = null;
        string? drawingTemplatePath = Environment.GetEnvironmentVariable("SW_TEMPLATE_DRAWING_PATH");
        var visible = true;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    output = args[++index];
                    break;
                case "--source-part" when index + 1 < args.Length:
                    sourcePartPath = args[++index];
                    break;
                case "--template" when index + 1 < args.Length:
                    drawingTemplatePath = args[++index];
                    break;
                case "--hidden":
                    visible = false;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: dotnet run --project tools/SolidWorksDrawingSmokeRunner -- --source-part <plate_basic_4holes.SLDPRT> --output output/solidworks/real/plate_basic_4holes_drawing [--template C:\\Path\\Drawing.drwdot] [--hidden]");
                    Environment.Exit(0);
                    break;
            }
        }

        return new DrawingRunnerOptions(output, sourcePartPath, drawingTemplatePath, visible);
    }
}
