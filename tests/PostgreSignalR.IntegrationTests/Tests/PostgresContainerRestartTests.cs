using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class PostgresContainerRestartTests(PostgresRestartFixture fixture) : PostgresRestartTestBase(fixture)
{
    [Fact]
    public async Task BackplaneRecoversAfterPostgresRestart()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var baseline = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await baseline).Arg<string>());

        await RestartPostgresAsync();
        await RetryAssertions.AssertEventuallyDeliveredAsync(client1, client2);
    }

    [Fact]
    public async Task BackplaneRecoversWhenFirstConnectionOccursDuringOutage()
    {
        await StopPostgresAsync();

        await using var receiver = await CreateAdditionalServerAsync(new Dictionary<string, string>
        {
            ["UseTableStrategy"] = "false"
        });

        await using var client2 = await receiver.CreateClientAsync(waitForHealthy: false);

        await StartPostgresAsync();

        await using var client1 = await Server1.CreateClientAsync();
        await using var client2b = await receiver.CreateClientAsync();

        await RetryAssertions.AssertEventuallyDeliveredAsync(client1, client2);
        await RetryAssertions.AssertEventuallyDeliveredAsync(client1, client2b);
    }
}
