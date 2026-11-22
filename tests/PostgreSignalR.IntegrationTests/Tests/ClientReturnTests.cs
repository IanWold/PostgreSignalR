namespace PostgreSignalR.IntegrationTests;

public abstract class ClientReturnTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ConfigurableBaseTest(fixture, configuration)
{
    [RetryFact]
    [Trait(Traits.Category, Traits.PostgresCompat)]
    public async Task Invoke_ReturnsAcrossServers()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server2.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();

        var result = await caller.Send.InvokeConnectionEcho(calleeId, ShortMessage);

        Assert.Equal($"echo:{ShortMessage}", result);
    }

    [RetryFact]
    public async Task Invoke_ReturnsWithinSameServer()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server1.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();

        var result = await caller.Send.InvokeConnectionEcho(calleeId, ShortMessage);

        Assert.Equal($"echo:{ShortMessage}", result);
    }
}

public sealed class PerTargetClientReturnTests(ContainerFixture fixture) : ClientReturnTests(fixture, BackplaneTestConfiguration.Default);

public sealed class SharedClientReturnTests(ContainerFixture fixture) : ClientReturnTests(fixture, BackplaneTestConfiguration.Shared);
