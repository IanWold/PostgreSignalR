using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ListenerConnectionTerminationTests(ContainerFixture fixture) : ResiliencyTestBase(fixture, new(Prefix: "backend-kill"))
{
    [RetryFact]
    public async Task BackplaneRecoversAfterListenerConnectionIsTerminated()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var baseline = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await baseline).Arg<string>());

        var terminated = await TerminateListenerConnectionsAsync();

        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        await AssertEventuallyDeliveredAsync(client1, client2);
    }
}
