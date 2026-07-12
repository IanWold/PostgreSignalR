using Npgsql;
using StackExchange.Redis;
using PostgreSignalR.Benchmarks.Abstractions;

var backplane = (Environment.GetEnvironmentVariable("BACKPLANE") ?? "none").ToLowerInvariant();
var enabled = string.Equals(Environment.GetEnvironmentVariable("SIMULATE_SHARED_LOAD"), "true", StringComparison.OrdinalIgnoreCase);

if (!enabled)
{
    Console.WriteLine("SIMULATE_SHARED_LOAD is not enabled; shared load generator idling.");
    await Task.Delay(Timeout.Infinite);
    
    return;
}

var concurrency = int.Parse(Environment.GetEnvironmentVariable("SHARED_LOAD_CONCURRENCY") ?? "16");
var opsPerSec = int.Parse(Environment.GetEnvironmentVariable("SHARED_LOAD_OPS_PER_SEC") ?? "200");
var perWorkerInterval = TimeSpan.FromMilliseconds(1000.0 * concurrency / opsPerSec);

Console.WriteLine($"Shared load starting: backplane={backplane}, concurrency={concurrency}, opsPerSec={opsPerSec}");

long completed = 0;
long failed = 0;

_ = Task.Run(async () =>
{
    while (true)
    {
        await Task.Delay(TimeSpan.FromSeconds(10));
        Console.WriteLine($"Shared load: {Interlocked.Read(ref completed)} ops completed, {Interlocked.Read(ref failed)} failed");
    }
});

if (backplane is "postgres")
{
    var connectionString = ConnectionStringHelper.NormalizePostgres(Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? throw new Exception("Postgres connection string required."));

    await using var dataSource = NpgsqlDataSource.Create(connectionString);
    await using (var setup = dataSource.CreateCommand("CREATE TABLE IF NOT EXISTS shared_load_rows (id BIGSERIAL PRIMARY KEY, payload TEXT NOT NULL, created_at TIMESTAMPTZ NOT NULL DEFAULT now())"))
    {
        await setup.ExecuteNonQueryAsync();
    }

    var workers = Enumerable.Range(0, concurrency).Select(_ => RunPostgresWorkerAsync(dataSource));

    await Task.WhenAll(workers);
}
else if (backplane is "redis")
{
    var connectionString = ConnectionStringHelper.NormalizeRedis(Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? throw new Exception("Redis connection string required."));
    var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
    var db = redis.GetDatabase();
    var workers = Enumerable.Range(0, concurrency).Select(_ => RunRedisWorkerAsync(db));

    await Task.WhenAll(workers);
}
else
{
    throw new Exception($"Unsupported BACKPLANE '{backplane}' for shared load. Expected 'postgres' or 'redis'.");
}

async Task RunPostgresWorkerAsync(NpgsqlDataSource dataSource)
{
    var random = new Random();

    while (true)
    {
        try
        {
            var roll = random.NextDouble();

            if (roll < 0.55)
            {
                await using var cmd = dataSource.CreateCommand("INSERT INTO shared_load_rows (payload) VALUES (@payload)");
                cmd.Parameters.AddWithValue("payload", RandomPayload(random, 200));
                await cmd.ExecuteNonQueryAsync();
            }
            else if (roll < 0.85)
            {
                await using var cmd = dataSource.CreateCommand("SELECT id, payload FROM shared_load_rows ORDER BY id DESC LIMIT 20");
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync()) { }
            }
            else if (roll < 0.97)
            {
                await using var cmd = dataSource.CreateCommand("UPDATE shared_load_rows SET payload = @payload WHERE id = (SELECT id FROM shared_load_rows ORDER BY random() LIMIT 1)");
                cmd.Parameters.AddWithValue("payload", RandomPayload(random, 200));
                await cmd.ExecuteNonQueryAsync();
            }
            else
            {
                await using var cmd = dataSource.CreateCommand("DELETE FROM shared_load_rows WHERE id IN (SELECT id FROM shared_load_rows ORDER BY id ASC LIMIT 50)");
                await cmd.ExecuteNonQueryAsync();
            }

            Interlocked.Increment(ref completed);
        }
        catch
        {
            Interlocked.Increment(ref failed);
        }

        await Task.Delay(perWorkerInterval);
    }
}

async Task RunRedisWorkerAsync(IDatabase database)
{
    var random = new Random();

    while (true)
    {
        try
        {
            var key = $"shared_load:{random.Next(0, 5000)}";
            var roll = random.NextDouble();

            if (roll < 0.45)
            {
                await database.StringSetAsync(key, RandomPayload(random, 200), TimeSpan.FromMinutes(5));
            }
            else if (roll < 0.90)
            {
                await database.StringGetAsync(key);
            }
            else if (roll < 0.98)
            {
                await database.StringIncrementAsync("shared_load:counter");
            }
            else
            {
                await database.KeyDeleteAsync(key);
            }

            Interlocked.Increment(ref completed);
        }
        catch
        {
            Interlocked.Increment(ref failed);
        }

        await Task.Delay(perWorkerInterval);
    }
}

static string RandomPayload(Random random, int bytes)
{
    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    return string.Create(bytes, random, (span, rnd) =>
    {
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = chars[rnd.Next(chars.Length)];
        }
    });
}
