using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class LargePayloadTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task LargePayload_AllServersReceive()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var client1 = await server1.CreateClientAsync();
        await using var client2 = await server2.CreateClientAsync();

        var c1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var c2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        var largePayload = GenerateLargeString();

        await client1.Send.SendToAll(largePayload);

        Assert.Equal(largePayload, (await c1).Arg<string>(0));
        Assert.Equal(largePayload, (await c2).Arg<string>(0));
    }
}
