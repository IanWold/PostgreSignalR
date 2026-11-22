using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

/// <summary>
/// With no hubs opted in through <c>AddBackplaneHub</c>, every hub uses the backplane.
/// </summary>
public sealed class AllBackplaneHubsTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, BackplaneTestConfiguration.Default)
{
    [RetryFact]
    public async Task EveryHub_UsesBackplane()
    {
        var diagnostics = await Server1.GetHubDiagnosticsAsync();

        Assert.True(diagnostics.TestHubUsesBackplane);
        Assert.True(diagnostics.SecondHubUsesBackplane);
    }

    [RetryFact]
    public async Task SecondHub_BroadcastReachesOtherServers()
    {
        await using var sender = await Server1.CreateSecondHubClientAsync();
        await using var receiver = await Server2.CreateSecondHubClientAsync();

        var message = receiver.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
    }
}

/// <summary>
/// Opting the test hub in puts only it on the backplane. The second hub uses SignalR's default lifetime manager,
/// so its messages stay on the server that sent them.
/// </summary>
public sealed class OptInBackplaneHubTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(BackplaneHubOptIn: BackplaneHubOptIn.AfterAddPostgresBackplane))
{
    [RetryFact]
    public async Task OnlyOptedInHub_UsesBackplane()
    {
        var diagnostics = await Server1.GetHubDiagnosticsAsync();

        Assert.True(diagnostics.TestHubUsesBackplane);
        Assert.False(diagnostics.SecondHubUsesBackplane);
    }

    [RetryFact]
    public async Task OptedInHub_BroadcastReachesOtherServers()
    {
        await using var sender = await Server1.CreateClientAsync();
        await using var receiver = await Server2.CreateClientAsync();

        var message = receiver.ExpectMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await message).Arg<string>());
    }

    [RetryFact]
    public async Task OtherHub_BroadcastStaysOnItsServer()
    {
        await using var sender = await Server1.CreateSecondHubClientAsync();
        await using var localReceiver = await Server1.CreateSecondHubClientAsync();
        await using var remoteReceiver = await Server2.CreateSecondHubClientAsync();

        // Start both waits before sending, since a client drops a message nothing is waiting for.
        var localMessage = localReceiver.ExpectMessageAsync(nameof(IClient.Message));
        var noRemoteMessage = remoteReceiver.EnsureNoMessageAsync(nameof(IClient.Message));

        await sender.Send.SendToAll(ShortMessage);

        Assert.Equal(ShortMessage, (await localMessage).Arg<string>());
        await noRemoteMessage;
    }
}

/// <summary>
/// Opting a hub in before calling <c>AddPostgresBackplane</c> works the same as after.
/// </summary>
public sealed class OptInBeforeAddPostgresBackplaneTests(ContainerFixture fixture) : TestData
{
    private TestServer _server = null!;

    public override async ValueTask InitializeAsync() =>
        (_server, _) = await fixture.GetSingleServerAsync(new(BackplaneHubOptIn: BackplaneHubOptIn.BeforeAddPostgresBackplane));

    [RetryFact]
    public async Task OnlyOptedInHub_UsesBackplane()
    {
        var diagnostics = await _server.GetHubDiagnosticsAsync();

        Assert.True(diagnostics.TestHubUsesBackplane);
        Assert.False(diagnostics.SecondHubUsesBackplane);
    }
}
