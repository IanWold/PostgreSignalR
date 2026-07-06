namespace PostgreSignalR.IntegrationTests;

public class PayloadTableCleanupTests(ContainerFixture fixture) : PayloadTableTestBase(fixture, new(PayloadTableStorage: PayloadTableStorage.Always, AutomaticCleanupIntervalMs: 500))
{
    [Fact]
    public async Task ExpiredPayloadRowIsRemovedByAutomaticCleanup()
    {
        var id = await InsertPayloadRowAsync(TimeSpan.FromHours(1));
        await AssertRowRemovedWithinAsync(id);
    }

    [Fact]
    public async Task NonExpiredPayloadRowSurvivesAutomaticCleanup()
    {
        var id = await InsertPayloadRowAsync(TimeSpan.Zero);

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        await AssertRowStillExistsAsync(id);
    }
}
