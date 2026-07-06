using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class EventPayloadStrategyOnlyTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(UseTableStrategy: false))
{
    [Fact]
    public async Task Broadcast_AllServersReceive()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromClient1).Arg<string>());
        Assert.Equal(ShortMessage, (await messageFromClient2).Arg<string>());
    }

    [Fact]
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
}
