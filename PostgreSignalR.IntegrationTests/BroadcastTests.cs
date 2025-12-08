using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class BroadcastTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var client1 = await server1.CreateClientAsync();
        await using var client2 = await server2.CreateClientAsync();

        var c1 = client1.ExpectMessageAsync(nameof(IClient.Receive));
        var c2 = client2.ExpectMessageAsync(nameof(IClient.Receive));

        await client1.Send.SendToAll("hello");

        Assert.Equal("hello", (await c1).Arg<string>(0));
        Assert.Equal("hello", (await c2).Arg<string>(0));
    }

    [Fact]
    public async Task CallerOnly_DoesNotReachOthers()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var caller = await server1.CreateClientAsync();
        await using var other = await server2.CreateClientAsync();

        var callerMsg = caller.ExpectMessageAsync(nameof(IClient.ReceiveCaller));

        await caller.Send.SendToCaller("from-caller");

        Assert.Equal("from-caller", (await callerMsg).Arg<string>(0));
        await other.EnsureNoMessageAsync(nameof(IClient.ReceiveCaller));
    }

    [Fact]
    public async Task Others_ReachAllOtherClients()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var caller = await server1.CreateClientAsync();
        await using var other1 = await server1.CreateClientAsync();
        await using var other2 = await server2.CreateClientAsync();

        var o1 = other1.ExpectMessageAsync(nameof(IClient.ReceiveOthers));
        var o2 = other2.ExpectMessageAsync(nameof(IClient.ReceiveOthers));

        await caller.Send.SendToOthers("broadcast-others");

        Assert.Equal("broadcast-others", (await o1).Arg<string>(0));
        Assert.Equal("broadcast-others", (await o2).Arg<string>(0));
        await caller.EnsureNoMessageAsync(nameof(IClient.ReceiveOthers));
    }

    [Fact]
    public async Task Broadcast_AllExceptOneDoesNotReachExcluded()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var client1 = await server1.CreateClientAsync();
        await using var client2 = await server2.CreateClientAsync();
        await using var excluded = await server2.CreateClientAsync();

        var m1 = client1.ExpectMessageAsync(nameof(IClient.Receive));
        var m2 = client2.ExpectMessageAsync(nameof(IClient.Receive));

        var excludedId = await excluded.Send.GetConnectionId();

        await client1.Send.SendToAllExcept("nope", excludedId);

        Assert.Equal("nope", (await m1).Arg<string>(0));
        Assert.Equal("nope", (await m2).Arg<string>(0));
        await excluded.EnsureNoMessageAsync(nameof(IClient.Receive));
    }
}
