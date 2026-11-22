using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public abstract class ListenerConnectionTerminationTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ResiliencyTestBase(fixture, configuration)
{
    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task BackplaneRecoversAfterListenerConnectionIsTerminated()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var baseline = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await baseline).Arg<string>());

        var terminated = await TerminateListenerConnectionsAsync();
        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        await AssertEventuallyDeliveredAsync(client1, client2);
    }

    [RetryFact]
    public async Task OnReconnectedIsRaisedOnceListenerIsListeningAgain()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        var server1Before = await Server1.GetListenerDiagnosticsAsync();
        var server2Before = await Server2.GetListenerDiagnosticsAsync();

        var terminated = await TerminateListenerConnectionsAsync();
        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        await AssertEventuallySucceedsAsync(async () =>
        {
            var server1After = await Server1.GetListenerDiagnosticsAsync();
            var server2After = await Server2.GetListenerDiagnosticsAsync();

            Assert.True(server1After.ReconnectedCount > server1Before.ReconnectedCount, "Expected server 1 to raise OnReconnected.");
            Assert.True(server2After.ReconnectedCount > server2Before.ReconnectedCount, "Expected server 2 to raise OnReconnected.");
        });

        // OnReconnected is raised once the listener is LISTENing again, so a message sent now is delivered without retrying.
        var message = client2.ExpectMessageAsync(nameof(IClient.Message));

        await client1.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
    }

    [RetryFact]
    public async Task BackplaneRecoversFromRepeatedDisruptions()
    {
        await using var client1 = await Server1.CreateClientAsync();
        await using var client2 = await Server2.CreateClientAsync();

        for (var i = 0; i < 2; i++)
        {
            var terminated = await TerminateListenerConnectionsAsync();
            Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

            await AssertEventuallyDeliveredAsync(client1, client2);
        }
    }

    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task GroupSubscriptionSurvivesReconnect()
    {
        await using var member = await Server1.CreateClientAsync();
        await using var sender = await Server2.CreateClientAsync();

        await member.Send.JoinGroup(GroupName);

        await AssertEventuallyDeliveredAsync(() => sender.Send.SendToAllInGroup(GroupName, Guid.NewGuid().ToString()), member, nameof(IClient.Message));

        var terminated = await TerminateListenerConnectionsAsync();
        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        await AssertEventuallyDeliveredAsync(() => sender.Send.SendToAllInGroup(GroupName, Guid.NewGuid().ToString()), member, nameof(IClient.Message));
    }

    [RetryFact]
    public async Task InvokeRecoversAfterReconnect()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server2.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();

        var baseline = await caller.Send.InvokeConnectionEcho(calleeId, ShortMessage);
        Assert.Equal($"echo:{ShortMessage}", baseline);

        var terminated = await TerminateListenerConnectionsAsync();
        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        string? result = null;

        await AssertEventuallySucceedsAsync(async () =>
        {
            result = await caller.Send.InvokeConnectionEcho(calleeId, ShortMessage);
        });

        Assert.Equal($"echo:{ShortMessage}", result);
    }

    [RetryFact]
    public async Task NewConnectionEstablishedDuringDisruptionEventuallyWorks()
    {
        await using var existingClient = await Server1.CreateClientAsync();

        await using var warmup = await Server2.CreateClientAsync();

        var terminated = await TerminateListenerConnectionsAsync();
        Assert.True(terminated >= 2, $"Expected to terminate at least 2 listener connections (one per server), but terminated {terminated}.");

        await using var newClient = await Server2.CreateClientAsync();

        await AssertEventuallyDeliveredAsync(existingClient, newClient);
    }
}

public sealed class PerTargetListenerConnectionTerminationTests(ContainerFixture fixture) : ListenerConnectionTerminationTests(fixture, new(Prefix: "backend-kill"));

public sealed class SharedListenerConnectionTerminationTests(ContainerFixture fixture) : ListenerConnectionTerminationTests(fixture, new(Prefix: "backend-kill", ChannelRouting: ChannelRouting.Shared));
