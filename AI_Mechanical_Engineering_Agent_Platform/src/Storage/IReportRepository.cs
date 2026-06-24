using DomainSchemas;

namespace Storage;

public interface IReportRepository
{
    Task SaveFinalReportAsync(FinalReport report, CancellationToken cancellationToken = default);

    Task<FinalReport?> GetFinalReportAsync(string taskId, CancellationToken cancellationToken = default);
}
