using Npgsql;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PostgreSignalR;

internal sealed class PostgresListener(NpgsqlDataSource dataSource, ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectRetryDelay = TimeSpan.FromSeconds(1);

    private const int PendingCommandCheckIntervalMs = 1000;

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<PendingCommand> _pendingCommands = new();

    private CancellationTokenSource _waitCts = new();
    private Task? _pumpTask;
    private NpgsqlConnection? _connection;
    private int _channelCount;
    private long _listenCount;

    public event EventHandler<NpgsqlNotificationEventArgs>? OnNotification;

    public event EventHandler? OnReconnected;

    internal int ChannelCount => Volatile.Read(ref _channelCount);

    internal long ListenCount => Interlocked.Read(ref _listenCount);

    private sealed class PendingCommand(string sql, int listenCount)
    {
        public string Sql { get; } = sql;

        public int ListenCount { get; } = listenCount;

        /// <summary>
        /// Completes once the command has taken effect on the listener connection. Continuations run asynchronously,
        /// so callers never continue on the pump while it holds <see cref="_gate"/>.
        /// </summary>
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task PumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_connection is not NpgsqlConnection connection)
            {
                return;
            }

            Task<bool>? wait = null;
            var reconnected = false;

            // Enqueue wakes the pump by cancelling _waitCts while holding _gate. Npgsql can miss a cancellation that arrives
            // while WaitAsync is still starting, so the pump starts waiting while holding _gate too: a wakeup then arrives
            // either before the wait starts, where the pending commands are run first, or once Npgsql is reading.
            await _gate.WaitAsync(CancellationToken.None);

            try
            {
                if (_waitCts.IsCancellationRequested)
                {
                    _waitCts.Dispose();
                    _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                }

                if (!_pendingCommands.IsEmpty)
                {
                    reconnected = await RunPendingCommandsAsync();
                }
                else
                {
                    // Also wake every PendingCommandCheckInterval, so if a wakeup is ever missed, commands are delayed
                    // rather than left waiting for the next notification. SubscribeAsync waits for its command to run.
                    wait = StartWaiting(connection);
                }
            }
            finally
            {
                _gate.Release();
            }

            if (reconnected)
            {
                OnReconnected?.Invoke(this, EventArgs.Empty);
            }

            if (wait is null)
            {
                continue;
            }

            try
            {
                await wait;
            }
            catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
            {
                // Woken to run commands
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await ReconnectAsync(_cts.Token);
                OnReconnected?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private Task<bool> StartWaiting(NpgsqlConnection connection)
    {
        try
        {
            return connection.WaitAsync(PendingCommandCheckIntervalMs, _waitCts.Token);
        }
        catch (Exception ex)
        {
            return Task.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// Runs every pending command in one round trip, reconnecting if that fails. Returns whether it reconnected.
    /// Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private async Task<bool> RunPendingCommandsAsync()
    {
        if (_pendingCommands.IsEmpty)
        {
            return false;
        }

        var batch = new List<PendingCommand>();
        while (_pendingCommands.TryDequeue(out var command))
        {
            batch.Add(command);
        }

        var reconnected = false;

        try
        {
            try
            {
                await ExecAsync(string.Concat(batch.Select(c => c.Sql)), _cts.Token);
                AddListens(batch.Sum(c => c.ListenCount));
            }
            catch when (!_cts.IsCancellationRequested)
            {
                // Reconnecting LISTENs on every channel in _channels, which already reflects every command in the batch.
                await ReconnectWithRetryCoreAsync(_cts.Token);
                reconnected = true;
            }
        }
        catch
        {
            foreach (var command in batch)
            {
                command.Completion.TrySetCanceled();
            }

            throw;
        }

        foreach (var command in batch)
        {
            command.Completion.TrySetResult();
        }

        return reconnected;
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
        {
            await ReconnectWithRetryCoreAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ReconnectWithRetryCoreAsync(CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await ReconnectCoreAsync(ct);
                logger.BackplaneReconnected();

                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.BackplaneReconnectFailedRetrying(ReconnectRetryDelay, ex);
                await Task.Delay(ReconnectRetryDelay, ct);
            }
        }
    }

    private async Task ReconnectCoreAsync(CancellationToken ct)
    {
        try
        {
            if (_connection is not null)
            {
                await _connection.CloseAsync();
            }
        }
        catch { }

        _connection?.Dispose();

        _connection = await dataSource.OpenConnectionAsync(ct);

        _connection.Notification += (_, e) => OnNotification?.Invoke(this, e);

        await ListenOnAllChannelsAsync(ct);

        _waitCts.Cancel();
    }

    /// <summary>
    /// LISTENs on every channel on a newly opened connection. That brings the connection up to date with every pending command,
    /// so they're completed rather than run. Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private async Task ListenOnAllChannelsAsync(CancellationToken ct)
    {
        if (_channels.Count > 0)
        {
            await ExecAsync(BuildListenSql(_channels), ct);
            AddListens(_channels.Count);
        }

        while (_pendingCommands.TryDequeue(out var command))
        {
            command.Completion.TrySetResult();
        }
    }

    private static string BuildListenSql(IEnumerable<string> channels)
    {
        var sb = new StringBuilder();
        foreach (var channel in channels)
        {
            sb.Append("LISTEN ").Append(channel.EscapeQuotes()).Append(';');
        }

        return sb.ToString();
    }

    private async Task ExecAsync(string sql, CancellationToken ct, NpgsqlConnection? connOverride = null)
    {
        await using var cmd = new NpgsqlCommand(sql, connOverride ?? _connection ?? throw new InvalidOperationException("Connection not open."));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private void AddListens(int count) =>
        Interlocked.Add(ref _listenCount, count);

    private static string Normalize(string channel) =>
        !string.IsNullOrWhiteSpace(channel)
        ? channel.Trim()
        : throw new ArgumentException("Channel cannot be empty.", nameof(channel));

    public async Task EnsureReadyAsync()
    {
        await _gate.WaitAsync(_cts.Token);
        try
        {
            // State, not FullState: while the pump waits for notifications, FullState is Open | Fetching.
            // Checking FullState would tear down the connection under a running pump and start a second pump.
            if (_connection is not null && _connection.State == System.Data.ConnectionState.Open && _pumpTask is { IsCompleted: false })
            {
                return;
            }

            if (_connection is not null)
            {
                try
                {
                    await _connection.CloseAsync();
                }
                catch { }

                _connection.Dispose();
            }

            _connection = await dataSource.OpenConnectionAsync(_cts.Token);

            _connection.Notification += (_, e) => OnNotification?.Invoke(this, e);

            await ListenOnAllChannelsAsync(_cts.Token);

            _waitCts?.Dispose();
            _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _pumpTask = Task.Run(() => PumpAsync(), _cts.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Subscribes to a channel. If the listener is running, returns once Postgres is listening on the channel,
    /// so a message published after this returns will be received.
    /// </summary>
    public async Task SubscribeAsync(string channel, CancellationToken ct = default)
    {
        channel = Normalize(channel);
        PendingCommand command;

        await _gate.WaitAsync(ct);

        try
        {
            if (!_channels.Add(channel))
            {
                return;
            }

            Volatile.Write(ref _channelCount, _channels.Count);

            command = Enqueue($"LISTEN {channel.EscapeQuotes()};", listenCount: 1);

            if (_pumpTask is not { IsCompleted: false })
            {
                // Nothing will run the command until EnsureReadyAsync opens the connection,
                // and that LISTENs on every channel in _channels.
                return;
            }
        }
        finally
        {
            _gate.Release();
        }

        await command.Completion.Task.WaitAsync(ct);
    }

    /// <summary>
    /// Unsubscribes from a channel. Returns once the UNLISTEN is queued, without waiting for it to run.
    /// </summary>
    public async Task UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        channel = Normalize(channel);
        await _gate.WaitAsync(ct);

        try
        {
            if (!_channels.Remove(channel))
            {
                return;
            }

            Volatile.Write(ref _channelCount, _channels.Count);

            Enqueue($"UNLISTEN {channel.EscapeQuotes()};", listenCount: 0);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Queues a command for the pump and wakes it. Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private PendingCommand Enqueue(string sql, int listenCount)
    {
        var command = new PendingCommand(sql, listenCount);

        _pendingCommands.Enqueue(command);
        _waitCts.Cancel();

        return command;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            if (_pumpTask is not null)
            {
                await _pumpTask;
            }
        }
        catch { }

        await _gate.WaitAsync();

        try
        {
            while (_pendingCommands.TryDequeue(out var command))
            {
                command.Completion.TrySetCanceled();
            }

            try
            {
                if (_connection is not null)
                {
                    await ExecAsync("UNLISTEN *;", CancellationToken.None, _connection);
                }
            }
            catch { }

            try
            {
                if (_connection is not null)
                {
                    await _connection.CloseAsync();
                }
            }
            catch { }

            _connection?.Dispose();
            _waitCts.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
            _cts.Dispose();
        }
    }
}
