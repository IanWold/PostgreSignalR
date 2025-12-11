namespace PostgreSignalR.IntegrationTests;

public class InvokeAndStreamingTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task Invoke_ReturnsAcrossServers()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var caller = await server1.CreateClientAsync();
        await using var callee = await server2.CreateClientAsync();

        var calleeId = await callee.Send.GetConnectionId();
        var result = await caller.Send.InvokeConnectionEcho(calleeId, "payload");
        Assert.Equal("echo:payload", result);
    }
}
