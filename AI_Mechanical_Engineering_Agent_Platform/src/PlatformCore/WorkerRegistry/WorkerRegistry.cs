using WorkerContracts;

namespace PlatformCore;

public sealed class WorkerRegistry
{
    private readonly Dictionary<string, IWorker> _workers = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IWorker worker)
    {
        _workers[worker.Name] = worker;
    }

    public IWorker? GetByName(string name) =>
        _workers.TryGetValue(name, out var worker) ? worker : null;

    public IReadOnlyList<IWorker> GetAll() =>
        _workers.Values.OrderBy(worker => worker.Name, StringComparer.OrdinalIgnoreCase).ToArray();
}
