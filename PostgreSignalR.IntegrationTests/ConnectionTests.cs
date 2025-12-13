using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ConnectionTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();
        var recv = target.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(targetId, "direct");

        Assert.Equal("direct", (await recv).Arg<string>(0));
        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [Fact]
    public async Task Connections_TargetsMultiple()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target1 = await Server2.CreateClientAsync();
        await using var target2 = await Server1.CreateClientAsync();

        var t1 = await target1.Send.GetConnectionId();
        var t2 = await target2.Send.GetConnectionId();

        var r1 = target1.ExpectMessageAsync(nameof(IClient.Message));
        var r2 = target2.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnections([t1, t2], "multi");

        Assert.Equal("multi", (await r1).Arg<string>(0));
        Assert.Equal("multi", (await r2).Arg<string>(0));
    }

}
