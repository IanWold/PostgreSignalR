using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class LargePayloadTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task LargePayload_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var c1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var c2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        var largePayload = new string('A', 10000);

        await client1.Send.SendToAll(largePayload);

        Assert.Equal(largePayload, (await c1).Arg<string>(0));
        Assert.Equal(largePayload, (await c2).Arg<string>(0));
    }
}
