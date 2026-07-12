using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using PostgreSignalR.Benchmarks.Abstractions;
using PostgreSignalR.Benchmarks;
using HdrHistogram;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;

var consoleBuffer = new StringBuilder();
Console.SetOut(new BufferedTextWriter(Console.Out, consoleBuffer));
Console.SetError(new BufferedTextWriter(Console.Error, consoleBuffer));

var resultsCollectorUrl = Environment.GetEnvironmentVariable("RESULTS_COLLECTOR_URL");

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    consoleBuffer.AppendLine((e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject?.ToString());
    PostResultsToCollectorAsync(resultsCollectorUrl, consoleBuffer.ToString()).GetAwaiter().GetResult();
};

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

var healthCheckTimeoutSeconds = int.Parse(Env("HEALTH_CHECK_TIMEOUT_SECONDS", "60"));

var drainQuietSeconds = double.Parse(Env("DRAIN_QUIET_SECONDS", "1"));
var drainMaxWaitSeconds = double.Parse(Env("DRAIN_MAX_WAIT_SECONDS", "60"));

var backplane = Env("BACKPLANE", "redis").ToLowerInvariant();
var payloadStrategy = Env("PAYLOAD_STRATEGY", "event").ToLowerInvariant();

Console.WriteLine();
Console.WriteLine("Benchmark Starting...");
Console.WriteLine($"Servers ({serverUrls.Count}): {string.Join(", ", serverUrls)}");
Console.WriteLine($"Publisher: {publisherUrl}");
Console.WriteLine($"Subscribers: {string.Join(", ", subscriberUrls)}");
Console.WriteLine($"Clients: {clients} ({clientsPerServer} per subscriber)");
Console.WriteLine($"PublishCount: {publishCount}, Concurrency: {concurrency}, PayloadBytes: {payloadBytes}");
Console.WriteLine($"WarmupSeconds: {warmupSeconds}, MeasureSeconds: {measureSeconds}");
Console.WriteLine($"DrainQuietSeconds: {drainQuietSeconds}, DrainMaxWaitSeconds: {drainMaxWaitSeconds}");
Console.WriteLine($"RepeatsPerRate: {repeatsPerRate}");
Console.WriteLine($"HealthCheckTimeoutSeconds: {healthCheckTimeoutSeconds}");

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

await Task.WhenAll(serverUrls.Select(url => WaitHealthy(http, url, healthCheckTimeoutSeconds)));

var (clockOffsetMs, clockOffsetBestRoundTripMs) = await EstimateClockOffsetMsAsync(http, publisherUrl);
Console.WriteLine($"Estimated clock offset vs publisher ({publisherUrl}): {clockOffsetMs}ms ({(clockOffsetMs >= 0 ? "publisher ahead" : "publisher behind")}, best round-trip {clockOffsetBestRoundTripMs}ms)");

if ((backplane, payloadStrategy) is ("postgres", "table"))
{
    var postgresConnectionString = ConnectionStringHelper.NormalizePostgres(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? "");

    if (string.IsNullOrWhiteSpace(postgresConnectionString))
    {
        Console.WriteLine("Unable to clear backplane_payloads table before running benchmark with table payload strategy; driver was not given a connection string.");
    }
    else
    {
        Console.WriteLine("Clearing backplane_payloads table before running benchmark with table payload strategy...");

        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync();
        
        await using var command = new NpgsqlCommand("TRUNCATE TABLE \"backplane_payloads\";", connection);
        await command.ExecuteNonQueryAsync();
    }
}

var connections = new List<HubConnection>(clients);

var seen = new ConcurrentDictionary<string, byte>(Environment.ProcessorCount, publishCount);
var fanoutCopies = new Counter();
var negativeLatency = new Counter();
var generation = new Counter();
var staleGeneration = new Counter();
var lastStaleTicks = new Counter();

var histogram = HistogramFactory.With64BitBucketSize()
    .WithValuesUpTo(60000000)
    .WithPrecisionOf(3)
    .WithThreadSafeWrites()
    .WithThreadSafeReads()
    .Create();

var measuring = false;

for (int i = 0; i < clients; i++)
{
    var hubUrl = $"{subscriberUrls[i % subscriberUrls.Count].TrimEnd('/')}/hub";
    var connection = new HubConnectionBuilder().WithUrl(hubUrl).WithAutomaticReconnect().Build();

    connection.On<Message>("bench", message =>
    {
        if (message.Generation != Interlocked.Read(ref generation.Value))
        {
            Interlocked.Increment(ref staleGeneration.Value);
            Interlocked.Exchange(ref lastStaleTicks.Value, Stopwatch.GetTimestamp());
            return;
        }

        if (!measuring)
        {
            return;
        }

        if (!seen.TryAdd(message.MessageId, 0))
        {
            Interlocked.Increment(ref fanoutCopies.Value);
            return;
        }

        var rawLatencyUs = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - message.SentUnixTimeMs + clockOffsetMs) * 1000;

        if (rawLatencyUs < 0)
        {
            Interlocked.Increment(ref negativeLatency.Value);
        }

        histogram.RecordValue(Math.Min(Math.Max(rawLatencyUs, 0), 60000000));
    });

    connections.Add(connection);
}

