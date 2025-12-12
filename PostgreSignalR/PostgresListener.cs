using Npgsql;
using System.Collections.Concurrent;
using System.Text;

namespace PostgreSignalR;

internal sealed class PostgresListener(NpgsqlDataSource dataSource) : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _channels = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<Func<NpgsqlConnection, CancellationToken, Task>> _operations = new();

    private CancellationTokenSource _waitCts = new();
    private Task? _pumpTask;
    private NpgsqlConnection? _connection;

    public event EventHandler<NpgsqlNotificationEventArgs>? OnNotification;

    private async Task PumpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            if (_connection is not NpgsqlConnection connection)
            {
                return;
            }

            try
            {
                await connection.WaitAsync(_waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                if (_cts.IsCancellationRequested)
                {
                    break;
                }

                await _gate.WaitAsync(CancellationToken.None);

                try
                {
                    _waitCts.Dispose();
                    _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);

                    while (_operations.TryDequeue(out var operation))
                    {
                        try
                        {
                            await operation(connection, _cts.Token);
                        }
                        catch
                        {
                            await ReconnectAsync(_cts.Token);
                        }
                    }
                }
                finally
                {
                    _gate.Release();
                }

                continue;
            }
            catch
            {
                await ReconnectAsync(_cts.Token);
            }
        }
    }

    private async Task ReconnectAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);

        try
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

            if (_channels.Count > 0)
            {
                await ExecAsync(BuildListenSql(_channels), ct);
            }

            _waitCts.Cancel();
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string BuildListenSql(IEnumerable<string> channels)
    {
        var sb = new StringBuilder();
        foreach (var channel in channels)
        {
            sb.Append("LISTEN ").Append(channel.EscapeQutoes()).Append(';');
        }

        return sb.ToString();
    }

    private async Task ExecAsync(string sql, CancellationToken ct, NpgsqlConnection? connOverride = null)
    {
        await using var cmd = new NpgsqlCommand(sql, connOverride ?? _connection ?? throw new InvalidOperationException("Connection not open."));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Normalize(string channel) =>
        !string.IsNullOrWhiteSpace(channel)
        ? channel.Trim()
        : throw new ArgumentException("Channel cannot be empty.", nameof(channel));

    public async Task EnsureReadyAsync()
    {
        await _gate.WaitAsync(_cts.Token);
        try
        {
            if (_connection is not null && _connection.FullState == System.Data.ConnectionState.Open && _pumpTask is { IsCompleted: false })
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

            if (_channels.Count > 0)
            {
                await ExecAsync(BuildListenSql(_channels), _cts.Token);
            }

            _waitCts?.Dispose();
            _waitCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _pumpTask = Task.Run(() => PumpAsync(), _cts.Token);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SubscribeAsync(string channel, CancellationToken ct = default)
    {
        channel = Normalize(channel);
        await _gate.WaitAsync(ct);

        try
        {
            if (!_channels.Add(channel))
            {
                return;
            }

            _operations.Enqueue(async (conn, tok) =>
            {
                await ExecAsync($"LISTEN {channel.EscapeQutoes()};", tok, conn);
            });

            _waitCts.Cancel();
        }
        finally
        {
            _gate.Release();
        }
    }

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

            _operations.Enqueue(async (conn, tok) =>
            {
                await ExecAsync($"UNLISTEN {channel.EscapeQutoes()};", tok, conn);
            });

            _waitCts.Cancel();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UnsubscribeAllAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);

        try
        {
            if (_channels.Count == 0)
            {
                return;
            }

            _channels.Clear();

            _operations.Enqueue(async (conn, tok) =>
            {
                await ExecAsync("UNLISTEN *;", tok, conn);
            });

            _waitCts.Cancel();
        }
        finally
        {
            _gate.Release();
        }
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
