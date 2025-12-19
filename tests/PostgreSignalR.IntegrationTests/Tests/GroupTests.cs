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

        await member1.Send.JoinGroup(GroupName);
        await member2.Send.JoinGroup(GroupName);

        var messageFromMember1 = member1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromMember2 = member2.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMember1).Arg<string>(0));
        Assert.Equal(ShortMessage, (await messageFromMember2).Arg<string>(0));

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_RemovalStopsDelivery()
    {
        await using var member1 = await Server1.CreateClientAsync();
        await using var member2 = await Server2.CreateClientAsync();

        await member1.Send.JoinGroup(GroupName);
        await member2.Send.JoinGroup(GroupName);
        await member2.Send.LeaveGroup(GroupName);

        var messageFromMember1 = member1.ExpectMessageAsync(nameof(IClient.Message));

        await member1.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMember1).Arg<string>(0));

        await member2.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
