using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks;
using HdrHistogram;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

static string Env(string key, string fallback) =>
    Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;

var serverA = Env("SERVER_A", "http://servera:8080");
var serverB = Env("SERVER_B", "http://serverb:8080");

// SERVERS (comma separated) replaces SERVER_A/SERVER_B. The first server publishes, clients connect to the rest.
var serverList = Env("SERVERS", "");
string[] servers = serverList.Length > 0
    ? serverList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : [serverA, serverB];

if (servers.Length == 0)
{
    throw new Exception("SERVERS must list at least one server.");
}

var sender = servers[0];
var clientServerOffset = servers.Length > 1 ? 1 : 0;
var clientServers = servers[clientServerOffset..];

var clients = int.Parse(Env("CLIENTS", "500"));
var publishCount = int.Parse(Env("PUBLISH_COUNT", "20000"));
var concurrency = int.Parse(Env("CONCURRENCY", "128"));
var payloadBytes = int.Parse(Env("PAYLOAD_BYTES", "128"));

var warmupSeconds = int.Parse(Env("WARMUP_SECONDS", "10"));
var measureSeconds = int.Parse(Env("MEASURE_SECONDS", "30"));

var mode = Env("MODE", "single");
var targetRate = int.Parse(Env("TARGET_RATE", "100"));
var sloP99Ms = int.Parse(Env("SLO_P99_MS", "250"));

var sweepStartRate = int.Parse(Env("SWEEP_START_RATE", "100"));
var sweepStepRate  = int.Parse(Env("SWEEP_STEP_RATE", "100"));
var sweepMaxRate   = int.Parse(Env("SWEEP_MAX_RATE", "2000"));
var sweepTrialSeconds = int.Parse(Env("SWEEP_TRIAL_SECONDS", "15"));

var batchSize = int.Parse(Env("BATCH_SIZE", "25"));

// Who each message is sent to: all (default), connection, user or group.
var target = Env("TARGET", "all").ToLowerInvariant();
var userCount = Math.Min(int.Parse(Env("USER_COUNT", clients.ToString())), clients);
var groupCount = Math.Min(int.Parse(Env("GROUP_COUNT", "10")), clients);

// MODE=churn connects, pings and disconnects in a loop; MODE=churn-group joins a group instead of pinging.
var isChurn = mode.Equals("churn", StringComparison.OrdinalIgnoreCase) || mode.Equals("churn-group", StringComparison.OrdinalIgnoreCase);
var churnJoinGroup = mode.Equals("churn-group", StringComparison.OrdinalIgnoreCase);
var churnConcurrency = int.Parse(Env("CHURN_CONCURRENCY", "16"));
var churnSeconds = int.Parse(Env("CHURN_SECONDS", measureSeconds.ToString()));

var targetCount = target switch
{
    "all" => 1,
    "connection" => clients,
    "user" => userCount,
    "group" => groupCount,
    _ => throw new Exception($"Unknown TARGET '{target}': use all, connection, user or group.")
};

if (targetCount < 1)
{
    throw new Exception($"TARGET={target} needs at least one client and at least one {target}.");
}

Console.WriteLine();
Console.WriteLine("Benchmark Starting...");
if (serverList.Length == 0)
{
    Console.WriteLine($"ServerA: {serverA}");
    Console.WriteLine($"ServerB: {serverB}");
}
else
{
    Console.WriteLine($"Servers: {string.Join(", ", servers)} (publishing from {sender})");
}
Console.WriteLine($"Clients: {clients}");
Console.WriteLine($"PublishCount: {publishCount}, Concurrency: {concurrency}, PayloadBytes: {payloadBytes}");
Console.WriteLine($"WarmupSeconds: {warmupSeconds}, MeasureSeconds: {measureSeconds}");
if (target is not "all")
{
    Console.WriteLine($"Target: {target}, {targetCount} {target}s");
}
if (isChurn)
{
    Console.WriteLine($"ChurnConcurrency: {churnConcurrency}, ChurnSeconds: {churnSeconds}");
}

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

foreach (var server in servers)
{
    await WaitHealthy(http, server);
}

var connections = new List<HubConnection>(clients);
var clientsPerServer = new int[servers.Length];

var seen = new ConcurrentDictionary<string, byte>(Environment.ProcessorCount, publishCount);
var fanoutCopies = new Counter();
var histogram = new LongHistogram(60000000, 3);

