using Npgsql;
using System.Collections.Concurrent;
using System.Text;

namespace PostgreSignalR;

public sealed class PostgresListener(string connectionString) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();        // global shutdown
    private CancellationTokenSource _waitCts = new();              // cancels WaitAsync only
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<Func<NpgsqlConnection, CancellationToken, Task>> _ops = new();
    private Task? _pumpTask;
    private NpgsqlConnection? _conn;

    public event EventHandler<NpgsqlNotificationEventArgs>? OnNotification;

    public async Task EnsureReadyAsync()
    {
        await _gate.WaitAsync(_cts.Token);
        try
        {
            if (_conn is not null && _conn.FullState == System.Data.ConnectionState.Open && _pumpTask is { IsCompleted: false })
                return;

            if (_conn is not null)
            {
                try { await _conn.CloseAsync(); } catch { }
                _conn.Dispose();
            }

            _conn = new NpgsqlConnection(connectionString);
            await _conn.OpenAsync(_cts.Token);
            _conn.Notification += (_, e) => OnNotification?.Invoke(this, e);

            // Subscribe to any pre-added channels
            if (_channels.Count > 0)
                await ExecAsync(BuildListenSql(_channels), _cts.Token);

            // (re)create a fresh wait CTS and start the pump
            _waitCts?.Dispose();
            _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _pumpTask = Task.Run(() => PumpAsync(), _cts.Token);
        }
        finally { _gate.Release(); }
    }

    public async Task SubscribeAsync(string channel, CancellationToken ct = default)
    {
        channel = Normalize(channel);
        await _gate.WaitAsync(ct);
        try
        {
            if (!_channels.Add(channel)) return;

            // Enqueue LISTEN and poke the waiter
            _ops.Enqueue(async (conn, tok) =>
            {
                await ExecAsync($"LISTEN {channel};", tok, conn);
            });
            _waitCts.Cancel(); // wake Pump to apply the change
        }
        finally { _gate.Release(); }
    }

    public async Task UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        channel = Normalize(channel);
        await _gate.WaitAsync(ct);
        try
        {
            if (!_channels.Remove(channel)) return;

            _ops.Enqueue(async (conn, tok) =>
            {
                await ExecAsync($"UNLISTEN {channel};", tok, conn);
            });
            _waitCts.Cancel();
        }
        finally { _gate.Release(); }
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_channels.Count == 0) return;
            _channels.Clear();

            _ops.Enqueue(async (conn, tok) => { await ExecAsync("UNLISTEN *;", tok, conn); });
            _waitCts.Cancel();
        }
        finally { _gate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { if (_pumpTask is not null) await _pumpTask; } catch { }
        await _gate.WaitAsync();
        try
        {
            try { if (_conn is not null) await ExecAsync("UNLISTEN *;", CancellationToken.None, _conn); } catch { }
            try { if (_conn is not null) await _conn.CloseAsync(); } catch { }
            _conn?.Dispose();
            _waitCts.Dispose();
        }
        finally { _gate.Release(); _gate.Dispose(); _cts.Dispose(); }
    }

    // ============================ Internals ============================

    private async Task PumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var localConn = _conn;
            if (localConn is null) return;

            try
            {
                // Block until a notification or until we’re asked to reconfigure
                await localConn.WaitAsync(_waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (_cts.IsCancellationRequested) break; // shutting down

                // Reconfigure: swap tokens, drain and run queued ops
                await _gate.WaitAsync(CancellationToken.None);
                try
                {
                    _waitCts.Dispose();
                    _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

                    // drain ops
                    while (_ops.TryDequeue(out var op))
                    {
                        try { await op(localConn, _cts.Token); }
                        catch
                        {
                            // If LISTEN/UNLISTEN failed (e.g., connection hiccup), force reconnect
                            await ReconnectAsync(_cts.Token);
                            localConn = _conn!; // replaced
                        }
                    }
                }
                finally { _gate.Release(); }

                continue; // resume waiting with fresh token
            }
            catch
            {
                // Connection error: reconnect and re-LISTEN everything
                await ReconnectAsync(_cts.Token);
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try { if (_conn is not null) await _conn.CloseAsync(); } catch { }
            _conn?.Dispose();

            _conn = new NpgsqlConnection(connectionString);
            await _conn.OpenAsync(ct);
            _conn.Notification += (_, e) => OnNotification?.Invoke(this, e);

            if (_channels.Count > 0)
                await ExecAsync(BuildListenSql(_channels), ct);

            // reset waiter so Pump uses a valid token after reconnect
            _waitCts.Cancel();
        }
        finally { _gate.Release(); }
    }

    private static string BuildListenSql(IEnumerable<string> channels)
    {
        var sb = new StringBuilder();
        foreach (var ch in channels) sb.Append("LISTEN ").Append(ch).Append(';');
        return sb.ToString();
    }

    private async Task ExecAsync(string sql, CancellationToken ct, NpgsqlConnection? connOverride = null)
    {
        var conn = connOverride ?? _conn ?? throw new InvalidOperationException("Connection not open.");
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Normalize(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) throw new ArgumentException("Channel cannot be empty.", nameof(channel));
        return channel.Trim();
    }
}
