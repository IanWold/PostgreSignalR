using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class PayloadTableAlwaysStorageTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(PayloadTableStorage: PayloadTableStorage.Always))
{
    [Fact]
    public async Task Broadcast_ShortMessageStillDelivered()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromClient1).Arg<string>());
        Assert.Equal(ShortMessage, (await messageFromClient2).Arg<string>());
    }

    [Fact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();

        var messageFromTarget = target.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(targetId, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromTarget).Arg<string>());

        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
