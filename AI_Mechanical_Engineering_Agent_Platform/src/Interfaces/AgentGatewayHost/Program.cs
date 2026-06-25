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
    AgentMessageDispatcher dispatcher) =>
{
    var response = await dispatcher.DispatchAsync(agentId, request);
    return response is null ? Results.NotFound() : Results.Ok(response);
});

app.Run();

public partial class Program
{
}
