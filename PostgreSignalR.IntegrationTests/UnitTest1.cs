using PostgreSignalR.IntegrationTests.App;

namespace PostgreSignalR.IntegrationTests;

public class UnitTest1(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task Test1()
    {
        await using var server1 = await CreateServerAsync();
        await using var server2 = await CreateServerAsync();
        await using var client1 = await server1.CreateClientAsync();
        await using var client2 = await server2.CreateClientAsync();

        await client1.Send.SendToAll("hello");
        var msg1 = await client2.ExpectMessageAsync(nameof(IClient.Receive));

        Assert.Equal("hello", msg1.Arg<string>(0));
    }
}
