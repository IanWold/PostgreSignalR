using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ListenerConnectionTerminationTests(ContainerFixture fixture) : ResiliencyTestBase(fixture, new(Prefix: "backend-kill"))
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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
