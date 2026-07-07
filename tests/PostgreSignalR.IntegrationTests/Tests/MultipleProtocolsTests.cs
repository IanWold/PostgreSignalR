using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class MultipleProtocolsTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Broadcast_ReachesBothJsonAndMessagePackClients()
    {
        await using var jsonClient = await Server1.CreateClientAsync();
        await using var messagePackClient = await Server2.CreateClientAsync(useMessagePack: true);

        var messageToJson = jsonClient.ExpectMessageAsync(nameof(IClient.Message));
        var messageToMessagePack = messagePackClient.ExpectMessageAsync(nameof(IClient.Message));

        await jsonClient.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await messageToJson).Arg<string>());
        Assert.Equal(ShortMessage, (await messageToMessagePack).Arg<string>());
    }

    [RetryFact]
    public async Task Group_ReachesBothJsonAndMessagePackMembers()
    {
        await using var jsonMember = await Server1.CreateClientAsync();
        await using var messagePackMember = await Server2.CreateClientAsync(useMessagePack: true);
        await using var outsider = await Server2.CreateClientAsync();

        await jsonMember.Send.JoinGroup(GroupName);
        await messagePackMember.Send.JoinGroup(GroupName);

        var messageToJson = jsonMember.ExpectMessageAsync(nameof(IClient.Message));
        var messageToMessagePack = messagePackMember.ExpectMessageAsync(nameof(IClient.Message));

        await jsonMember.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageToJson).Arg<string>());
        Assert.Equal(ShortMessage, (await messageToMessagePack).Arg<string>());

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
