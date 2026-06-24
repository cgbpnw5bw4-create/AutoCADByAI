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

        return Directory.GetCurrentDirectory();
    }
}
