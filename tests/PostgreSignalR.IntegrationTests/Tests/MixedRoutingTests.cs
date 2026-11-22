using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// A per-target server and a shared server on the same database and prefix, as during a rollout that changes routing.
/// They use different channels, so they don't exchange messages, but each keeps serving its own connections.
/// </summary>
public sealed class MixedRoutingTests(ContainerFixture fixture) : TestData
{
    private TestServer _perTargetServer = null!;
    private TestServer _sharedServer = null!;

    public override async ValueTask InitializeAsync() =>
        (_perTargetServer, _sharedServer) = await fixture.GetServerPairAsync(BackplaneTestConfiguration.Default, BackplaneTestConfiguration.Shared);

    [RetryFact]
    public async Task Broadcast_DoesNotCrossRoutingModes()
    {
        await using var perTargetClient = await _perTargetServer.CreateClientAsync();
        await using var sharedClient = await _sharedServer.CreateClientAsync();

        var messageToPerTarget = perTargetClient.ExpectMessageAsync(nameof(IClient.Message));
        await perTargetClient.Send.SendToAll(ShortMessage);
        Assert.Equal(ShortMessage, (await messageToPerTarget).Arg<string>());
        await sharedClient.EnsureNoMessageAsync(nameof(IClient.Message));

        var messageToShared = sharedClient.ExpectMessageAsync(nameof(IClient.Message));
        await sharedClient.Send.SendToAll(ShortMessage);
        Assert.Equal(ShortMessage, (await messageToShared).Arg<string>());
        await perTargetClient.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Group_DoesNotCrossRoutingModes()
    {
        await using var perTargetMember = await _perTargetServer.CreateClientAsync();
        await using var sharedMember = await _sharedServer.CreateClientAsync();

        await perTargetMember.Send.JoinGroup(GroupName);
        await sharedMember.Send.JoinGroup(GroupName);

        var messageToPerTarget = perTargetMember.ExpectMessageAsync(nameof(IClient.Message));
        await perTargetMember.Send.SendToAllInGroup(GroupName, ShortMessage);
        Assert.Equal(ShortMessage, (await messageToPerTarget).Arg<string>());
        await sharedMember.EnsureNoMessageAsync(nameof(IClient.Message));

        var messageToShared = sharedMember.ExpectMessageAsync(nameof(IClient.Message));
        await sharedMember.Send.SendToAllInGroup(GroupName, ShortMessage);
        Assert.Equal(ShortMessage, (await messageToShared).Arg<string>());
        await perTargetMember.EnsureNoMessageAsync(nameof(IClient.Message));
    }

    [RetryFact]
    public async Task Connection_DoesNotCrossRoutingModes()
    {
        await using var perTargetClient = await _perTargetServer.CreateClientAsync();
        await using var sharedClient = await _sharedServer.CreateClientAsync();

        await perTargetClient.Send.SendToConnection(sharedClient.ConnectionId, ShortMessage);
        await sharedClient.EnsureNoMessageAsync(nameof(IClient.Message));

        await sharedClient.Send.SendToConnection(perTargetClient.ConnectionId, ShortMessage);
        await perTargetClient.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
