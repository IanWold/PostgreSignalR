namespace PostgreSignalR.IntegrationTests;

public class InvokeAndStreamingTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [RetryFact]
    public async Task Invoke_ReturnsAcrossServers()
    {
        await using var caller = await Server1.CreateClientAsync();
        await using var callee = await Server2.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();
        var result = await caller.Send.InvokeConnectionEcho(calleeId, "payload");
        Assert.Equal("echo:payload", result);
    }
}
