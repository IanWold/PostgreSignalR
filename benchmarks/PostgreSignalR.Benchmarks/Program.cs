using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks;
using HdrHistogram;
using Microsoft.AspNetCore.SignalR.Client;

static string Env(string key, string fallback) =>
    Environment.GetEnvironmentVariable(key) ?? fallback;

var serverUrlsEnv = Environment.GetEnvironmentVariable("SERVER_URLS");
List<string> serverUrls;

if (!string.IsNullOrWhiteSpace(serverUrlsEnv))
{
    serverUrls = serverUrlsEnv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    if (serverUrls.Count < 2)
    {
        throw new Exception($"SERVER_URLS must list at least 2 servers (one publisher, one subscriber), got {serverUrls.Count}.");
    }
}
else
{
    var numServers = int.Parse(Env("NUM_SERVERS", "2"));

    if (numServers is < 2 or > 10)
    {
        throw new Exception($"NUM_SERVERS must be between 2 and 10 (the number of server slots defined in docker-compose.yml), got {numServers}.");
    }

    serverUrls = Enumerable.Range(1, numServers).Select(i => $"http://server{i}:8080").ToList();
}

var publisherUrl = serverUrls[0];
var subscriberUrls = serverUrls.Skip(1).ToList();

var clientsPerServer = int.Parse(Env("CLIENTS_PER_SERVER", "500"));
var clients = clientsPerServer * subscriberUrls.Count;
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
var repeatsPerRate = int.Parse(Env("REPEATS_PER_RATE", "1"));

Console.WriteLine();
Console.WriteLine("Benchmark Starting...");
Console.WriteLine($"Servers ({serverUrls.Count}): {string.Join(", ", serverUrls)}");
Console.WriteLine($"Publisher: {publisherUrl}");
Console.WriteLine($"Subscribers: {string.Join(", ", subscriberUrls)}");
Console.WriteLine($"Clients: {clients} ({clientsPerServer} per subscriber)");
Console.WriteLine($"PublishCount: {publishCount}, Concurrency: {concurrency}, PayloadBytes: {payloadBytes}");
Console.WriteLine($"WarmupSeconds: {warmupSeconds}, MeasureSeconds: {measureSeconds}");
Console.WriteLine($"RepeatsPerRate: {repeatsPerRate}");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

await Task.WhenAll(serverUrls.Select(url => WaitHealthy(http, url)));

var connections = new List<HubConnection>(clients);

var seen = new ConcurrentDictionary<string, byte>(Environment.ProcessorCount, publishCount);
var fanoutCopies = new Counter();
var histogram = new LongHistogram(60000000, 3);

var measuring = false;

