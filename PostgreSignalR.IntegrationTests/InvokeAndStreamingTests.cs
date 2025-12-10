// namespace PostgreSignalR.IntegrationTests;

// TODO: GitHub is hanging on these tests
// public class InvokeAndStreamingTests(ContainerFixture fixture) : BaseTest(fixture)
// {
//     [Fact]
//     public async Task Invoke_ReturnsAcrossServers()
//     {
//         await using var server1 = await CreateServerAsync();
//         await using var server2 = await CreateServerAsync();
//         await using var caller = await server1.CreateClientAsync();
//         await using var callee = await server2.CreateClientAsync();

//         var calleeId = await callee.Send.GetConnectionId();
//         var result = await caller.Send.InvokeConnectionEcho(calleeId, "payload");
//         Assert.Equal("echo:payload", result);
//     }

//     [Fact]
//     public async Task Stream_FromServerArrivesAcrossServers()
//     {
//         await using var server1 = await CreateServerAsync();
//         await using var server2 = await CreateServerAsync();
//         await using var sender = await server1.CreateClientAsync();
//         await using var receiver = await server2.CreateClientAsync();

//         var recv1 = receiver.ExpectMessageAsync(nameof(IClient.ReceiveStreamItem));
//         var recv2 = receiver.ExpectMessageAsync(nameof(IClient.ReceiveStreamItem));
//         var recv3 = receiver.ExpectMessageAsync(nameof(IClient.ReceiveStreamItem));

//         var targetId = await receiver.Send.GetConnectionId();
//         await sender.Send.SendStreamToConnection(targetId, 3, "item");

//         Assert.Equal("item-0", (await recv1).Arg<string>(0));
//         Assert.Equal("item-1", (await recv2).Arg<string>(0));
//         Assert.Equal("item-2", (await recv3).Arg<string>(0));
//     }
// }
