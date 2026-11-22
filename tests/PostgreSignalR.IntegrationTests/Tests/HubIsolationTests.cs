using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Hubs on the same backplane don't receive each other's messages. The test hub sends,
/// and the second hub has matching connections on both servers that must not receive anything.
/// </summary>
public abstract class HubIsolationTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ConfigurableBaseTest(fixture, configuration)
{
    [RetryFact]
    public async Task Broadcast_DoesNotReachOtherHub()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var receiver = await Server2.CreateClientAsync();
        await using var otherHubLocal = await Server1.CreateSecondHubClientAsync();
        await using var otherHubRemote = await Server2.CreateSecondHubClientAsync();

        // Start every wait before sending, since a client drops a message nothing is waiting for.
        var message = receiver.ExpectMessageAsync(nameof(IClient.Message));
        var noLocalMessage = otherHubLocal.EnsureNoMessageAsync(nameof(IClient.Message));
        var noRemoteMessage = otherHubRemote.EnsureNoMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
        await noLocalMessage;
        await noRemoteMessage;
    }

    [RetryFact]
    public async Task User_DoesNotReachOtherHub()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var receiver = await Server2.CreateClientAsync("u1");
        await using var otherHubLocal = await Server1.CreateSecondHubClientAsync("u1");
        await using var otherHubRemote = await Server2.CreateSecondHubClientAsync("u1");

        var message = receiver.ExpectMessageAsync(nameof(IClient.Message));
        var noLocalMessage = otherHubLocal.EnsureNoMessageAsync(nameof(IClient.Message));
        var noRemoteMessage = otherHubRemote.EnsureNoMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToUser("u1", ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
        await noLocalMessage;
        await noRemoteMessage;
    }

    [RetryFact]
    public async Task Group_DoesNotReachOtherHub()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var member = await Server2.CreateClientAsync();
        await using var otherHubLocal = await Server1.CreateSecondHubClientAsync();
        await using var otherHubRemote = await Server2.CreateSecondHubClientAsync();

        await member.Send.JoinGroup(GroupName);
        await otherHubLocal.Send.JoinGroup(GroupName);
        await otherHubRemote.Send.JoinGroup(GroupName);

        var message = member.ExpectMessageAsync(nameof(IClient.Message));
        var noLocalMessage = otherHubLocal.EnsureNoMessageAsync(nameof(IClient.Message));
        var noRemoteMessage = otherHubRemote.EnsureNoMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAllInGroup(GroupName, ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
        await noLocalMessage;
        await noRemoteMessage;
    }

    [RetryFact]
    public async Task Connection_DoesNotReachOtherHub()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var otherHubLocal = await Server1.CreateSecondHubClientAsync();
        await using var otherHubRemote = await Server2.CreateSecondHubClientAsync();

        var noLocalMessage = otherHubLocal.EnsureNoMessageAsync(nameof(IClient.Message));
        var noRemoteMessage = otherHubRemote.EnsureNoMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToConnection(otherHubLocal.ConnectionId, ShortMessage);
        await sender.Send.SendToConnection(otherHubRemote.ConnectionId, ShortMessage);

        await noLocalMessage;
        await noRemoteMessage;
    }
}

public sealed class ScopedPerTargetHubIsolationTests(ContainerFixture fixture) : HubIsolationTests(fixture, new(ScopeChannelsByHub: true));

public sealed class SharedHubIsolationTests(ContainerFixture fixture) : HubIsolationTests(fixture, BackplaneTestConfiguration.Shared);
