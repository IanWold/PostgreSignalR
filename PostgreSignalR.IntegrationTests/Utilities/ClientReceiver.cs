using PostgreSignalR.IntegrationTests.App;

namespace PostgreSignalR.IntegrationTests;

public class ClientReceiver(Func<string, object?[], Task> callback) : IClient
{
    public Task Receive(string message) =>
        callback(nameof(IClient.Receive), [message]);

    public Task ReceiveCaller(string message) =>
        callback(nameof(IClient.ReceiveCaller), [message]);

    public Task ReceiveOthers(string message) =>
        callback(nameof(IClient.ReceiveOthers), [message]);

    public Task ReceiveGroup(string message) =>
        callback(nameof(IClient.ReceiveGroup), [message]);

    public Task ReceiveGroupRemoval(string message) =>
        callback(nameof(IClient.ReceiveGroupRemoval), [message]);

    public Task ReceiveConnection(string message) =>
        callback(nameof(IClient.ReceiveConnection), [message]);

    public Task ReceiveUser(string message) =>
        callback(nameof(IClient.ReceiveUser), [message]);
}