for (int i = 0; i < clients; i++)
{
    var hubUrl = $"{subscriberUrls[i % subscriberUrls.Count].TrimEnd('/')}/hub";
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

Console.WriteLine($"Connecting {clients} clients ({clientsPerServer} each) across {subscriberUrls.Count} subscriber(s)...");
await Task.WhenAll(connections.Select(c => c.StartAsync()));
Console.WriteLine("Clients connected.");

Console.WriteLine($"Measurement mode: {mode}");
Console.WriteLine();

Console.WriteLine($"Warmup: {warmupSeconds}s");
await Publish(http, publisherUrl, publishCount: Math.Min(2000, publishCount / 10), concurrency: Math.Max(8, concurrency / 4), payloadBytes);
await Task.Delay(TimeSpan.FromSeconds(warmupSeconds));

if (mode.Equals("sweep", StringComparison.OrdinalIgnoreCase))
{
    async Task<bool> sweep(int rate)
    {
        var result = await RunTrialAsync(
            http,
            publisherUrl,
            rate,
            sweepTrialSeconds,
            repeatsPerRate,
            concurrency,
            payloadBytes,
            batchSize,
            seen,
            fanoutCopies,
            histogram,
            v => measuring = v
        );

        Console.WriteLine(
            $"| {$"{result.TargetRateMsgsPerSec,12}"} |" +
            $" {$"{result.AchievedRateMsgsPerSec,14:F0}"} |" +
            $" {result.P50Us,8} |" +
            $" {result.P95Us,8} |" +
            $" {result.P99Us,8} |" +
            $" {result.MaxUs,8} |" +
            $" {$"{result.Missing,7}"} |" +
            $" {$"{result.FanoutCopies,13}"} |" +
            $" {$"{result.Sent,13}"} |"
        );

        if (result.AchievedRateMsgsPerSec < rate * 0.95)
        {
            Console.WriteLine($"  Warning: achieved {result.AchievedRateMsgsPerSec:F0} msg/s, below the {rate} msg/s target - driver/server could not keep pace, latency at this row reflects a lower effective rate");
        }

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

    Console.WriteLine();
    Console.WriteLine("Sweep up:");
    Console.WriteLine("| Rate (msg/s) | Achieved (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent |");
    Console.WriteLine("|--------------|------------------|----------|----------|----------|----------|---------|---------------|---------------|");

    for (int rate = sweepStartRate; rate <= sweepMaxRate; rate += sweepStepRate)
    {
        if (!await sweep(rate))
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("Sweep down:");
    Console.WriteLine("| Rate (msg/s) | Achieved (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent |");
    Console.WriteLine("|--------------|------------------|----------|----------|----------|----------|---------|---------------|---------------|");

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
        http,
        publisherUrl,
        targetRate,
        measureSeconds,
        repeatsPerRate,
        concurrency,
        payloadBytes,
        batchSize,
        seen,
        fanoutCopies,
        histogram,
        v => measuring = v
    );

    Console.WriteLine();
    Console.WriteLine("Benchmark results:");
    Console.WriteLine($"Target rate: {result.TargetRateMsgsPerSec} msg/s for {measureSeconds}s");
    Console.WriteLine($"Sent: {result.Sent} in {result.SendElapsedSec:F2}s ({result.AchievedRateMsgsPerSec:F0} msg/s achieved)");

    if (result.AchievedRateMsgsPerSec < result.TargetRateMsgsPerSec * 0.95)
    {
        Console.WriteLine($"Warning: achieved rate fell short of the {result.TargetRateMsgsPerSec} msg/s target - driver/server could not keep pace, latency below reflects a lower effective rate");
    }

    Console.WriteLine($"Unique received: {result.UniqueReceived}");
    Console.WriteLine($"Missing: {result.Missing}");
    Console.WriteLine($"Fanout copies (expected): {result.FanoutCopies}");
    Console.WriteLine($"Latency p50: {result.P50Us}us, p95: {result.P95Us}us, p99: {result.P99Us}us, max: {result.MaxUs}us");
}

Console.WriteLine();
Console.WriteLine("Disconnecting clients...");
await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask()));
Console.WriteLine("Done.");

static async Task Publish(HttpClient http, string publisherUrl, int publishCount, int concurrency, int payloadBytes)
{
    var response = await http.PostAsJsonAsync($"{publisherUrl.TrimEnd('/')}/publish", new
    {
        PublishCount = publishCount,
        Concurrency = concurrency,
        PayloadBytes = payloadBytes
    });

    response.EnsureSuccessStatusCode();
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
    string publisherUrl,
    int targetRateMsgsPerSec,
    int trialSeconds,
    int repeats,
    int concurrency,
    int payloadBytes,
    int batchSize,
    ConcurrentDictionary<string, byte> seen,
    Counter fanoutCopies,
    LongHistogram hist,
    Action<bool> setMeasuring
)
{
    var pooledHist = new LongHistogram(60000000, 3);

    long totalSent = 0;
    long totalUnique = 0;
    long totalFanoutCopies = 0;
    double totalElapsedSec = 0;

    for (int repeat = 0; repeat < repeats; repeat++)
    {
        seen.Clear();
        Interlocked.Exchange(ref fanoutCopies.Value, 0);
        lock (hist) hist.Reset();

        setMeasuring(true);

        var totalToSend = Math.Max(1, targetRateMsgsPerSec * trialSeconds);
        var batchPeriod = TimeSpan.FromSeconds(batchSize / (double)targetRateMsgsPerSec);
        var next = Stopwatch.GetTimestamp();
        var tickFreq = (double)Stopwatch.Frequency;

        var sw = Stopwatch.StartNew();

        var inFlight = new List<Task>();

        for (int sent = 0; sent < totalToSend; sent += batchSize)
        {
            var thisBatch = Math.Min(batchSize, totalToSend - sent);

            inFlight.Add(Publish(http, publisherUrl, thisBatch, concurrency, payloadBytes));

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

        await Task.WhenAll(inFlight);
        sw.Stop();

        await Task.Delay(1000);
        setMeasuring(false);

        totalSent += totalToSend;
        totalUnique += seen.Count;
        totalFanoutCopies += Interlocked.Read(ref fanoutCopies.Value);
        totalElapsedSec += sw.Elapsed.TotalSeconds;

        lock (hist) pooledHist.Add(hist);
    }

    var missing = Math.Max(0, totalSent - totalUnique);

    (long p50, long p95, long p99, long max) = GetPercentiles(pooledHist);

    return new TrialResult(
        targetRateMsgsPerSec,
        (int)totalSent,
        totalElapsedSec,
        (int)totalUnique,
        (int)missing,
        totalFanoutCopies,
        p50, p95, p99, max
    );
}
