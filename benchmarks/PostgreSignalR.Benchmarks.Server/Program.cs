using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks.Server;
using Microsoft.AspNetCore.SignalR;
using PostgreSignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting();
builder.Services.AddSignalR();

var backplane = (Environment.GetEnvironmentVariable("BACKPLANE") ?? throw new Exception()).ToLowerInvariant();

var usePayloadTable = (Environment.GetEnvironmentVariable("PAYLOAD_STRATEGY") ?? "event").Equals("table", StringComparison.OrdinalIgnoreCase);

if (backplane is "redis")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? throw new Exception();
    builder.Services.AddSignalR().AddStackExchangeRedis(connectionString);
}
else if (backplane is "postgres")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new Exception();
    var signalrBuilder = builder.Services.AddSignalR().AddPostgresBackplane(connectionString);

    if (usePayloadTable)
    {
        signalrBuilder.AddBackplaneTablePayloadStrategy(o =>
        {
            o.AutomaticCleanup = false;
            o.StorageMode = PostgresBackplanePayloadTableStorage.Always;
        });
    }
}

var app = builder.Build();

if (backplane is "postgres" && usePayloadTable)
{
    await app.InitializePostgresBackplanePayloadTableAsync();
}

app.MapHub<BenchmarkHub>("/hub");

app.MapGet("/health", () => Results.Ok(new { ok = true, backplane, payloadStrategy = usePayloadTable ? "table" : "event" }));

SemaphoreSlim? publishSemaphore = null;

app.MapPost("/publish", async (PublishRequest request, IHubContext<BenchmarkHub> hub, CancellationToken c) =>
{
    var semaphore = LazyInitializer.EnsureInitialized(
        ref publishSemaphore,
        () => new SemaphoreSlim(request.Concurrency, request.Concurrency)
    );

    var payload = request.PayloadBytes > 0 ? new string('x', request.PayloadBytes) : string.Empty;
    var sendTasks = new List<Task>(request.PublishCount);

    for (int i = 0; i < request.PublishCount; i++)
    {
        await semaphore.WaitAsync(c).ConfigureAwait(false);

        var message = new Message(
            MessageId: Guid.NewGuid().ToString("N"),
            SentUnixTimeMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            PayloadBytes: request.PayloadBytes,
            Payload: payload
        );

        sendTasks.Add(hub.Clients.All.SendAsync("bench", message, c).ContinueWith(
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
