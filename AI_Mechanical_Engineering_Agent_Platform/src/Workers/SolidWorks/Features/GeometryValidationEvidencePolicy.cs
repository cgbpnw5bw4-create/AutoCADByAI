using System.Security.Cryptography;
using System.Text;

namespace SolidWorksWorker.Features;

/// <summary>
/// Independent V2.0-D source binding for geometry-read evidence. It deliberately
/// does not alter the V2.0-C FeatureHandler evidence hash.
/// </summary>
public static class GeometryValidationEvidencePolicy
{
    public const string EvidenceId = "v2.0-d-solidworks-geometry-reader";
    private const string Prefix = "geometry-validation-source-sha256:";

    public static string ComputeSourceRevision()
    {
        try
        {
            var root = FindProjectRoot();
            var paths = new[]
            {
                "src/Workers/SolidWorks/Features/SolidWorksGeometryReader.cs",
                "src/Workers/SolidWorks/Features/FeatureExecutionPipeline.cs",
                "src/Workers/SolidWorks/Features/V20DFeatureGraphPartFamilyBuilder.cs",
                "src/Workers/SolidWorks/Features/V20DThreeCircleCutEvidencePolicy.cs",
                "src/Modules/CADModeling/ModelUpdateService.cs",
                "src/DomainSchemas/GeometryValidation.cs",
                "src/DomainSchemas/HoleFeatureDefinition.cs",
                "src/DomainSchemas/HoleGeometryValidation.cs",
                "src/DomainSchemas/HolePlanValidation.cs"
            };
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var path in paths)
            {
                var fullPath = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
                hash.AppendData(Encoding.UTF8.GetBytes(path));
                hash.AppendData(Encoding.UTF8.GetBytes("\n"));
                hash.AppendData(File.ReadAllBytes(fullPath));
                hash.AppendData(Encoding.UTF8.GetBytes("\n"));
            }

            return Prefix + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return Prefix + "unavailable";
        }
    }

    private static string FindProjectRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AI_Mechanical_Engineering_Agent_Platform.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("AI_Mechanical_Engineering_Agent_Platform.sln could not be located.");
    }
}
