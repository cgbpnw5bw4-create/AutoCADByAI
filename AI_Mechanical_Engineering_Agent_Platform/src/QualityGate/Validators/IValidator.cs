using DomainSchemas;

namespace QualityGate;

public interface IValidator
{
    string Name { get; }

    ReviewReport Validate(object payload);
}
