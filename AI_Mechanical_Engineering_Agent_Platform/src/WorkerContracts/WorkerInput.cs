namespace WorkerContracts;

public sealed record WorkerInput(
    string TaskId,
    string Operation,
    object Payload,
    IReadOnlyDictionary<string, string> Context);
