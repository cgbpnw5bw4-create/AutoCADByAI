using AgentGatewayHost;
using AgentRuntime.Microsoft;
using PlatformCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddSingleton(RuntimePlatformFactory.CreateDefault());
builder.Services.AddSingleton(serviceProvider =>
{
    var platform = serviceProvider.GetRequiredService<PlatformKernel>();
    return new AgentDirectoryService(platform.AgentRegistry, platform.PermissionManager);
});
builder.Services.AddSingleton<AgentMessageDispatcher>();

var app = builder.Build();

app.MapGet("/agents", (AgentDirectoryService directory) => directory.GetVisibleAgents());

app.MapPost("/agents/{agentId}/message", async (
    string agentId,
    GatewayMessageRequest request,
    AgentMessageDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await dispatcher.DispatchAsync(agentId, request, cancellationToken);
        return response is null ? Results.NotFound() : Results.Ok(response);
    }
    catch (ArgumentException ex) { return Results.BadRequest(new { failure_stage = "gateway_invalid_request", reason = ex.Message }); }
});

app.MapGet("/tasks/{taskId}", (string taskId, HttpRequest request, AgentMessageDispatcher dispatcher) =>
{
    var task = dispatcher.GetTask(taskId, request.Headers["X-Task-Access-Token"].ToString());
    return task is null ? Results.NotFound() : Results.Ok(task);
});

app.MapPost("/tasks/{taskId}/approvals", async (string taskId, GatewayApprovalRequest approval,
    HttpRequest request, AgentMessageDispatcher dispatcher, CancellationToken cancellationToken) =>
{
    var result = await dispatcher.SubmitApprovalAsync(taskId, request.Headers["X-Task-Access-Token"].ToString(), approval, cancellationToken);
    return result.NotFound ? Results.NotFound() : result.Accepted ? Results.Ok(result) : Results.Conflict(result);
});

app.Run();

public partial class Program
{
}
