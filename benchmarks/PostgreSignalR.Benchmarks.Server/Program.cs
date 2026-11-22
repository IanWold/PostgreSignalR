using PostgreSignalR;
using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks.Server;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, QueryStringUserIdProvider>();

var backplane = (Environment.GetEnvironmentVariable("BACKPLANE") ?? throw new Exception()).ToLowerInvariant();

// PerTarget (default) or Shared. Only used by the postgres backplane; every server must use the same value.
var channelRouting = Enum.Parse<ChannelRouting>(
    Environment.GetEnvironmentVariable("CHANNEL_ROUTING") is { Length: > 0 } routing ? routing : nameof(ChannelRouting.PerTarget),
    ignoreCase: true
);

// Uncomment if running benchmarks with payload table
//var instantiatePayloadTable = bool.Parse(Environment.GetEnvironmentVariable("MAKETABLE") ?? "false");

if (backplane is "redis")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? throw new Exception();
    builder.Services.AddSignalR().AddStackExchangeRedis(connectionString);
}
else if (backplane is "postgres")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new Exception();
    builder.Services.AddSignalR().AddPostgresBackplane(connectionString, o => o.ChannelRouting = channelRouting);

    // Uncomment if running benchmarks with payload table
    // .AddBackplaneTablePayloadStrategy(o =>
    // {
    //     o.AutomaticCleanup = false;
    //     o.StorageMode = PostgreSignalR.PostgresBackplanePayloadTableStorage.Always;
    // });
}

var app = builder.Build();

// Uncomment if running benchmarks with payload table
// if (backplane is "postgres")
// {
//     await app.InitializePostgresBackplanePayloadTableAsync();
// }

app.MapHub<BenchmarkHub>("/hub");

app.MapGet("/health", () => Results.Ok(new { ok = true, backplane }));

if (backplane is "postgres" && app.Services.GetRequiredService<HubLifetimeManager<BenchmarkHub>>() is PostgresHubLifetimeManager<BenchmarkHub> lifetimeManager)
{
    // The backplane initializes on the first connection or send, so both counters are 0 until then.
    app.MapGet("/listener", () => Results.Ok(new ListenerStats(
        channelRouting.ToString(),
        lifetimeManager.ListenerChannelCount,
        lifetimeManager.ListenerListenCount
    )));
}

app.MapPost("/publish", async (PublishRequest request, IHubContext<BenchmarkHub> hub, CancellationToken c) =>
{
    var target = request.Target?.ToLowerInvariant() ?? "all";

    if (target is not "all" && (target is not ("connection" or "user" or "group") || request.Targets is not { Length: > 0 }))
    {
        return Results.BadRequest(new { error = "Target must be all, connection, user or group, and targeted sends need Targets." });
    }

    IClientProxy Recipients(int i) => target switch
    {
        "connection" => hub.Clients.Client(request.Targets![i % request.Targets.Length]),
        "user" => hub.Clients.User(request.Targets![i % request.Targets.Length]),
        "group" => hub.Clients.Group(request.Targets![i % request.Targets.Length]),
        _ => hub.Clients.All
    };

    var semaphore = new SemaphoreSlim(request.Concurrency, request.Concurrency);
    var sendTasks = new List<Task>(request.PublishCount);

    for (int i = 0; i < request.PublishCount; i++)
    {
        await semaphore.WaitAsync(c).ConfigureAwait(false);

        var message = new Message(
            MessageId: Guid.NewGuid().ToString("N"),
            SentUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PayloadBytes: request.PayloadBytes
        );

        sendTasks.Add(Recipients(i).SendAsync("bench", message, c).ContinueWith(
            _ => semaphore.Release(),
            c,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        ));
    }

    await Task.WhenAll(sendTasks).ConfigureAwait(false);
    return Results.Ok(new { published = request.PublishCount, concurrency = request.Concurrency, payloadBytes = request.PayloadBytes });
});

app.Run();
