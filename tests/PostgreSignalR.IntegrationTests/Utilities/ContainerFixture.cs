using System.Collections.Concurrent;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using PostgreSignalR.IntegrationTests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ContainerFixture))]

namespace PostgreSignalR.IntegrationTests;

public class ContainerFixture : IAsyncLifetime
{
    public INetwork? Network { get; set; }
    public IFutureDockerImage? TestServerImage { get; set; }
    public PostgreSqlContainer? PostgresContainer { get; set; }

    public DatabaseContainer? SharedDatabse { get; set; }
    public TestServer? SharedServer1 { get; set; }
    public TestServer? SharedServer2 { get; set; }

    // Non-default configurations are provisioned lazily on first request and cached for the remainder of the
    // test run, keyed by value so every test asking for the same configuration reuses the same server pair.
    private readonly ConcurrentDictionary<BackplaneTestConfiguration, Task<ConfiguredServerPair>> _configuredServerPairs = new();

    public async Task<DatabaseContainer> GetDatabaseAsync()
    {
        var database = new DatabaseContainer(PostgresContainer!.GetConnectionString());
        await database.InitializeAsync();
        return database;
    }

    public async Task<TestServerContainer> CreateTestServerAsync(DatabaseContainer database, IReadOnlyDictionary<string, string>? environment = null)
    {
        var containerBuilder = new ContainerBuilder(TestServerImage)
            .WithName($"signalr-test-{Guid.NewGuid():N}")
            .WithNetwork(Network)
            .WithEnvironment("ConnectionStrings__Postgres", database.ConnectionStringInternal)
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

    /// <summary>
    /// Gets a server pair configured per <paramref name="configuration"/>. The default configuration returns
    /// the two servers shared by the whole test run; any other configuration is provisioned on its own
    /// database and pair of app containers (built from the same shared image, just with different
    /// environment variables), created once on first request and reused afterward.
    /// </summary>
    public Task<(TestServer Server1, TestServer Server2)> GetServerPairAsync(BackplaneTestConfiguration configuration) =>
        configuration == BackplaneTestConfiguration.Default
            ? Task.FromResult((SharedServer1!, SharedServer2!))
            : GetOrCreateConfiguredPairAsync(configuration);

    private async Task<(TestServer, TestServer)> GetOrCreateConfiguredPairAsync(BackplaneTestConfiguration configuration)
    {
        var pair = await _configuredServerPairs.GetOrAdd(configuration, CreateConfiguredServerPairAsync);
        return (pair.Server1, pair.Server2);
    }

    /// <summary>
    /// Gets the connection string for the database backing <paramref name="configuration"/>'s server pair,
    /// for tests that need to inspect backplane-managed state (e.g. the payload table) directly.
    /// </summary>
    public async Task<string> GetDatabaseConnectionStringAsync(BackplaneTestConfiguration configuration)
    {
        if (configuration == BackplaneTestConfiguration.Default)
        {
            return SharedDatabse!.ConnectionString;
        }

        var pair = await _configuredServerPairs.GetOrAdd(configuration, CreateConfiguredServerPairAsync);
        return pair.Database.ConnectionString;
    }

    private async Task<ConfiguredServerPair> CreateConfiguredServerPairAsync(BackplaneTestConfiguration configuration)
    {
        var database = await GetDatabaseAsync();
        var environment = configuration.ToEnvironmentVariables();

        var server1 = new TestServer(await CreateTestServerAsync(database, environment));
        var server2 = new TestServer(await CreateTestServerAsync(database, environment));

        return new ConfiguredServerPair(database, server1, server2);
    }

    public async ValueTask InitializeAsync()
    {
        Network = new NetworkBuilder()
            .WithName($"signalr-testnet-{Guid.NewGuid():N}")
            .Build();

        await Network.CreateAsync();

        TestServerImage = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory().DirectoryPath)
#if NET10_0
            .WithDockerfile("Dockerfile.integrationTests.net10")
#elif NET9_0
            .WithDockerfile("Dockerfile.integrationTests.net9")
#elif NET8_0
            .WithDockerfile("Dockerfile.integrationTests.net8")
#endif
            .Build();

        await TestServerImage.CreateAsync();

        PostgresContainer = new PostgreSqlBuilder("postgres:16")
            .WithUsername("postgres")
            .WithPassword("admin")
            .WithNetwork(Network)
            .WithNetworkAliases("postgres_network")
            .WithCleanUp(true)
            .WithAutoRemove(true)
            .Build();

        await PostgresContainer.StartAsync();

        SharedDatabse = await GetDatabaseAsync();
        SharedServer1 = new(await CreateTestServerAsync(SharedDatabse));
        SharedServer2 = new(await CreateTestServerAsync(SharedDatabse));
    }

    public async ValueTask DisposeAsync()
    {
        // Stop app servers before dropping the databases they're connected to, rather than the reverse.
        await SharedServer1!.DisposeAsync();
        await SharedServer2!.DisposeAsync();
        await SharedDatabse!.DisposeAsync();

        foreach (var pairTask in _configuredServerPairs.Values)
        {
            var pair = await pairTask;
            await pair.Server1.DisposeAsync();
            await pair.Server2.DisposeAsync();
            await pair.Database.DisposeAsync();
        }

        await Network!.DisposeAsync();
        await TestServerImage!.DisposeAsync();
        await PostgresContainer!.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    private sealed record ConfiguredServerPair(DatabaseContainer Database, TestServer Server1, TestServer Server2);
}
