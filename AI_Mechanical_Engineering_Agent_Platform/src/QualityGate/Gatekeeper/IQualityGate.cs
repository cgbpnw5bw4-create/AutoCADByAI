using DomainSchemas;

namespace QualityGate;

// 领域标识只声明扩展位置，不表示对应的工程能力已经实现或验收。
public enum QualityGateDomain
{
    General,
    Geometry,
    APIEvidence,
    Assembly,
    Drawing,
    Manufacturability
}

// 门禁裁决消费已有校验或复审报告，不调用模型、Worker 或外部 CAD。
public interface IQualityGate
{
    string Name { get; }

    QualityGateDomain Domain { get; }

    GateDecision Evaluate(ReviewReport report);
}

public sealed record QualityGateCheckResult(
    string Name,
    QualityGateDomain Domain,
    GateDecision Decision);
