using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class LargePayloadTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task LargePayload_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(LongMessage);

        Assert.Equal(LongMessage, (await messageFromClient1).Arg<string>(0));
        Assert.Equal(LongMessage, (await messageFromClient2).Arg<string>(0));
    }
}
