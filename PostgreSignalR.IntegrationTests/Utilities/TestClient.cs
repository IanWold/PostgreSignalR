using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using PostgreSignalR.IntegrationTests.App;

namespace PostgreSignalR.IntegrationTests;

public class TestClient(HubConnection connection) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ClientMessage>> _waiters = new(StringComparer.OrdinalIgnoreCase);

    private IServer? _serverProxy;

    private ClientReceiver Receiver =>
        field ??= new(ReceiverCallback);

    public IServer Send =>
        _serverProxy ?? throw new Exception("Not initialized!");

    private Task ReceiverCallback(string key, object?[] args)
    {
        if (_waiters.TryRemove(key, out var waiter))
        {
            waiter.TrySetResult(new ClientMessage(key, args));
        }

        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        connection.ClientRegistration<IClient>(Receiver);
        _serverProxy = connection.ServerProxy<IServer>();
        
        await connection.StartAsync();
    }

    public static async Task<TestClient> CreateAsync(Uri address)
    {
        var client = new TestClient(
            new HubConnectionBuilder()
                .WithUrl(address)
                .WithAutomaticReconnect()
                .Build()
        );

        await client.InitializeAsync();
        return client;
    }
    
    public Task<ClientMessage> ExpectMessageAsync(string key, TimeSpan? timeout = null)
    {
        var waiter = new TaskCompletionSource<ClientMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[key] = waiter;

        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(3));
        cancellation.Token.Register(() => waiter.TrySetCanceled());

        return waiter.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
