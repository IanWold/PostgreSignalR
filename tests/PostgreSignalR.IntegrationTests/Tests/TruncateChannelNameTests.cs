using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class TruncateChannelNameTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(ChannelNameNormaization: ChannelNameNormaization.Truncate))
{
    [RetryFact]
    public async Task LongGroupNameStillDeliversMessages()
    {
        var longGroupName = new string('A', 100);

        await using var member = await Server1.CreateClientAsync();
        await using var outsider = await Server2.CreateClientAsync();

        await member.Send.JoinGroup(longGroupName);

        var messageFromMember = member.ExpectMessageAsync(nameof(IClient.Message));

        await member.Send.SendToAllInGroup(longGroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMember).Arg<string>());

        await outsider.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task GroupNamesSharingALongPrefixDoNotCollide()
    {
        var sharedPrefix = new string('A', 100);
        var groupA = sharedPrefix + "1";
        var groupB = sharedPrefix + "2";

        await using var memberA = await Server1.CreateClientAsync();
        await using var memberB = await Server2.CreateClientAsync();

        await memberA.Send.JoinGroup(groupA);
        await memberB.Send.JoinGroup(groupB);

        var messageFromMemberA = memberA.ExpectMessageAsync(nameof(IClient.Message));

        await memberA.Send.SendToAllInGroup(groupA, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMemberA).Arg<string>());

        await memberB.EnsureNoMessageAsync(nameof(IClient.Message));

        var messageFromMemberB = memberB.ExpectMessageAsync(nameof(IClient.Message));

        await memberB.Send.SendToAllInGroup(groupB, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromMemberB).Arg<string>());

        await memberA.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
