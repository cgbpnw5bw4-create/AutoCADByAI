using DomainSchemas;

namespace QualityGate;

public interface IGatekeeper
{
    GateEvaluationResult Evaluate(ReviewReport reviewReport);
}
