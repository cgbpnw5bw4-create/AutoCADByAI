namespace AgentContracts;

public sealed record AgentInput(
    string Source,
    string Channel,
    string ConversationId,
    string User,
    string Message,
    IReadOnlyList<string> Attachments,
    IReadOnlyDictionary<string, string> Context);
