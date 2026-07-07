using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class CrossServerGroupTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task AddConnectionToGroupFromAnotherServerDeliversGroupMessages()
    {
        await using var member = await Server2.CreateClientAsync();
        await using var manager = await Server1.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        var memberConnectionId = await member.Send.GetConnectionId();

        await manager.Send.AddConnectionToGroup(memberConnectionId, GroupName);

        var messageFromMember = member.ExpectMessageAsync(nameof(IClient.Message));

        await manager.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMember).Arg<string>());

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task RemoveConnectionFromGroupFromAnotherServerStopsDelivery()
    {
        await using var member = await Server2.CreateClientAsync();
        await using var manager = await Server1.CreateClientAsync();

        var memberConnectionId = await member.Send.GetConnectionId();

        await member.Send.JoinGroup(GroupName);

        var baseline = member.ExpectMessageAsync(nameof(IClient.Message));

        await manager.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await baseline).Arg<string>());

        await manager.Send.RemoveConnectionFromGroup(memberConnectionId, GroupName);
        await manager.Send.SendToAllInGroup(GroupName, ShortMessage);
        await member.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
