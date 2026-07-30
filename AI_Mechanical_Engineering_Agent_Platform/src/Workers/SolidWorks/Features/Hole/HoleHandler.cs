using System.Globalization;
using DomainSchemas;

namespace SolidWorksWorker.Features.Hole;

public sealed class HoleHandler : FeatureHandlerBase
{
    public override string FeatureType => FeatureTypes.Hole;

    public override string OperationType => "AddHoleWizardHole";

    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema { get; } =
    [
        new("hole_diameter_mm", "positive_number", false, "孔直径，单位毫米。"),
        new("diameter_mm", "positive_number", false, "孔直径兼容别名，单位毫米。"),
        new("end_condition", "text", false, "终止条件；尚无已授权真实映射。")
    ];

    public override FeatureApiEvidence ApiEvidence { get; } = new(
        "No accepted API mapping; Hole Wizard candidate explicitly rejected",
        "Project V1.9 API evidence and SOLIDWORKS API Help",
        ["diameter", "end condition", "placement references"],
        "No return contract is authorized for V2.0-B.",
        [
            "A supported simple-hole strategy is selected.",
            "Placement, direction and termination references are resolved.",
            "A dedicated diagnostic passes for the exact adapter."
        ],
        FeatureApiEvidenceStatuses.Unverified,
        [
            "The current AddHoleWizardHole operation name is not API proof.",
            "V1.9 rejected blind use of Hole Wizard.",
            "Placement references and standard/type identifiers are unresolved."
        ],
        ["No handler-specific real diagnostic exists."]);

    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature)
    {
        if (!CanHandle(feature))
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.UnsupportedFeatureType,
                $"{PartFamilyFailureStages.UnsupportedFeatureType}: {feature.FeatureType} cannot be handled by {nameof(HoleHandler)}.");
        }

        var diameterText =
            feature.Parameters.GetValueOrDefault("hole_diameter_mm") ??
            feature.Parameters.GetValueOrDefault("diameter_mm");
        if (!double.TryParse(
                diameterText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var diameter) ||
            !double.IsFinite(diameter) ||
            diameter <= 0)
        {
            return FeatureHandlerValidationResult.Failed(
                PartFamilyFailureStages.InvalidFeatureParameter,
                $"{PartFamilyFailureStages.InvalidFeatureParameter}: {feature.FeatureId} requires a finite positive hole diameter.");
        }

        return FeatureHandlerValidationResult.Passed();
    }

    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(
        FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EvidenceBlocked(context.Feature));
    }
}
