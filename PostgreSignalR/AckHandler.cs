using System.Collections.Concurrent;

namespace PostgreSignalR;

internal sealed class AckHandler : IDisposable
{
    private readonly ConcurrentDictionary<int, AckInfo> _acks = new();
    private readonly Timer _timer;
    private readonly long _ackThreshold = (long)TimeSpan.FromSeconds(30).TotalMilliseconds;
    private readonly TimeSpan _ackInterval = TimeSpan.FromSeconds(5);
    private readonly Lock _lock = new();
    private bool _disposed;

    public AckHandler()
    {
        _timer = CreateNonCapturingTimer(state => ((AckHandler)state!).CheckAcks(), state: this, dueTime: _ackInterval, period: _ackInterval);
    }

    static Timer CreateNonCapturingTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var restoreFlow = false;

        try
        {
            if (!ExecutionContext.IsFlowSuppressed())
            {
                ExecutionContext.SuppressFlow();
                restoreFlow = true;
            }

            return new Timer(callback, state, dueTime, period);
        }
        finally
        {
            if (restoreFlow)
            {
                ExecutionContext.RestoreFlow();
            }
        }
    }

    public Task CreateAck(int id)
    {
        lock (_lock)
        {
            return _disposed
                ? Task.CompletedTask
                : _acks.GetOrAdd(id, _ => new AckInfo()).Tcs.Task;
        }
    }

    public void TriggerAck(int id)
    {
        if (_acks.TryRemove(id, out var ack))
        {
            ack.Tcs.TrySetResult();
        }
    }

    private void CheckAcks()
    {
        if (_disposed)
        {
            return;
        }

        var currentTick = Environment.TickCount64;

        foreach (var pair in _acks)
        {
            if (currentTick - pair.Value.CreatedTick > _ackThreshold && _acks.TryRemove(pair.Key, out var ack))
            {
                ack.Tcs.TrySetCanceled();
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;

            _timer.Dispose();

            foreach (var pair in _acks)
            {
                if (_acks.TryRemove(pair.Key, out var ack))
                {
                    ack.Tcs.TrySetCanceled();
                }
            }
        }
    }

    private sealed class AckInfo
    {
        public TaskCompletionSource Tcs { get; private set; }
        public long CreatedTick { get; private set; }

        public AckInfo()
        {
            CreatedTick = Environment.TickCount64;
            Tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
