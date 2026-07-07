using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR.IntegrationTests;

public class ClientErrorTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task InvokeConnection_CalleeThrowsSameServer()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server1.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();
        var exception = await Assert.ThrowsAsync<HubException>(() =>
            caller.Send.InvokeConnectionEchoWithError(calleeId, ShortMessage)
        );

        Assert.Contains(ShortMessage, exception.Message);
    }

    [RetryFact]
    public async Task InvokeConnection_CalleeThrowsCrossServer()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server2.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();
        var exception = await Assert.ThrowsAsync<HubException>(() =>
            caller.Send.InvokeConnectionEchoWithError(calleeId, ShortMessage)
        );

        Assert.Contains(ShortMessage, exception.Message);
    }

    [RetryFact]
    public async Task InvokeConnection_CalleeDisconnectsSameServer()
    {
        await using var caller = await Server1.CreateClientAsync();
        var callee = await Server1.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();
        var invokeTask = caller.Send.InvokeConnectionEchoSlow(calleeId, ShortMessage, 10_000);

        await Task.Delay(TestTimeouts.DisconnectSettleDelay);
        await callee.DisposeAsync();

        var exception = await Assert.ThrowsAsync<HubException>(() => invokeTask);

        Assert.Contains("disconnected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