var measuring = false;

for (int i = 0; i < clients; i++)
{
    // Clients fill the client servers in contiguous blocks, while users and groups are assigned round robin,
    // so a user's connections and a group's members are spread across servers.
    var serverIndex = clientServerOffset + (int)((long)i * clientServers.Length / clients);
    clientsPerServer[serverIndex]++;

    var hubUrl = $"{servers[serverIndex].TrimEnd('/')}/hub";

    if (target is "user")
    {
        hubUrl += $"?user={UserName(i % userCount)}";
    }

    var connection = new HubConnectionBuilder().WithUrl(hubUrl).WithAutomaticReconnect().Build();

    connection.On<Message>("bench", message =>
    {
        if (!measuring)
        {
            return;
        }

        if (!seen.TryAdd(message.MessageId, 0))
        {
            Interlocked.Increment(ref fanoutCopies.Value);
            return;
        }

        lock (histogram)
        {
            histogram.RecordValue(Math.Min(Math.Max((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - message.SentUnixTimeMs) * 1000, 0), 60000000));
        }
    });

    connections.Add(connection);
}

Console.WriteLine($"Connecting {clients} clients to {string.Join(", ", clientServers.Select(s => $"{s.TrimEnd('/')}/hub"))}...");
await Task.WhenAll(connections.Select(c => c.StartAsync()));
Console.WriteLine("Clients connected.");

if (target is "group")
{
    Console.WriteLine($"Joining clients to {groupCount} groups...");
    await Task.WhenAll(connections.Select((c, i) => c.InvokeAsync("JoinGroup", GroupName(i % groupCount))));
}

// Message i goes to targets[i % targets.Length]. Shuffled so consecutive messages go to different servers.
string[] targets = target switch
{
    "connection" => [.. connections.Select(c => c.ConnectionId!)],
    "user" => [.. Enumerable.Range(0, userCount).Select(UserName)],
    "group" => [.. Enumerable.Range(0, groupCount).Select(GroupName)],
    _ => []
};
new Random(42).Shuffle(targets);

Console.WriteLine($"Measurement mode: {mode}");
Console.WriteLine();

if (!isChurn)
{
    Console.WriteLine($"Warmup: {warmupSeconds}s");
    await Publish(http, sender, publishCount: Math.Min(2000, publishCount / 10), concurrency: Math.Max(8, concurrency / 4), payloadBytes, target, targets);
    await Task.Delay(TimeSpan.FromSeconds(warmupSeconds));
}

