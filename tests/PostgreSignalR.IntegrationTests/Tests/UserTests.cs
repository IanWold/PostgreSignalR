using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class UserTests(ContainerFixture fixture) : BaseTest(fixture)
{
    [Fact]
    public async Task Users_SendToUserHitsAllConnections()
    {
        await using var user1a = await Server1.CreateClientAsync("u1");
        await using var user1b = await Server2.CreateClientAsync("u1");
        await using var user2 = await Server2.CreateClientAsync("u2");

        var r1 = user1a.ExpectMessageAsync(nameof(IClient.Message));
        var r2 = user1b.ExpectMessageAsync(nameof(IClient.Message));

        await user2.Send.SendToUser("u1", "user-msg");

        Assert.Equal("user-msg", (await r1).Arg<string>());
        Assert.Equal("user-msg", (await r2).Arg<string>());
        await user2.EnsureNoMessageAsync(nameof(IClient.Message));
    }
}
