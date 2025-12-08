using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR;

internal sealed class PostgresSubscriptionManager
{
    private readonly ConcurrentDictionary<string, HubConnectionStore> _subscriptions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task AddSubscriptionAsync(string id, HubConnectionContext connection, Func<string, HubConnectionStore, Task> subscribeMethod)
    {
        await _lock.WaitAsync();

        try
        {
            if (connection.ConnectionAborted.IsCancellationRequested)
            {
                return;
            }

            var subscription = _subscriptions.GetOrAdd(id, _ => new HubConnectionStore());

            subscription.Add(connection);

            if (subscription.Count == 1)
            {
                await subscribeMethod(id, subscription);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveSubscriptionAsync(string id, HubConnectionContext connection, object state, Func<object, string, Task> unsubscribeMethod)
    {
        await _lock.WaitAsync();

        try
        {
            if (!_subscriptions.TryGetValue(id, out var subscription))
            {
                return;
            }

            subscription.Remove(connection);

            if (subscription.Count == 0)
            {
                _subscriptions.TryRemove(id, out _);
                await unsubscribeMethod(state, id);
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}
