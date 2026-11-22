namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Under shared routing, a server LISTENs on five channels: all, group management, its acks, its client results, and shared.
/// Connecting, disconnecting and changing groups never runs LISTEN or UNLISTEN.
/// </summary>
public sealed class SharedListenerChannelTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, BackplaneTestConfiguration.Shared)
{
    private const int SharedRoutingChannelCount = 5;

    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Listener_StaysOnFiveChannels()
    {
        await using var client1 = await Server1.CreateClientAsync($"user-{Guid.NewGuid()}");
        await using var client2 = await Server1.CreateClientAsync($"user-{Guid.NewGuid()}");
        await using var client3 = await Server1.CreateClientAsync();

        await client1.Send.JoinGroup(GroupName);
        await client2.Send.JoinGroup(Guid.NewGuid().ToString());
        await client3.Send.JoinGroup(Guid.NewGuid().ToString());

        var diagnostics = await Server1.GetListenerDiagnosticsAsync();

        Assert.Equal(SharedRoutingChannelCount, diagnostics.ChannelCount);
    }

    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task ConnectingAndChangingGroups_DoesNotListen()
    {
        // Make sure the backplane has initialized, which is when it LISTENs.
        await using var initialClient = await Server1.CreateClientAsync();

        var before = await Server1.GetListenerDiagnosticsAsync();

        for (var i = 0; i < 5; i++)
        {
            await using var client = await Server1.CreateClientAsync($"user-{Guid.NewGuid()}");

            await client.Send.JoinGroup(GroupName);
            await client.Send.LeaveGroup(GroupName);
        }

        var after = await Server1.GetListenerDiagnosticsAsync();

        Assert.Equal(before.ListenCount, after.ListenCount);
        Assert.Equal(SharedRoutingChannelCount, after.ChannelCount);
    }
}

/// <summary>
/// Under per-target routing, a server LISTENs on a channel per local connection, user and group.
/// This checks the diagnostics the shared routing tests rely on can see that.
/// </summary>
public sealed class PerTargetListenerChannelTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, BackplaneTestConfiguration.Default)
{
    private const int FixedChannelCount = 4;

    [RetryFact]
    public async Task Listener_AddsChannelsForConnectionsUsersAndGroups()
    {
        await using var initialClient = await Server1.CreateClientAsync();

        var before = await Server1.GetListenerDiagnosticsAsync();

        await using var client = await Server1.CreateClientAsync($"user-{Guid.NewGuid()}");
        await client.Send.JoinGroup(GroupName);

        var after = await Server1.GetListenerDiagnosticsAsync();

        // A connection, a user and a group.
        Assert.True(after.ListenCount >= before.ListenCount + 3, $"Expected at least 3 more LISTENs, but went from {before.ListenCount} to {after.ListenCount}.");
        Assert.True(after.ChannelCount >= FixedChannelCount + 4, $"Expected at least {FixedChannelCount + 4} channels, but found {after.ChannelCount}.");
    }
}
