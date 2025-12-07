using PostgreSignalR.IntegrationTests.App;

namespace PostgreSignalR.IntegrationTests;

public class ClientReceiver(Func<string, object?[], Task> callback) : IClient
{
    public Task Receive(string message) =>
        callback(nameof(IClient.Receive), [message]);
}
