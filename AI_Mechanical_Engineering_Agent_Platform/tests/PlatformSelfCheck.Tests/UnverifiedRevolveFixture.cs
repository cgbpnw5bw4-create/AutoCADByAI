using DomainSchemas;
using SolidWorksWorker.Features;
using SolidWorksWorker.Features.Revolve;

namespace PlatformSelfCheck.Tests;

// 显式缺证据场景。生产能力补齐后，仍保留原先全部拒绝断言。
internal sealed class UnverifiedRevolveFixture : FeatureHandlerBase
{
    private readonly RevolveBossHandler _inner = new();
    public static FeatureHandlerRegistry CreateRegistry() => new(FeatureHandlerRegistry.CreateDefault().GetAll()
        .Select(handler => handler.FeatureType == FeatureTypes.RevolveBoss ? new UnverifiedRevolveFixture() : handler));
    public override string FeatureType => FeatureTypes.RevolveBoss;
    public override string OperationType => "RevolveBoss";
    public override IReadOnlyList<FeatureHandlerParameterDefinition> ParameterSchema => _inner.ParameterSchema;
    public override FeatureApiEvidence ApiEvidence => _inner.ApiEvidence with
    {
        Status = FeatureApiEvidenceStatuses.Unverified,
        EvidenceId = null, DiagnosticRunPath = null, SourceRevision = null
    };
    public override FeatureHandlerValidationResult Validate(FeatureDefinition feature) => _inner.Validate(feature);
    public override Task<FeatureHandlerExecutionResult> ExecuteAsync(FeatureHandlerExecutionContext context,
        CancellationToken cancellationToken = default) => Task.FromResult(EvidenceBlocked(context.Feature));
}
