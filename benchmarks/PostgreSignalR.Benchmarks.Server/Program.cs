using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks.Server;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRouting();
builder.Services.AddSignalR();

var backplane = (Environment.GetEnvironmentVariable("BACKPLANE") ?? throw new Exception()).ToLowerInvariant();

if (backplane is "redis")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? throw new Exception();
    builder.Services.AddSignalR().AddStackExchangeRedis(connectionString);
}
else if (backplane is "postgres")
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new Exception();
    builder.Services.AddSignalR().AddPostgresBackplane(connectionString);
}

var app = builder.Build();

app.MapHub<BenchmarkHub>("/hub");

app.MapGet("/health", () => Results.Ok(new { ok = true, backplane }));

app.MapPost("/publish", async (PublishRequest request, IHubContext<BenchmarkHub> hub, CancellationToken c) =>
{
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
