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
    private readonly ConcurrentDictionary<BackplaneTestConfiguration, Task<ConfiguredServerPair>> _configuredServerPairs = new();
    private readonly ConcurrentDictionary<BackplaneTestConfiguration, Task<ConfiguredSingleServer>> _configuredSingleServers = new();

    public INetwork? Network { get; set; }
    public IFutureDockerImage? TestServerImage { get; set; }
    public PostgreSqlContainer? PostgresContainer { get; set; }

    public DatabaseContainer? SharedDatabse { get; set; }
    public TestServer? SharedServer1 { get; set; }
    public TestServer? SharedServer2 { get; set; }

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

    public Task<(TestServer Server1, TestServer Server2)> GetServerPairAsync(BackplaneTestConfiguration configuration) =>
        configuration == BackplaneTestConfiguration.Default
            ? Task.FromResult((SharedServer1!, SharedServer2!))
            : GetOrCreateConfiguredPairAsync(configuration);

    private async Task<(TestServer, TestServer)> GetOrCreateConfiguredPairAsync(BackplaneTestConfiguration configuration)
    {
        var pair = await _configuredServerPairs.GetOrAdd(configuration, CreateConfiguredServerPairAsync);
        return (pair.Server1, pair.Server2);
    }

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

    public async Task<(TestServer Server, string DatabaseConnectionString)> GetSingleServerAsync(BackplaneTestConfiguration configuration)
    {
        var single = await _configuredSingleServers.GetOrAdd(configuration, CreateConfiguredSingleServerAsync);
        return (single.Server, single.Database.ConnectionString);
    }

    private async Task<ConfiguredSingleServer> CreateConfiguredSingleServerAsync(BackplaneTestConfiguration configuration)
    {
        var database = await GetDatabaseAsync();
        var environment = configuration.ToEnvironmentVariables();

        var server = new TestServer(await CreateTestServerAsync(database, environment));

        return new ConfiguredSingleServer(database, server);
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

        foreach (var singleTask in _configuredSingleServers.Values)
        {
            var single = await singleTask;

            await single.Server.DisposeAsync();
            await single.Database.DisposeAsync();
        }

        await Network!.DisposeAsync();
        await TestServerImage!.DisposeAsync();
        await PostgresContainer!.DisposeAsync();

        GC.SuppressFinalize(this);
    }

    private sealed record ConfiguredServerPair(DatabaseContainer Database, TestServer Server1, TestServer Server2);
    private sealed record ConfiguredSingleServer(DatabaseContainer Database, TestServer Server);
}
