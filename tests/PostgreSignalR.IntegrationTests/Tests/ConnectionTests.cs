using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ConnectionTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Connection_TargetsSingleConnection()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target = await Server2.CreateClientAsync();
        await using var bystander = await Server2.CreateClientAsync();

        var targetId = await target.Send.GetConnectionId();

        var messageFromTarget = target.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(targetId, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromTarget).Arg<string>(0));

        await bystander.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Connections_TargetsMultiple()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var target1 = await Server2.CreateClientAsync();
        await using var target2 = await Server1.CreateClientAsync();

        var target1Id = await target1.Send.GetConnectionId();
        var target2Id = await target2.Send.GetConnectionId();

        var messageFromTarget1 = target1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromTarget2 = target2.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnections([target1Id, target2Id], ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromTarget1).Arg<string>(0));
        Assert.Equal(ShortMessage, (await messageFromTarget2).Arg<string>(0));
    }
}
