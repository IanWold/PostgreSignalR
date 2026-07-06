namespace PostgreSignalR.IntegrationTests;

public abstract class PayloadTestBase<TPayload>(ContainerFixture fixture) : BaseTest(fixture)
{
    protected abstract TPayload Payload { get; }
    protected abstract string MessageKey { get; }

    protected abstract Task SendToAllAsync(TestClient sender, TPayload payload);
    protected abstract Task SendToConnectionAsync(TestClient sender, string connectionId, TPayload payload);
    protected abstract Task SendToGroupAsync(TestClient sender, string groupName, TPayload payload);
    protected abstract Task SendToUsersAsync(TestClient sender, string[] userIds, TPayload payload);

    [Fact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(MessageKey);
        var messageFromClient2 = client2.ExpectMessageAsync(MessageKey);

        await SendToAllAsync(client1, Payload);

        Assert.Equal(Payload, (await messageFromClient1).Arg<TPayload>());
        Assert.Equal(Payload, (await messageFromClient2).Arg<TPayload>());
    }

    [Fact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();

        var messageFromTarget = target.ExpectMessageAsync(MessageKey);

        await SendToConnectionAsync(sender, targetId, Payload);

        Assert.Equal(Payload, (await messageFromTarget).Arg<TPayload>());

        await bystander.EnsureNoMessageAsync(MessageKey);
    }

    [Fact]
    public async Task Group_SendHitsMembersAcrossServers()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        await member1.Send.JoinGroup(GroupName);
        await member2.Send.JoinGroup(GroupName);

        var messageFromMember1 = member1.ExpectMessageAsync(MessageKey);
        var messageFromMember2 = member2.ExpectMessageAsync(MessageKey);

        await SendToGroupAsync(member1, GroupName, Payload);

        Assert.Equal(Payload, (await messageFromMember1).Arg<TPayload>());
        Assert.Equal(Payload, (await messageFromMember2).Arg<TPayload>());

        await outsider.EnsureNoMessageAsync(MessageKey);
    }

    [Fact]
    public async Task Users_SendToUsersHitsMultipleUsers()
    {
        await using var user1 = await Server1.CreateClientAsync("u1");
        await using var user2 = await Server2.CreateClientAsync("u2");
        await using var user3 = await Server2.CreateClientAsync("u3");

        var messageFromUser1 = user1.ExpectMessageAsync(MessageKey);
        var messageFromUser2 = user2.ExpectMessageAsync(MessageKey);

        await SendToUsersAsync(user3, ["u1", "u2"], Payload);

        Assert.Equal(Payload, (await messageFromUser1).Arg<TPayload>());
        Assert.Equal(Payload, (await messageFromUser2).Arg<TPayload>());
        await user3.EnsureNoMessageAsync(MessageKey);
    }
}
