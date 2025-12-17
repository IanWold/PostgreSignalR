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

        await member1.Send.JoinGroup("alpha");
        await member2.Send.JoinGroup("alpha");

        var m1 = member1.ExpectMessageAsync(nameof(IClient.Message));
        var m2 = member2.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup("alpha", "group-msg");

        Assert.Equal("group-msg", (await m1).Arg<string>(0));
        Assert.Equal("group-msg", (await m2).Arg<string>(0));
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

        Assert.Equal("after-remove", (await m1).Arg<string>(0));
        await member2.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
