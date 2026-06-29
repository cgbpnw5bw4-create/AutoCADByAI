namespace WorkerContracts;

public interface IWorker
{
    string Name { get; }

    string TargetSystem { get; }

    Task<WorkerOutput> ExecuteAsync(WorkerInput input);

    Task<WorkerOutput> ExecuteAsync(WorkerInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteAsync(input);
    }
}
