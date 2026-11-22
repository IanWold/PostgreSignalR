using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// Shared routing with payload strategies defined outside the library. A strategy that implements <see cref="IPrefixedPayloadStrategy"/>
/// gets the routing header in front of its text; for one that doesn't, the header travels inside the message instead.
/// These run existing suites with both kinds.
/// </summary>
internal static class CustomPayloadStrategyConfigurations
{
    public static BackplaneTestConfiguration SharedPlain { get; } = new(CustomPayloadStrategy: CustomPayloadStrategy.Plain, ChannelRouting: ChannelRouting.Shared);

    public static BackplaneTestConfiguration SharedPrefixed { get; } = new(CustomPayloadStrategy: CustomPayloadStrategy.Prefixed, ChannelRouting: ChannelRouting.Shared);
}

public sealed class SharedCustomPayloadStrategyStringPayloadTests(ContainerFixture fixture) : StringPayloadTests(fixture, CustomPayloadStrategyConfigurations.SharedPlain);

public sealed class SharedCustomPayloadStrategyGroupTests(ContainerFixture fixture) : GroupTests(fixture, CustomPayloadStrategyConfigurations.SharedPlain);

public sealed class SharedCustomPayloadStrategyClientReturnTests(ContainerFixture fixture) : ClientReturnTests(fixture, CustomPayloadStrategyConfigurations.SharedPlain);

public sealed class SharedCustomPrefixedPayloadStrategyStringPayloadTests(ContainerFixture fixture) : StringPayloadTests(fixture, CustomPayloadStrategyConfigurations.SharedPrefixed);

public sealed class SharedCustomPrefixedPayloadStrategyClientReturnTests(ContainerFixture fixture) : ClientReturnTests(fixture, CustomPayloadStrategyConfigurations.SharedPrefixed);

/// <summary>
/// With an <see cref="IPrefixedPayloadStrategy"/>, a server resolves a connection, user or group payload only when it holds one of the targets.
/// </summary>
public sealed class SharedCustomPrefixedPayloadStrategyResolutionTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, CustomPayloadStrategyConfigurations.SharedPrefixed)
{
    [RetryFact]
    public async Task ServersResolveOnlyPayloadsForTheirOwnTargets()
    {
        var server1Group = Guid.NewGuid().ToString();
        var server2Group = Guid.NewGuid().ToString();

        await using var server1Client = await Server1.CreateClientAsync();
        await using var server2Client = await Server2.CreateClientAsync();

        await server1Client.Send.JoinGroup(server1Group);
        await server2Client.Send.JoinGroup(server2Group);

        var server1Before = (await Server1.GetPayloadDiagnosticsAsync()).PrefixedResolveCount;
        var server2Before = (await Server2.GetPayloadDiagnosticsAsync()).PrefixedResolveCount;

        // Every server receives each of these. Each server receives them in the order they were sent, so once a client
        // receives a later message, its server has already handled the earlier ones.
        await SendAndExpectAsync(server1Client, server2Client, s => s.SendToConnection(server2Client.ConnectionId, ShortMessage));
        await SendAndExpectAsync(server1Client, server2Client, s => s.SendToAllInGroup(server2Group, ShortMessage));
        await SendAndExpectAsync(server1Client, server1Client, s => s.SendToAllInGroup(server1Group, ShortMessage));
        await SendAndExpectAsync(server1Client, server2Client, s => s.SendToAllInGroup(server2Group, ShortMessage));

        var server1Resolved = (await Server1.GetPayloadDiagnosticsAsync()).PrefixedResolveCount - server1Before;
        var server2Resolved = (await Server2.GetPayloadDiagnosticsAsync()).PrefixedResolveCount - server2Before;

        Assert.Equal(1, server1Resolved);
        Assert.Equal(3, server2Resolved);
    }

    private static async Task SendAndExpectAsync(TestClient sender, TestClient receiver, Func<IServer, Task> send)
    {
        var message = receiver.ExpectMessageAsync(nameof(IClient.Message));
        await send(sender.Send);
        await message;
    }
}