if (isChurn)
{
    var cycleHistogram = new LongHistogram(60000000, 3);

    Console.WriteLine($"Warmup: {warmupSeconds}s");
    await RunChurnAsync(clientServers, churnJoinGroup, churnConcurrency, warmupSeconds, cycleHistogram);
    var before = await WaitForSteadyListenersAsync(http, servers);

    Console.WriteLine();
    Console.WriteLine($"Churn run: {churnConcurrency} concurrent loops of connect, {(churnJoinGroup ? "join a new group" : "ping")}, disconnect for {churnSeconds}s");

    var result = await RunChurnAsync(clientServers, churnJoinGroup, churnConcurrency, churnSeconds, cycleHistogram);
    var (drained, drainTime, after) = await WaitForDrainAsync(http, servers, before);

    Console.WriteLine();
    Console.WriteLine("Benchmark results:");
    Console.WriteLine($"Cycles: {result.Cycles} in {result.ElapsedSec:F2}s, failed: {result.Failed}");
    Console.WriteLine($"Connects+disconnects per second: {result.Cycles / result.ElapsedSec:F1}");

    if (before.All(s => s is null))
    {
        Console.WriteLine("Listener drain: n/a (no /listener endpoint)");
    }
    else if (drained)
    {
        Console.WriteLine($"Listener drain: {drainTime.TotalSeconds:F2}s, connects+disconnects per second including drain: {result.Cycles / (result.ElapsedSec + drainTime.TotalSeconds):F1}");
    }
    else
    {
        Console.WriteLine($"Listener drain: channels still above the pre-run count after {drainTime.TotalSeconds:F0}s");
    }

    Console.WriteLine($"Cycle latency p50: {result.P50Us}us, p95: {result.P95Us}us, p99: {result.P99Us}us, max: {result.MaxUs}us");

    if (result.FirstError is not null)
    {
        Console.WriteLine($"First failure: {result.FirstError}");
    }

    Console.WriteLine();
    PrintListenerStats(servers, null, clientsPerServer, after, before);
}
else if (mode.Equals("sweep", StringComparison.OrdinalIgnoreCase))
{
    async Task<bool> sweep(int rate)
    {
        var result = await RunTrialAsync(
            http, sender,
            rate, sweepTrialSeconds,
            concurrency, payloadBytes,
            batchSize,
            target, targets,
            seen, fanoutCopies,
            histogram,
            v => measuring = v
        );

        Console.WriteLine(
            $"| {$"{result.TargetRateMsgsPerSec,12}"} |" +
            $" {result.P50Us,8} |" +
            $" {result.P95Us,8} |" +
            $" {result.P99Us,8} |" +
            $" {result.MaxUs,8} |" +
            $" {$"{result.Missing,7}"} |" +
            $" {$"{result.FanoutCopies,13}"} |" +
            $" {$"{result.Sent,13}"} |"
        );

        if (result.P99Us > sloP99Ms * 1000L)
        {
            Console.WriteLine($"Stopped: p99 {result.P99Us/1000.0}ms exceeded SLO {sloP99Ms}ms");
            return false;
        }

        return true;
    }

    Console.WriteLine("Sweep:");
    Console.WriteLine($"  start={sweepStartRate} ");
    Console.WriteLine($"  step={sweepStepRate} ");
    Console.WriteLine($"  max={sweepMaxRate}");
    Console.WriteLine($"  trial={sweepTrialSeconds}s SLO(p99)<={sloP99Ms}ms");
    if (target is not "all")
    {
        Console.WriteLine($"  target={target} ({targets.Length} {target}s)");
    }

    Console.WriteLine();
    Console.WriteLine("Sweep up:");
    Console.WriteLine("| Rate (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent |");
    Console.WriteLine("|--------------|----------|----------|----------|----------|---------|---------------|---------------|");

    for (int rate = sweepStartRate; rate <= sweepMaxRate; rate += sweepStepRate)
    {
        if (!await sweep(rate))
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("Sweep down:");
    Console.WriteLine("| Rate (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent |");
    Console.WriteLine("|--------------|----------|----------|----------|----------|---------|---------------|---------------|");

    for (int rate = sweepMaxRate; rate >= sweepStartRate; rate -= sweepStepRate)
    {
        if (!await sweep(rate))
        {
            break;
        }
    }
}
else
{
    Console.WriteLine();
    Console.WriteLine($"Single run: targetRate={targetRate} msg/s for {measureSeconds}s");

    var result = await RunTrialAsync(
        http, sender,
        targetRate, measureSeconds,
        concurrency, payloadBytes,
        batchSize,
        target, targets,
        seen, fanoutCopies,
        histogram,
        v => measuring = v
    );

    Console.WriteLine();
    Console.WriteLine("Benchmark results:");
    Console.WriteLine($"Target rate: {result.TargetRateMsgsPerSec} msg/s for {measureSeconds}s");
    if (target is not "all")
    {
        Console.WriteLine($"Sent to: {target} ({targets.Length} {target}s, rotating)");
    }
    Console.WriteLine($"Sent: {result.Sent} in {result.SendElapsedSec:F2}s");
    Console.WriteLine($"Unique received: {result.UniqueReceived}");
    Console.WriteLine($"Missing: {result.Missing}");
    Console.WriteLine($"Fanout copies (expected): {result.FanoutCopies}");
    Console.WriteLine($"Latency p50: {result.P50Us}us, p95: {result.P95Us}us, p99: {result.P99Us}us, max: {result.MaxUs}us");
}

if (!isChurn && target is not "all")
{
    Console.WriteLine();
    PrintListenerStats(servers, servers.Length > 1 ? sender : null, clientsPerServer, await GetListenerStatsAsync(http, servers));
}

Console.WriteLine();
Console.WriteLine("Disconnecting clients...");
await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask()));
Console.WriteLine("Done.");

static string UserName(int i) => $"user-{i}";

static string GroupName(int i) => $"group-{i}";

