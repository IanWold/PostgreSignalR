using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class BroadcastTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
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

    [RetryFact]
    public async Task CallerOnly_DoesNotReachOthers()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var other = await Server2.CreateClientAsync();

        var messageFromCaller = caller.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToCaller(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromCaller).Arg<string>());
        await other.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Others_ReachAllOtherClients()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var other1 = await Server1.CreateClientAsync();
        await using var other2 = await Server2.CreateClientAsync();

        var messageFromOther1 = other1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromOther2 = other2.ExpectMessageAsync(nameof(IClient.Message));

        await caller.Send.SendToOthers(ShortMessage);

        Assert.Equal(ShortMessage, (await messageFromOther1).Arg<string>());
        Assert.Equal(ShortMessage, (await messageFromOther2).Arg<string>());

        await caller.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Broadcast_AllExceptOneDoesNotReachExcluded()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();
        await using var excluded = await Server2.CreateClientAsync();

        var messageFromClient1 = client1.ExpectMessageAsync(nameof(IClient.Message));
        var messageFromClient2 = client2.ExpectMessageAsync(nameof(IClient.Message));

        var excludedId = await excluded.Send.GetConnectionId();

        await client1.Send.SendToAllExcept(ShortMessage, excludedId);

        Assert.Equal(ShortMessage, (await messageFromClient1).Arg<string>());
        Assert.Equal(ShortMessage, (await messageFromClient2).Arg<string>());

        await excluded.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
