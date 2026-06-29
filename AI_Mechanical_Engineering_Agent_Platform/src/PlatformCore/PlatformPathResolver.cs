namespace PlatformCore;

public static class PlatformPathResolver
{
    public static string FindProjectRoot(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")) ||
                File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var start = startDirectory ?? AppContext.BaseDirectory;
        throw new DirectoryNotFoundException(
            $"Could not locate project root from '{start}'. Ensure the working directory is within the AI_Mechanical_Engineering_Agent_Platform tree.");
    }
}