static async Task Publish(HttpClient http, string sender, int publishCount, int concurrency, int payloadBytes, string target, string[] targets)
{
    var response = await http.PostAsJsonAsync($"{sender.TrimEnd('/')}/publish", new
    {
        PublishCount = publishCount,
        Concurrency = concurrency,
        PayloadBytes = payloadBytes,
        Target = target,
        Targets = targets
    });

    response.EnsureSuccessStatusCode();
}

static async Task<ListenerStats?[]> GetListenerStatsAsync(HttpClient http, string[] servers) =>
    await Task.WhenAll(servers.Select(async server =>
    {
        try
        {
            using var response = await http.GetAsync($"{server.TrimEnd('/')}/listener");
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<ListenerStats>() : null;
        }
        catch
        {
            return null;
        }
    }));

// Waits until no server's channel count has changed for a second, so the server-side work from earlier disconnects is done.
static async Task<ListenerStats?[]> WaitForSteadyListenersAsync(HttpClient http, string[] servers)
{
    var stats = await GetListenerStatsAsync(http, servers);
    var steadyPolls = 0;
    var sw = Stopwatch.StartNew();

    while (steadyPolls < 4 && sw.Elapsed < TimeSpan.FromSeconds(30))
    {
        await Task.Delay(250);

        var next = await GetListenerStatsAsync(http, servers);
        steadyPolls = next.Select(s => s?.ChannelCount).SequenceEqual(stats.Select(s => s?.ChannelCount)) ? steadyPolls + 1 : 0;
        stats = next;
    }

    return stats;
}

// A disconnect returns before the server has unsubscribed its channels, so wait for every server to get back to its
// pre-run channel count to see how long the server-side work behind the run took.
static async Task<(bool Drained, TimeSpan Elapsed, ListenerStats?[] Stats)> WaitForDrainAsync(HttpClient http, string[] servers, ListenerStats?[] before)
{
    var sw = Stopwatch.StartNew();

    while (true)
    {
        var stats = await GetListenerStatsAsync(http, servers);
        var drained = stats.Zip(before).All(s => s.First is null || s.Second is null || s.First.ChannelCount <= s.Second.ChannelCount);

        if (drained || sw.Elapsed > TimeSpan.FromSeconds(60))
        {
            return (drained, sw.Elapsed, stats);
        }

        await Task.Delay(100);
    }
}

static void PrintListenerStats(string[] servers, string? sender, int[] clientsPerServer, ListenerStats?[] stats, ListenerStats?[]? before = null)
{
    Console.WriteLine("Listener channels:");
    Console.WriteLine($"| Server | Clients | Routing | Channels | LISTENs |{(before is null ? "" : " Channels before run | LISTENs during run |")}");
    Console.WriteLine($"|--------|---------|---------|----------|---------|{(before is null ? "" : "---------------------|--------------------|")}");

    for (int i = 0; i < servers.Length; i++)
    {
        var s = stats[i];
        var row = $"| {servers[i]}{(servers[i] == sender ? " (sender)" : "")} | {clientsPerServer[i]} | {s?.ChannelRouting ?? "n/a"} | {s?.ChannelCount.ToString() ?? "n/a"} | {s?.ListenCount.ToString() ?? "n/a"} |";

        if (before is not null)
        {
            var b = before[i];
            row += $" {b?.ChannelCount.ToString() ?? "n/a"} | {(s is null || b is null ? "n/a" : (s.ListenCount - b.ListenCount).ToString())} |";
        }

        Console.WriteLine(row);
    }
}

