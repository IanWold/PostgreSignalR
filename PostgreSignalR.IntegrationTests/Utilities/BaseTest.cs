
namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture) : IAsyncLifetime
{
    internal DatabaseContainer Database { get; private set; } = default!;

    internal async Task<TestServer> CreateServerAsync() =>
        new(await fixture.CreateTestServerAsync(Database));

    public async ValueTask InitializeAsync() =>
        Database = await fixture.GetDatabaseAsync();

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
