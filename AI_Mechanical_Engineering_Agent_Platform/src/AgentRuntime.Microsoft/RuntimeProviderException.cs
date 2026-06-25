using System.Net;

namespace AgentRuntime.Microsoft;

public sealed class RuntimeProviderException : Exception
{
    public RuntimeProviderException(
        string issueType,
        string message,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IssueType = issueType;
        StatusCode = statusCode;
    }

    public string IssueType { get; }

    public HttpStatusCode? StatusCode { get; }
}
