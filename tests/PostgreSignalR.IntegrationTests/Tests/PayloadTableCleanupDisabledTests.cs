namespace PostgreSignalR.IntegrationTests;

public class PayloadTableCleanupDisabledTests(ContainerFixture fixture) : PayloadTableTestBase(fixture, new(PayloadTableStorage: PayloadTableStorage.Always, AutomaticCleanup: false))
{
    [RetryFact]
    public async Task ExpiredPayloadRowSurvivesWhenCleanupDisabled()
    {
        var id = await InsertPayloadRowAsync(TimeSpan.FromHours(1));

        await Task.Delay(TimeSpan.FromSeconds(2));

        await AssertRowStillExistsAsync(id);
    }
}
