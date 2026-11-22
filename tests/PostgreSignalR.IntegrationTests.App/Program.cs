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
var reconnectedCount = 0;

var backplaneConfiguration = builder.Configuration.GetSection("Backplane");
var payloadTableConfiguration = builder.Configuration.GetSection("PayloadTable");
var useTableStrategy = builder.Configuration.GetValue("UseTableStrategy", true);
var customPayloadStrategy = builder.Configuration.GetValue<string?>("CustomPayloadStrategy");
var backplaneHubOptIn = builder.Configuration.GetValue<string?>("BackplaneHubOptIn");

var signalRBuilder = builder.Services.AddSignalR(o => o.EnableDetailedErrors = true)
    .AddMessagePackProtocol();

// Opting in TestHub leaves SecondHub off the backplane.
if (backplaneHubOptIn == "BeforeAddPostgresBackplane")
{
    signalRBuilder.AddBackplaneHub<TestHub>();
}

signalRBuilder.AddPostgresBackplane(new NpgsqlDataSourceBuilder(postgresConnectionString).Build(), o =>
{
    backplaneConfiguration.Bind(o);
    o.OnInitialized += () => isBackplaneReady.TrySetResult();
    o.OnReconnected += () => Interlocked.Increment(ref reconnectedCount);
});

if (backplaneHubOptIn == "AfterAddPostgresBackplane")
{
    signalRBuilder.AddBackplaneHub<TestHub>();
}

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
app.MapHub<SecondHub>("/second-hub");

app.MapGet("/health", () =>
    isBackplaneReady.Task.IsCompleted
    ? Results.Ok("ready")
    : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.MapGet("/diagnostics/listener", (HubLifetimeManager<TestHub> lifetimeManager) =>
    lifetimeManager is PostgresHubLifetimeManager<TestHub> postgresLifetimeManager
    ? Results.Ok(new ListenerDiagnostics(postgresLifetimeManager.ListenerChannelCount, postgresLifetimeManager.ListenerListenCount, Volatile.Read(ref reconnectedCount)))
    : Results.NotFound()
);

app.MapGet("/diagnostics/hubs", (HubLifetimeManager<TestHub> testHubLifetimeManager, HubLifetimeManager<SecondHub> secondHubLifetimeManager) =>
    Results.Ok(new HubDiagnostics(
        testHubLifetimeManager is PostgresHubLifetimeManager<TestHub>,
        secondHubLifetimeManager is PostgresHubLifetimeManager<SecondHub>
    ))
);

app.MapGet("/diagnostics/payload", (IPayloadStrategy payloadStrategy) =>
    payloadStrategy is CustomPrefixedPayloadStrategy prefixedPayloadStrategy
    ? Results.Ok(new PayloadDiagnostics(prefixedPayloadStrategy.PrefixedResolveCount))
    : Results.NotFound()
);

app.Run();
