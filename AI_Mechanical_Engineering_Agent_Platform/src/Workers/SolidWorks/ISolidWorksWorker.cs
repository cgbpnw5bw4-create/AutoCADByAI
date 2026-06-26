using DomainSchemas;
using WorkerContracts;

namespace SolidWorksWorker;

public interface ISolidWorksWorker : IWorker
{
    Task<SolidWorksWorkerResult> ExecuteAsync(
        SolidWorksWorkerRequest request,
        CancellationToken cancellationToken = default);
}
