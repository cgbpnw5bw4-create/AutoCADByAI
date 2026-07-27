using DomainSchemas;

namespace SolidWorksWorker;

public static class RealSolidWorksExecutionCoordinator
{
    private static readonly SemaphoreSlim ExecutionGate = new(1, 1);

    public static async Task<IDisposable?> TryAcquireAsync(
        int timeoutSeconds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(
            timeoutSeconds,
            SolidWorksRuntimeOptions.MinimumExecutionTimeoutSeconds,
            SolidWorksRuntimeOptions.MaximumExecutionTimeoutSeconds)));
        try
        {
            await ExecutionGate.WaitAsync(timeout.Token);
            return new Lease();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                ExecutionGate.Release();
            }
        }
    }
}
