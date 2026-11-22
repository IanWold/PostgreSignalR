using Microsoft.AspNetCore.SignalR;
using Npgsql;
using PostgreSignalR;
using PostgreSignalR.IntegrationTests.Abstractions;
using PostgreSignalR.IntegrationTests.App;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args
});

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres") ?? throw new Exception();

var isBackplaneReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

var backplaneConfiguration = builder.Configuration.GetSection("Backplane");
var payloadTableConfiguration = builder.Configuration.GetSection("PayloadTable");
var useTableStrategy = builder.Configuration.GetValue("UseTableStrategy", true);
var customPayloadStrategy = builder.Configuration.GetValue<string?>("CustomPayloadStrategy");

var signalRBuilder = builder.Services.AddSignalR(o => o.EnableDetailedErrors = true)
    .AddMessagePackProtocol()
    .AddPostgresBackplane(new NpgsqlDataSourceBuilder(postgresConnectionString).Build(), o =>
    {
        backplaneConfiguration.Bind(o);
        o.OnInitialized += () => isBackplaneReady.TrySetResult();
    });

if (customPayloadStrategy == "Plain")
{
    builder.Services.AddSingleton<IPayloadStrategy, CustomPayloadStrategy>();
}
else if (customPayloadStrategy == "Prefixed")
{
    builder.Services.AddSingleton<IPayloadStrategy, CustomPrefixedPayloadStrategy>();
}
else if (useTableStrategy)
{
    signalRBuilder.AddBackplaneTablePayloadStrategy(payloadTableConfiguration.Bind);
}

builder.Services.AddSingleton<IUserIdProvider, QueryStringUserIdProvider>();

var app = builder.Build();

await app.InitializePostgresBackplanePayloadTableAsync();

app.UseRouting();

app.MapHub<TestHub>("/hub");

app.MapGet("/health", () =>
    isBackplaneReady.Task.IsCompleted
    ? Results.Ok("ready")
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.MapGet("/diagnostics/listener", (HubLifetimeManager<TestHub> lifetimeManager) =>
    lifetimeManager is PostgresHubLifetimeManager<TestHub> postgresLifetimeManager
    ? Results.Ok(new ListenerDiagnostics(postgresLifetimeManager.ListenerChannelCount, postgresLifetimeManager.ListenerListenCount))
    : Results.NotFound()
);

app.MapGet("/diagnostics/payload", (IPayloadStrategy payloadStrategy) =>
    payloadStrategy is CustomPrefixedPayloadStrategy prefixedPayloadStrategy
    ? Results.Ok(new PayloadDiagnostics(prefixedPayloadStrategy.PrefixedResolveCount))
    : Results.NotFound()
);

app.Run();
