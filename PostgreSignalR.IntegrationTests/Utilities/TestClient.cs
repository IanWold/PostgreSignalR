using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PostgreSignalR.IntegrationTests.Abstractions;
using TypedSignalR.Client;

namespace PostgreSignalR.IntegrationTests;

public class TestClient(HubConnection connection) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskCompletionSource<ClientMessage>>> _waiters = new(StringComparer.OrdinalIgnoreCase);

    private IServer? _serverProxy;

    private ClientReceiver Receiver =>
        field ??= new(ReceiverCallback);

    public IServer Send =>
        _serverProxy ?? throw new Exception("Not initialized!");

    private Task ReceiverCallback(string key, object?[] args)
    {
        if (_waiters.TryGetValue(key, out var queue))
        {
            while (queue.TryDequeue(out var waiter))
            {
                if (waiter.Task.IsCanceled)
                {
                    continue;
                }

                waiter.TrySetResult(new ClientMessage(key, args));
                break;
            }
        }

        return Task.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        connection.Register<IClient>(Receiver);
        _serverProxy = connection.CreateHubProxy<IServer>();
        
        await connection.StartAsync();
    }

    public static async Task<TestClient> CreateAsync(Uri address, string? user = null)
    {
        var client = new TestClient(
            new HubConnectionBuilder()
                .WithUrl(AddUser(address, user))
                .WithAutomaticReconnect()
                .Build()
        );

        await client.InitializeAsync();
        return client;
    }

    private static Uri AddUser(Uri baseUri, string? user)
    {
        if (string.IsNullOrWhiteSpace(user)) return baseUri;

        var sep = string.IsNullOrEmpty(baseUri.Query) ? "?" : "&";
        return new Uri($"{baseUri}{sep}user={Uri.EscapeDataString(user)}");
    }
    
    public Task<ClientMessage> ExpectMessageAsync(string key, TimeSpan? timeout = null)
    {
        var waiter = new TaskCompletionSource<ClientMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var queue = _waiters.GetOrAdd(key, _ => new ConcurrentQueue<TaskCompletionSource<ClientMessage>>());
        queue.Enqueue(waiter);

        var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(1));
        var registration = cts.Token.Register(() => waiter.TrySetCanceled(cts.Token));

        return AwaitAndCleanupAsync(waiter.Task, cts, registration);
    }

    private static async Task<ClientMessage> AwaitAndCleanupAsync(Task<ClientMessage> task, CancellationTokenSource cts, CancellationTokenRegistration registration)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        finally
        {
            registration.Dispose();
            cts.Dispose();
        }
    }

    public async Task EnsureNoMessageAsync(string key, TimeSpan? timeout = null)
    {
        try
        {
            await ExpectMessageAsync(key, timeout ?? TimeSpan.FromMilliseconds(250));
            throw new Xunit.Sdk.XunitException($"Unexpected message for key '{key}'.");
        }
        catch (TaskCanceledException)
        {
            // expected
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    public async ValueTask DisposeAsync()
    {
        await connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
