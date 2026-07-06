using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ServerRestartTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(Prefix: "restart"))
{
    [Fact]
    public async Task ServerRecoversAfterRestart()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromClient2).Arg<string>());

        await Server1.RestartAsync();

        await using var restartedClient = await Server1.CreateClientAsync();
        await using var otherServerClient = await Server2.CreateClientAsync();

        var messageFromOtherServer = otherServerClient.ExpectMessageAsync(nameof(IClient.Message));

        await restartedClient.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromOtherServer).Arg<string>());

        var newGroupName = Guid.NewGuid().ToString();

        await restartedClient.Send.JoinGroup(newGroupName);

        var messageFromRestarted = restartedClient.ExpectMessageAsync(nameof(IClient.Message));

        await otherServerClient.Send.SendToAllInGroup(newGroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromRestarted).Arg<string>());
    }
}