Console.WriteLine($"Connecting {clients} clients ({clientsPerServer} each) across {subscriberUrls.Count} subscriber(s)...");
await Task.WhenAll(connections.Select(c => c.StartAsync()));
Console.WriteLine("Clients connected.");

Console.WriteLine($"Measurement mode: {mode}");
Console.WriteLine();

Console.WriteLine($"Warmup: {warmupSeconds}s");

var warmupResult = await RunTrialAsync(
    http,
    publisherUrl,
    targetRate,
    warmupSeconds,
    1,
    concurrency,
    payloadBytes,
    batchSize,
    seen,
    fanoutCopies,
    negativeLatency,
    generation,
    staleGeneration,
    lastStaleTicks,
    drainQuietSeconds,
    drainMaxWaitSeconds,
    histogram,
    v => measuring = v
);

async Task<int> FinishAsync(int exitCode)
{
    Console.WriteLine();
    Console.WriteLine("Disconnecting clients...");
    await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask()));
    Console.WriteLine("Done.");
    await PostResultsToCollectorAsync(resultsCollectorUrl, consoleBuffer.ToString());
    return exitCode;
}

if (warmupResult.Sent > 0 && warmupResult.UniqueReceived == 0)
{
    Console.WriteLine();
    Console.WriteLine($"Stopping; warmup sent {warmupResult.Sent} messages but received 0.");
    return await FinishAsync(1);
}

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
            negativeLatency,
            generation,
            staleGeneration,
            lastStaleTicks,
            drainQuietSeconds,
            drainMaxWaitSeconds,
            histogram,
            v => measuring = v
        );

        Console.WriteLine(
            $"| {$"{result.TargetRateMsgsPerSec,12}"} |" +
            $" {$"{result.AchievedRateMsgsPerSec,16:F0}"} |" +
            $" {result.P50Us,8} |" +
            $" {result.P95Us,8} |" +
            $" {result.P99Us,8} |" +
            $" {result.MaxUs,8} |" +
            $" {$"{result.Missing,7}"} |" +
            $" {$"{result.FanoutCopies,13}"} |" +
            $" {$"{result.Sent,13}"} |" +
            $" {$"{result.HistogramCount,10}"} |" +
            $" {$"{result.NegativeLatencyCount,11}"} |"
        );

        if (result.AchievedRateMsgsPerSec < rate * 0.95)
        {
            Console.WriteLine($"  Warning: achieved {result.AchievedRateMsgsPerSec:F0} msg/s, below the {rate} msg/s target - driver/server could not keep pace, latency at this row reflects a lower effective rate");
        }

        if (result.Sent > 0 && result.HistogramCount == 0)
        {
            Console.WriteLine($"Stopped: received 0 of {result.Sent} messages sent this trial.");
            return false;
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
    Console.WriteLine("| Rate (msg/s) | Achieved (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent | Hist Count | Neg Latency |");
    Console.WriteLine("|--------------|------------------|----------|----------|----------|----------|---------|---------------|---------------|------------|-------------|");

    for (int rate = sweepStartRate; rate <= sweepMaxRate; rate += sweepStepRate)
    {
        if (!await sweep(rate))
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("Sweep down:");
    Console.WriteLine("| Rate (msg/s) | Achieved (msg/s) | p50 (Us) | p95 (Us) | p99 (Us) | Max (Us) | Missing | Fanout Copies | Messages Sent | Hist Count | Neg Latency |");
    Console.WriteLine("|--------------|------------------|----------|----------|----------|----------|---------|---------------|---------------|------------|-------------|");

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
        negativeLatency,
        generation,
        staleGeneration,
        lastStaleTicks,
        drainQuietSeconds,
        drainMaxWaitSeconds,
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
    Console.WriteLine($"Histogram count: {result.HistogramCount} (compare to Unique received above - should match)");
    Console.WriteLine($"Negative computed latency (clamped to 0): {result.NegativeLatencyCount}");

    if (result.Sent > 0 && result.HistogramCount == 0)
    {
        Console.WriteLine("Warning: received 0 messages (initialization error?).");
    }
}

return await FinishAsync(0);

static async Task Publish(HttpClient http, string publisherUrl, int publishCount, int concurrency, int payloadBytes, long generation)
{
    var response = await http.PostAsJsonAsync($"{publisherUrl.TrimEnd('/')}/publish", new
    {
        PublishCount = publishCount,
        Concurrency = concurrency,
        PayloadBytes = payloadBytes,
        Generation = generation
    });

    response.EnsureSuccessStatusCode();
}

static async Task WaitHealthy(HttpClient http, string baseUrl, int timeoutSeconds)
{
    for (int i = 0; i < timeoutSeconds; i++)
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

    throw new Exception($"Health check failed for {baseUrl} after {timeoutSeconds}s");
}

static async Task PostResultsToCollectorAsync(string? collectorUrl, string output)
{
    if (string.IsNullOrWhiteSpace(collectorUrl))
    {
        return;
    }

    try
    {
        using var client = new HttpClient();
        await client.PostAsync(collectorUrl, new StringContent(output));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to post results to collector: {ex}");
    }
}

static async Task<(long OffsetMs, long BestRoundTripMs)> EstimateClockOffsetMsAsync(HttpClient http, string serverUrl)
{
    var bestOffsetMs = 0D;
    var bestRoundTripMs = double.MaxValue;

    for (int i = 0; i < 15; i++)
    {
        var t0 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        TimeResult? response;

        try
        {
            response = await http.GetFromJsonAsync<TimeResult>($"{serverUrl.TrimEnd('/')}/time");
        }
        catch
        {
            continue;
        }

        var t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (response is null)
        {
            continue;
        }

        var roundTripMs = t2 - t0;

        if (roundTripMs < bestRoundTripMs)
        {
            bestRoundTripMs = roundTripMs;
            bestOffsetMs = response.UnixTimeMs - (t0 + t2) / 2.0;
        }
    }

    var boundedRoundTripMs = bestRoundTripMs == double.MaxValue ? 0 : (long)Math.Round(bestRoundTripMs);
    return ((long)Math.Round(bestOffsetMs), boundedRoundTripMs);
}

static async Task DrainAsync(Counter generation, Counter staleGeneration, Counter lastStaleTicks, double quietSeconds, double maxWaitSeconds)
{
    var staleAtStart = Interlocked.Read(ref staleGeneration.Value);

    Interlocked.Increment(ref generation.Value);
    Interlocked.Exchange(ref lastStaleTicks.Value, Stopwatch.GetTimestamp());

    var tickFrequency = (double)Stopwatch.Frequency;
    var start = Stopwatch.GetTimestamp();

    while (true)
    {
        var now = Stopwatch.GetTimestamp();
        var sinceLastStale = (now - Interlocked.Read(ref lastStaleTicks.Value)) / tickFrequency;

        if (sinceLastStale >= quietSeconds)
        {
            break;
        }

        if ((now - start) / tickFrequency >= maxWaitSeconds)
        {
            Console.WriteLine($"  Warning: drain wait hit the {maxWaitSeconds:F0}s cap with stragglers still trickling in - proceeding anyway");
            break;
        }

        await Task.Delay(250);
    }

    var strayCount = Interlocked.Read(ref staleGeneration.Value) - staleAtStart;
    if (strayCount > 0)
    {
        var waitedSeconds = (Stopwatch.GetTimestamp() - start) / tickFrequency;
        Console.WriteLine($"  Drained {strayCount} stale-generation stragglers over {waitedSeconds:F1}s before starting the next window");
    }
}

static (long p50, long p95, long p99, long max) GetPercentiles(LongHistogram hist)
{
    if (hist.TotalCount == 0)
    {
        return (0, 0, 0, 0);
    }

    var p50 = hist.GetValueAtPercentile(50);
    var p95 = hist.GetValueAtPercentile(95);
    var p99 = hist.GetValueAtPercentile(99);
    var max = hist.GetMaxValue();

    return (p50, p95, p99, max);
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
    Counter negativeLatency,
    Counter generation,
    Counter staleGeneration,
    Counter lastStaleTicks,
    double drainQuietSeconds,
    double drainMaxWaitSeconds,
    Recorder hist,
    Action<bool> setMeasuring
)
{
    var pooledHist = new LongHistogram(60000000, 3);

    long totalSent = 0;
    long totalUnique = 0;
    long totalFanoutCopies = 0;
    long totalNegativeLatency = 0;
    double totalElapsedSec = 0;

    for (int repeat = 0; repeat < repeats; repeat++)
    {
        seen.Clear();
        Interlocked.Exchange(ref fanoutCopies.Value, 0);
        Interlocked.Exchange(ref negativeLatency.Value, 0);

        var thisGeneration = Interlocked.Increment(ref generation.Value);

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

            inFlight.Add(Publish(http, publisherUrl, thisBatch, concurrency, payloadBytes, thisGeneration));

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
        totalNegativeLatency += Interlocked.Read(ref negativeLatency.Value);
        totalElapsedSec += sw.Elapsed.TotalSeconds;

        pooledHist.Add(hist.GetIntervalHistogram());

        await DrainAsync(generation, staleGeneration, lastStaleTicks, drainQuietSeconds, drainMaxWaitSeconds);
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
        p50, p95, p99, max,
        pooledHist.TotalCount,
        totalNegativeLatency
    );
}
