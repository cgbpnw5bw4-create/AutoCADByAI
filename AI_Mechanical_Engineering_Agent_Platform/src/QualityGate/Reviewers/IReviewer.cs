using DomainSchemas;

namespace QualityGate;

public interface IReviewer
{
    string Name { get; }

    ReviewReport Review(object payload);
}
