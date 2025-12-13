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

    public async Task<DatabaseContainer> GetDatabaseAsync()
    {
        var database = new DatabaseContainer(PostgresContainer!.GetConnectionString());
        await database.InitializeAsync();
        return database;
    }

    public async Task<TestServerContainer> CreateTestServerAsync(DatabaseContainer database)
    {
        var container = new ContainerBuilder()
            .WithImage(TestServerImage)
            .WithName($"signalr-test-{Guid.NewGuid():N}")
            .WithNetwork(Network)
            .WithEnvironment("ConnectionStrings__Postgres", database.ConnectionStringInternal)
            .WithPortBinding(8080, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(8080))
            .Build();

        await container.StartAsync();
        return new TestServerContainer(container);
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

        PostgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16")
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
        await SharedDatabse!.DisposeAsync();
        await SharedServer1!.DisposeAsync();
        await SharedServer2!.DisposeAsync();

        await Network!.DisposeAsync();
        await TestServerImage!.DisposeAsync();
        await PostgresContainer!.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}