// Each worker loops: connect, then Ping (or JoinGroup) so the cycle waits for the backplane to set the connection up, then disconnect.
static async Task<ChurnResult> RunChurnAsync(string[] servers, bool joinGroup, int concurrency, int seconds, LongHistogram hist)
{
    lock (hist) hist.Reset();

    long cycles = 0, failed = 0, next = -1;
    string? firstError = null;

    var deadline = Stopwatch.GetTimestamp() + seconds * Stopwatch.Frequency;
    var sw = Stopwatch.StartNew();

    await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async _ =>
    {
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var n = Interlocked.Increment(ref next);
            var started = Stopwatch.GetTimestamp();

            try
            {
                // WebSockets without negotiation: one socket per cycle, so the driver doesn't run out of ports.
                await using var connection = new HubConnectionBuilder()
                    .WithUrl($"{servers[n % servers.Length].TrimEnd('/')}/hub", o =>
                    {
                        o.SkipNegotiation = true;
                        o.Transports = HttpTransportType.WebSockets;
                    })
                    .Build();

                await connection.StartAsync();

                if (joinGroup)
                {
                    await connection.InvokeAsync("JoinGroup", $"churn-{n}");
                }
                else
                {
                    await connection.InvokeAsync("Ping");
                }

                await connection.StopAsync();

                Interlocked.Increment(ref cycles);

                lock (hist)
                {
                    hist.RecordValue(Math.Min((long)Stopwatch.GetElapsedTime(started).TotalMicroseconds, 60000000));
                }
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                Interlocked.CompareExchange(ref firstError, ex.Message, null);
            }
        }
    }));

    sw.Stop();

    (long p50, long p95, long p99, long max) = GetPercentiles(hist);

    return new ChurnResult(
        Interlocked.Read(ref cycles),
        Interlocked.Read(ref failed),
        sw.Elapsed.TotalSeconds,
        firstError,
        p50, p95, p99, max
    );
}

static async Task WaitHealthy(HttpClient http, string baseUrl)
{
    for (int i = 0; i < 60; i++)
    {
        try
        {
            var response = await http.GetAsync($"{baseUrl.TrimEnd('/')}/health");
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch { /* swallow */ }

        await Task.Delay(1000);
    }

    throw new Exception($"Health check failed for {baseUrl}");
}

static (long p50, long p95, long p99, long max) GetPercentiles(LongHistogram hist)
{
    lock (hist)
    {
        var p50 = hist.GetValueAtPercentile(50);
        var p95 = hist.GetValueAtPercentile(95);
        var p99 = hist.GetValueAtPercentile(99);
        var max = hist.GetMaxValue();

        return (p50, p95, p99, max);
    }
}

static async Task<TrialResult> RunTrialAsync(
    HttpClient http,
    string sender,
    int targetRateMsgsPerSec,
    int trialSeconds,
    int concurrency,
    int payloadBytes,
    int batchSize,
    string target,
    string[] targets,
    ConcurrentDictionary<string, byte> seen,
    Counter fanoutCopies,
    LongHistogram hist,
    Action<bool> setMeasuring)
{
    seen.Clear();
    Interlocked.Exchange(ref fanoutCopies.Value, 0);
    lock (hist) hist.Reset();

    setMeasuring(true);

    var delay = TimeSpan.FromMilliseconds((int)Math.Max(0, Math.Round(1000.0 * batchSize / targetRateMsgsPerSec)));

    var totalToSend = Math.Max(1, targetRateMsgsPerSec * trialSeconds);

    var batchPeriod = TimeSpan.FromSeconds(batchSize / (double)targetRateMsgsPerSec);
    var next = Stopwatch.GetTimestamp();
    var tickFreq = (double)Stopwatch.Frequency;

    var sw = Stopwatch.StartNew();

    for (int sent = 0; sent < totalToSend; sent += batchSize)
    {
        var thisBatch = Math.Min(batchSize, totalToSend - sent);

        // Continue the rotation through the targets from where the previous batch left off.
        string[] batchTargets = targets.Length == 0 ? [] : [.. Enumerable.Range(sent, thisBatch).Select(i => targets[i % targets.Length])];

        await Publish(http, sender, thisBatch, concurrency, payloadBytes, target, batchTargets);

        next += (long)(batchPeriod.TotalSeconds * tickFreq);
        var now = Stopwatch.GetTimestamp();
        var remainingTicks = next - now;

        if (remainingTicks > 0)
        {
            var remainingMs = (int)(remainingTicks * 1000.0 / tickFreq);

            if (remainingMs > 0)
            {
                await Task.Delay(remainingMs);
            }
            else
            {
                await Task.Yield();
            }
        }
    }

    sw.Stop();

    await Task.Delay(1000);
    setMeasuring(false);

    var unique = seen.Count;
    var missing = Math.Max(0, totalToSend - unique);

    (long p50, long p95, long p99, long max) = GetPercentiles(hist);

    return new TrialResult(
        targetRateMsgsPerSec,
        totalToSend,
        sw.Elapsed.TotalSeconds,
        unique,
        missing,
        Interlocked.Read(ref fanoutCopies.Value),
        p50, p95, p99, max
    );
}
