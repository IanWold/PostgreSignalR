using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace PostgreSignalR.IntegrationTests;

public class PostgresRestartFixture(ContainerFixture containerFixture) : IAsyncLifetime
{
    private INetwork? _network;

    public PostgreSqlContainer? PostgresContainer { get; private set; }
    public DatabaseContainer? Database { get; private set; }
    public TestServer? Server1 { get; private set; }
    public TestServer? Server2 { get; private set; }

    public async ValueTask InitializeAsync()
    {
        _network = new NetworkBuilder()
            .WithName($"signalr-testnet-pgrestart-{Guid.NewGuid():N}")
            .Build();

        await _network.CreateAsync();

        PostgresContainer = new PostgreSqlBuilder("postgres:16")
            .WithUsername("postgres")
            .WithPassword("admin")
            .WithNetwork(_network)
            .WithNetworkAliases("postgres_network")
            .WithCleanUp(true)
            .WithAutoRemove(true)
            .Build();

        await PostgresContainer.StartAsync();

        Database = new DatabaseContainer(PostgresContainer.GetConnectionString());
        await Database.InitializeAsync();

        Server1 = new TestServer(await CreateTestServerAsync());
        Server2 = new TestServer(await CreateTestServerAsync());
    }

    private async Task<TestServerContainer> CreateTestServerAsync()
    {
        var container = new ContainerBuilder(containerFixture.TestServerImage)
            .WithName($"signalr-test-pgrestart-{Guid.NewGuid():N}")
            .WithNetwork(_network)
            .WithEnvironment("ConnectionStrings__Postgres", Database!.ConnectionStringInternal)
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        await container.StartAsync();
        return new TestServerContainer(container);
    }

    public async Task RestartPostgresAsync()
    {
        await PostgresContainer!.StopAsync();
        await PostgresContainer!.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Server1!.DisposeAsync();
        await Server2!.DisposeAsync();
        await Database!.DisposeAsync();
        await PostgresContainer!.DisposeAsync();
        await _network!.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
