using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class UserTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var c1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var c2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll("hello");

        Assert.Equal("hello", (await c1).Arg<string>());
        Assert.Equal("hello", (await c2).Arg<string>());
    }

    [RetryFact]
    public async Task CallerOnly_DoesNotReachOthers()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var other = await Server2.CreateClientAsync();

        var callerMsg = caller.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToCaller("from-caller");

        Assert.Equal("from-caller", (await callerMsg).Arg<string>());
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

        Assert.Equal("broadcast-others", (await o1).Arg<string>());
        Assert.Equal("broadcast-others", (await o2).Arg<string>());
        await caller.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_SendHitsMembersAcrossServers()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        await member1.Send.JoinGroup("alpha");
        await member2.Send.JoinGroup("alpha");

        var m1 = member1.ExpectMessageAsync(nameof(IClient.Message));
        var m2 = member2.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup("alpha", "group-msg");

        Assert.Equal("group-msg", (await m1).Arg<string>());
        Assert.Equal("group-msg", (await m2).Arg<string>());
        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_RemovalStopsDelivery()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();

        await member1.Send.JoinGroup("beta");
        await member2.Send.JoinGroup("beta");
        await member2.Send.LeaveGroup("beta");

        var m1 = member1.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup("beta", "after-remove");

        Assert.Equal("after-remove", (await m1).Arg<string>());
        await member2.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();
        var recv = target.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(targetId, "direct");

        Assert.Equal("direct", (await recv).Arg<string>());
        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
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

        Assert.Equal("multi", (await r1).Arg<string>());
        Assert.Equal("multi", (await r2).Arg<string>());
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

        Assert.Equal("nope", (await m1).Arg<string>());
        Assert.Equal("nope", (await m2).Arg<string>());
        await excluded.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Users_SendToUserHitsAllConnections()
    {
        await using var user1a = await Server1.CreateClientAsync("u1");
        await using var user1b = await Server2.CreateClientAsync("u1");
        await using var user2 = await Server2.CreateClientAsync("u2");

        var r1 = user1a.ExpectMessageAsync(nameof(IClient.Message));
        var r2 = user1b.ExpectMessageAsync(nameof(IClient.Message));

        await user2.Send.SendToUser("u1", "user-msg");

        Assert.Equal("user-msg", (await r1).Arg<string>());
        Assert.Equal("user-msg", (await r2).Arg<string>());
        await user2.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Users_SendToUsersHitsMultipleUsers()
    {
        await using var user1 = await Server1.CreateClientAsync("u1");
        await using var user2 = await Server2.CreateClientAsync("u2");
        await using var user3 = await Server2.CreateClientAsync("u3");

        var r1 = user1.ExpectMessageAsync(nameof(IClient.Message));
        var r2 = user2.ExpectMessageAsync(nameof(IClient.Message));

        await user3.Send.SendToUsers(["u1", "u2"], "multi-user");

        Assert.Equal("multi-user", (await r1).Arg<string>());
        Assert.Equal("multi-user", (await r2).Arg<string>());
        await user3.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
