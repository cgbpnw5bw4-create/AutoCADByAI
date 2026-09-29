using System.Text.Json;
using DomainSchemas;
using SolidWorksWorker.Features;

var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true, WriteIndented = true };
if (args[0] == "revision")
{
    Console.WriteLine(JsonSerializer.Serialize(new { feature = FeatureExecutionEvidencePolicy.ComputeCurrentSourceRevision(), v20d = V20DThreeCircleCutEvidencePolicy.ComputeCurrentSourceRevision() }, options));
    return 0;
}
if (args[0] == "family")
{
    var results = new[] { "shaft_basic", "jacket_basic" }.ToDictionary(type => type, type => PartFamilyProductionEvidencePolicy.IsActive(type));
    Console.WriteLine(JsonSerializer.Serialize(results, options));
    return results.Values.All(value => value) ? 0 : 2;
}
using var report = JsonDocument.Parse(File.ReadAllText(args[1]));
var plan = report.RootElement.GetProperty("build_plan").Deserialize<SolidWorksBuildPlan>(options)!;
var definition = PartTypeRegistry.CreateDefault().GetDefinition(plan.PartType)!;
if (args[0] == "preflight")
{
    var result = PartFamilyProductionEvidencePolicy.ValidateForRealExecution(plan);
    Console.WriteLine(JsonSerializer.Serialize(result, options));
    return result.IsValid ? 0 : 2;
}
var expected = definition.DescribeExpectedGeometry(plan)!;
var resultsList = args.Skip(2).Select(path =>
{
    using var probe = JsonDocument.Parse(File.ReadAllText(path));
    var measured = probe.RootElement.GetProperty("geometry").Deserialize<MeasuredGeometry>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    return new { probe_path = Path.GetFullPath(path), measured, validation = PartGeometryValidator.Validate(expected, measured) };
}).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new { plan_sha256 = PartFamilyProductionEvidencePolicy.ComputePlanFingerprint(plan), expected, results = resultsList }, options));
return resultsList.Length == 2 && resultsList.All(item => item.validation.IsValid) ? 0 : 2;
