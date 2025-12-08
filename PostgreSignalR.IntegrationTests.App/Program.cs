using Microsoft.AspNetCore.SignalR;
using PostgreSignalR.IntegrationTests.App;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres") ?? throw new Exception();

var isBackplaneReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

builder.Services.AddSignalR().AddPostgresBackplane(postgresConnectionString, o => o.OnInitialized = () => isBackplaneReady.TrySetResult());

builder.Services.AddSingleton<IUserIdProvider, QueryStringUserIdProvider>();

var app = builder.Build();

app.UseRouting();
app.MapHub<TestHub>("/hub");
app.MapGet("/health", () =>
    isBackplaneReady.Task.IsCompleted
    ? Results.Ok("ready")
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.Run();
