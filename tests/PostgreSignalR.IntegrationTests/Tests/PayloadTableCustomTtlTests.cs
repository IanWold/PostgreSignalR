namespace PostgreSignalR.IntegrationTests;

public class PayloadTableCustomTtlTests(ContainerFixture fixture) : PayloadTableTestBase(fixture, new(PayloadTableStorage: PayloadTableStorage.Always, AutomaticCleanupTtlMs: 500, AutomaticCleanupIntervalMs: 250))
{
    [Fact]
    public async Task CustomTtlIsHonoredByAutomaticCleanup()
    {
        var id = await InsertPayloadRowAsync(TimeSpan.FromSeconds(2));
        await AssertRowRemovedWithinAsync(id);
    }
}
