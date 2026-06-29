using DomainSchemas;
using QualityGate;

namespace PlatformCore.Modules.CADModeling.Validators;

public sealed class SolidWorksEnvironmentValidator : IValidator
{
    public string Name => "solidworks-environment-validator";

    public ReviewReport Validate(object payload)
    {
        if (payload is not SolidWorksWorkerRequest request)
        {
            return new ReviewReport(
                $"solidworks-environment-validation-{Guid.NewGuid():N}",
                Name,
                false,
                0,
                new[] { "payload must be SolidWorksWorkerRequest." },
                RequiresHumanApproval: false,
                HasFatalError: true);
        }

        var report = ValidateEnvironment(request);

        return new ReviewReport(
            report.ReportId,
            Name,
            report.FinalStatus is "Passed" or "Warning" or "Skipped",
            report.FinalStatus == "Passed" ? 0.95 : 0.5,
            report.Issues,
            RequiresHumanApproval: false,
            HasFatalError: report.FinalStatus == "Failed");
    }

    public SolidWorksPreflightReport ValidateEnvironment(
        SolidWorksWorkerRequest request,
        SolidWorksRuntimeOptions? options = null,
        bool allowComProbe = false)
    {
        options ??= SolidWorksRuntimeOptions.FromEnvironment();

        return SolidWorksPreflightEvaluator.Evaluate(
            request,
            options,
            allowComProbe,
            SolidWorksComAvailable);
    }

    private static bool SolidWorksComAvailable()
    {
#pragma warning disable CA1416
        return Type.GetTypeFromProgID("SldWorks.Application") is not null;
#pragma warning restore CA1416
    }
}
