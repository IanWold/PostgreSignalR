using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ClientReceiver(Func<string, object?[], Task> callback) : IClient
{
    public Task Message(string message) =>
        callback(nameof(IClient.Message), [message]);

    public Task<string> EchoBack(string message) =>
        Task.FromResult($"echo:{message}");
}
