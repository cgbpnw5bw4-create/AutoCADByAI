using PlatformCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(PlatformBootstrapper.CreateDefault());

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new
{
    service = "AI Mechanical Engineering Agent Platform API Host",
    status = "ok"
}));

app.Run();
