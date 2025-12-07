namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture) : IClassFixture<ContainerFixture>, IAsyncLifetime
{
    internal DatabaseContainer Database { get; private set; } = default!;

    internal async Task<TestServer> CreateServerAsync() =>
        new(await fixture.CreateTestServerAsync(Database));

    public async Task InitializeAsync() =>
        Database = await fixture.GetDatabaseAsync();

    public async Task DisposeAsync() =>
        await Database.DisposeAsync();
}
