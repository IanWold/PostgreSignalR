using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class GroupTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Group_SendHitsMembersAcrossServers()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        var group = Guid.NewGuid().ToString();
        var message = Guid.NewGuid().ToString();

        await member1.Send.JoinGroup(group);
        await member2.Send.JoinGroup(group);

        var m1 = member1.ExpectMessageAsync(nameof(IClient.Message));
        var m2 = member2.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup(group, message);

        Assert.Equal(message, (await m1).Arg<string>(0));
        Assert.Equal(message, (await m2).Arg<string>(0));
        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_RemovalStopsDelivery()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();

        var group = Guid.NewGuid().ToString();
        var message = Guid.NewGuid().ToString();

        await member1.Send.JoinGroup(group);
        await member2.Send.JoinGroup(group);
        await member2.Send.LeaveGroup(group);

        var m1 = member1.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup(group, message);

        Assert.Equal(message, (await m1).Arg<string>(0));
        await member2.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
