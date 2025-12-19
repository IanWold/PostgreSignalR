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

        Assert.Equal(ShortMessage, (await messageFromMember1).Arg<string>());
        Assert.Equal(ShortMessage, (await messageFromMember2).Arg<string>());

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

        Assert.Equal(ShortMessage, (await messageFromMember1).Arg<string>());

        await member2.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Groups_SendToAllInGroupsHitsMembersOfEachGroup()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var group1Member = await Server2.CreateClientAsync();
        await using var group2Member = await Server1.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        var group1 = GroupName;
        var group2 = Guid.NewGuid().ToString();

        await sender.Send.JoinGroup(group1);
        await group1Member.Send.JoinGroup(group1);
        await group2Member.Send.JoinGroup(group2);

        var messageToSender = sender.ExpectMessageAsync(nameof(IClient.Message));
        var messageToGroup1Member = group1Member.ExpectMessageAsync(nameof(IClient.Message));
        var messageToGroup2Member = group2Member.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAllInGroups([group1, group2], ShortMessage);

        Assert.Equal(ShortMessage, (await messageToSender).Arg<string>());
        Assert.Equal(ShortMessage, (await messageToGroup1Member).Arg<string>());
        Assert.Equal(ShortMessage, (await messageToGroup2Member).Arg<string>());

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Groups_OthersInGroupExcludesCaller()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var member = await Server2.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        await caller.Send.JoinGroup(GroupName);
        await member.Send.JoinGroup(GroupName);

        var messageFromMember = member.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToOthersInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMember).Arg<string>());

        await caller.EnsureNoMessageAsync(nameof(IClient.Message));
        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Groups_GroupExceptExcludesSpecifiedConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var member = await Server2.CreateClientAsync();
        await using var excluded = await Server2.CreateClientAsync();

        await sender.Send.JoinGroup(GroupName);
        await member.Send.JoinGroup(GroupName);
        await excluded.Send.JoinGroup(GroupName);

        var excludedId = await excluded.Send.GetConnectionId();

        var messageToSender = sender.ExpectMessageAsync(nameof(IClient.Message));
        var messageToMember = member.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToGroupExcept(GroupName, excludedId, ShortMessage);

        Assert.Equal(ShortMessage, (await messageToSender).Arg<string>());
        Assert.Equal(ShortMessage, (await messageToMember).Arg<string>());

        await excluded.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
