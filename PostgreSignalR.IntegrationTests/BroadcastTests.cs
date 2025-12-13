using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class BroadcastTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var c1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var c2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll("hello");

        Assert.Equal("hello", (await c1).Arg<string>(0));
        Assert.Equal("hello", (await c2).Arg<string>(0));
    }

    [RetryFact]
    public async Task CallerOnly_DoesNotReachOthers()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var other = await Server2.CreateClientAsync();

        var callerMsg = caller.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToCaller("from-caller");

        Assert.Equal("from-caller", (await callerMsg).Arg<string>(0));
        await other.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Others_ReachAllOtherClients()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var other1 = await Server1.CreateClientAsync();
        await using var other2 = await Server2.CreateClientAsync();

        var o1 = other1.ExpectMessageAsync(nameof(IClient.Message));
        var o2 = other2.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToOthers("broadcast-others");

        Assert.Equal("broadcast-others", (await o1).Arg<string>(0));
        Assert.Equal("broadcast-others", (await o2).Arg<string>(0));
        await caller.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Broadcast_AllExceptOneDoesNotReachExcluded()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();
        await using var excluded = await Server2.CreateClientAsync();

        var m1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var m2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        var excludedId = await excluded.Send.GetConnectionId();

        await client1.Send.SendToAllExcept("nope", excludedId);

        Assert.Equal("nope", (await m1).Arg<string>(0));
        Assert.Equal("nope", (await m2).Arg<string>(0));
        await excluded.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
