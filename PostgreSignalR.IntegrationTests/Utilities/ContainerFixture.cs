using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using PostgreSignalR.IntegrationTests;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(ContainerFixture))]

namespace PostgreSignalR.IntegrationTests;

public class ContainerFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<Containers>? _containersTask;
    private bool _disposed;

    private record Containers(INetwork Network, IFutureDockerImage TestServerImage, PostgreSqlContainer PostgresContainer) : IAsyncDisposable
    {
        public static async Task<Containers> CreateAsync()
        {
            var network = new NetworkBuilder()
                .WithName($"signalr-testnet-{Guid.NewGuid():N}")
                .Build();

            await network.CreateAsync();

            var testServerImage = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory().DirectoryPath)
#if NET10_0
                .WithDockerfile("Dockerfile.integrationTests.net10")
#elif NET9_0
                .WithDockerfile("Dockerfile.integrationTests.net9")
#elif NET8_0
                .WithDockerfile("Dockerfile.integrationTests.net8")
#endif
                .Build();

            await testServerImage.CreateAsync();

            var postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:16")
                .WithUsername("postgres")
                .WithPassword("admin")
                .WithNetwork(network)
                .WithNetworkAliases("postgres_network")
                .WithCleanUp(true)
                .WithAutoRemove(true)
                .Build();

            await postgresContainer.StartAsync();

            return new(network, testServerImage, postgresContainer);
        }

        public async ValueTask DisposeAsync()
        {
            await Network.DisposeAsync();
            await TestServerImage.DisposeAsync();
            await PostgresContainer.DisposeAsync();
        }
    }

    private async Task<Containers> EnsureContainersAsync()
    {
        if (_containersTask is not null)
        {
            return await _containersTask;
        }

        await _gate.WaitAsync();
        try
        {
            _containersTask ??= Containers.CreateAsync();
        }
        finally
        {
            _gate.Release();
        }

        return await _containersTask;
    }

    public async Task<DatabaseContainer> GetDatabaseAsync()
    {
        var containers = await EnsureContainersAsync();

        var database = new DatabaseContainer(containers.PostgresContainer.GetConnectionString());
        await database.InitializeAsync();
        return database;
    }

    public async Task<TestServerContainer> CreateTestServerAsync(DatabaseContainer database)
    {
        var containers = await EnsureContainersAsync();

        var container = new ContainerBuilder()
            .WithImage(containers.TestServerImage)
            .WithName($"signalr-test-{Guid.NewGuid():N}")
            .WithNetwork(containers.Network)
            .WithEnvironment("ConnectionStrings__Postgres", database.ConnectionStringInternal)
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        await container.StartAsync();
        return new TestServerContainer(container!);
    }

    public async ValueTask InitializeAsync() { }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_containersTask is not null)
        {
            var containers = await _containersTask;
            await containers.DisposeAsync();
        }

        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
