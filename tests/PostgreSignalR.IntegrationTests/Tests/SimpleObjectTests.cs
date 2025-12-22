using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests.Tests;

public class SimpleObjectTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));

        await client1.Send.SendToAll_SimpleObject(RandomSimpleObject);

        Assert.Equal(RandomSimpleObject, (await messageFromClient1).Arg<SimpleObject>());
        Assert.Equal(RandomSimpleObject, (await messageFromClient2).Arg<SimpleObject>());
    }

    [RetryFact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();

        var messageFromTarget = target.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));

        await sender.Send.SendToConnection_SimpleObject(targetId, RandomSimpleObject);

        Assert.Equal(RandomSimpleObject, (await messageFromTarget).Arg<SimpleObject>());

        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_SendHitsMembersAcrossServers()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        await member1.Send.JoinGroup(GroupName);
        await member2.Send.JoinGroup(GroupName);

        var messageFromMember1 = member1.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));
        var messageFromMember2 = member2.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));

        await member1.Send.SendToAllInGroup_SimpleObject(GroupName, RandomSimpleObject);

        Assert.Equal(RandomSimpleObject, (await messageFromMember1).Arg<SimpleObject>());
        Assert.Equal(RandomSimpleObject, (await messageFromMember2).Arg<SimpleObject>());

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Users_SendToUsersHitsMultipleUsers()
    {
        await using var user1 = await Server1.CreateClientAsync("u1");
        await using var user2 = await Server2.CreateClientAsync("u2");
        await using var user3 = await Server2.CreateClientAsync("u3");

        var messageFromUser1 = user1.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));
        var messageFromUser2 = user2.ExpectMessageAsync(nameof(IClient.MessageSimpleObject));

        await user3.Send.SendToUsers_SimpleObject(["u1", "u2"], RandomSimpleObject);

        Assert.Equal(RandomSimpleObject, (await messageFromUser1).Arg<SimpleObject>());
        Assert.Equal(RandomSimpleObject, (await messageFromUser2).Arg<SimpleObject>());
        await user3.EnsureNoMessageAsync(nameof(IClient.MessageSimpleObject));
    }
}
