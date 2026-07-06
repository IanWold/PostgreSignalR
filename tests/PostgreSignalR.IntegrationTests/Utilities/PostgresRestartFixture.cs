using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Npgsql;

namespace PostgreSignalR.IntegrationTests;

public class PostgresRestartFixture(ContainerFixture containerFixture) : IAsyncLifetime
{
    private const string PostgresUser = "postgres";
    private const string PostgresPassword = "admin";

    private INetwork? _network;
    private IContainer? _postgresContainer;

    public DatabaseContainer? Database { get; private set; }
    public TestServer? Server1 { get; private set; }
    public TestServer? Server2 { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder()
            .WithName($"signalr-testnet-pgrestart-{Guid.NewGuid():N}")
            .Build();

        await _network.CreateAsync();

        _postgresContainer = new ContainerBuilder("postgres:16")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres_network")
            .WithEnvironment("POSTGRES_USER", PostgresUser)
            .WithEnvironment("POSTGRES_PASSWORD", PostgresPassword)
            .WithPortBinding(5432, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(5432))
            .Build();

        await _postgresContainer.StartAsync();
        await WaitUntilPostgresReadyAsync();

        Database = new DatabaseContainer(GetConnectionString);
        await Database.InitializeAsync();

        Server1 = new TestServer(await CreateTestServerAsync());
        Server2 = new TestServer(await CreateTestServerAsync());
    }

    private string GetConnectionString() =>
        $"Host=127.0.0.1;Port={_postgresContainer!.GetMappedPublicPort(5432)};Username={PostgresUser};Password={PostgresPassword};Database=postgres;";

    private async Task WaitUntilPostgresReadyAsync()
    {
        for (var i = 0; i < TestTimeouts.HealthCheckMaxAttempts; i++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(GetConnectionString());
                await connection.OpenAsync();
                
                return;
            }
            catch
            {
                await Task.Delay(TestTimeouts.HealthCheckPollInterval);
            }
        }

        throw new TimeoutException("Postgres did not become ready.");
    }

    private Task<TestServerContainer> CreateTestServerAsync() =>
        CreateTestServerAsync(null);

    public async Task<TestServerContainer> CreateTestServerAsync(IReadOnlyDictionary<string, string>? environment)
    {
        var containerBuilder = new ContainerBuilder(containerFixture.TestServerImage)
            .WithName($"signalr-test-pgrestart-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithEnvironment("ConnectionStrings__Postgres", Database!.ConnectionStringInternal)
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080));

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                containerBuilder = containerBuilder.WithEnvironment(key, value);
            }
        }

        var container = containerBuilder.Build();

        await container.StartAsync();
        return new TestServerContainer(container);
    }

    public async Task RestartPostgresAsync()
    {
        await StopPostgresAsync();
        await StartPostgresAsync();
    }

    public async Task StopPostgresAsync()
    {
        await _postgresContainer!.StopAsync();
        await WaitUntilPostgresUnreachableAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    private async Task WaitUntilPostgresUnreachableAsync()
    {
        for (var i = 0; i < TestTimeouts.HealthCheckMaxAttempts; i++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(GetConnectionString());
                await connection.OpenAsync();
            }
            catch
            {
                return;
            }

            await Task.Delay(TestTimeouts.HealthCheckPollInterval);
        }

        throw new TimeoutException("Postgres did not become unreachable.");
    }

    public async Task StartPostgresAsync()
    {
        await _postgresContainer!.StartAsync();
        await WaitUntilPostgresReadyAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Server1!.DisposeAsync();
        await Server2!.DisposeAsync();
        await Database!.DisposeAsync();
        await _postgresContainer!.DisposeAsync();
        await _network!.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
