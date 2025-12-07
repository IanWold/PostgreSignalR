using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Images;
using DotNet.Testcontainers.Networks;
using Testcontainers.PostgreSql;

namespace PostgreSignalR.IntegrationTests;

public class ContainerFixture : IAsyncLifetime
{
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
                .WithDockerfile("Dockerfile.integrationTests")
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
    
    private Containers? TestContainers { get; set; }

    public async Task<DatabaseContainer> GetDatabaseAsync()
    {
        TestContainers ??= await Containers.CreateAsync();

        var database = new DatabaseContainer(TestContainers.PostgresContainer.GetConnectionString());
        await database.InitializeAsync();
        return database;
    }

    public async Task<TestServerContainer> CreateTestServerAsync(DatabaseContainer database)
    {
        TestContainers ??= await Containers.CreateAsync();

        var container = new ContainerBuilder()
            .WithImage(TestContainers.TestServerImage)
            .WithName($"signalr-test-{Guid.NewGuid():N}")
            .WithNetwork(TestContainers!.Network)
            .WithEnvironment("ConnectionStrings__Postgres", database.ConnectionStringInternal)
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        await container.StartAsync();
        return new TestServerContainer(container!);
    }

    public async Task InitializeAsync() { }

    public async Task DisposeAsync()
    {
        await TestContainers!.DisposeAsync();
    }
}
