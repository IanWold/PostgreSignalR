using System.Timers;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR;

/// <summary>
/// Defines the strategy for storing and passing payloads through Postgres notifications.
/// </summary>
public interface IPostgresBackplanePayloadStrategy
{
    /// <summary>
    /// Called to dispatch a notification through Postgres.
    /// This method <i>must</i> call <c>NOTIFY</c> or <c>pg_notify</c>.
    /// </summary>
    /// <param name="channelName">The name of the channel to notify.</param>
    /// <param name="message">The message to resolve into a payload.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the work.</returns>
    Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default);

    /// <summary>
    /// Called after receiving a notification from Postgres to resolve the notification body into the payload message.
    /// This is called within an event handler so must be synchronous.
    /// </summary>
    /// <param name="eventArgs">The event args containing the payload from Postgres.</param>
    /// <returns>The message intended to be passed by the notification.</returns>
    byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs);
}

/// <summary>
/// A backplane payload strategy which always writes payloads directly to the notification event payload.
/// </summary>
/// <param name="options"></param>
public sealed class EventPayloadStrategy(IOptions<PostgresBackplaneOptions> options) : IPostgresBackplanePayloadStrategy
{
    /// <inheritdoc/>
    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        using var connection = await options.Value.DataSource.OpenConnectionAsync(ct);
        using var command = new NpgsqlCommand($"NOTIFY {channelName.EscapeQutoes()}, '{Convert.ToBase64String(message)}';", connection);

        await command.ExecuteNonQueryAsync(ct);
    }

    /// <inheritdoc/>
    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        Convert.FromBase64String(eventArgs.Payload);
}

/// <summary>
/// A backplane payload strategy that uses a table to store payloads, only dispatching a reference to the table through the notification event payload.
/// Can be configured to always use the table, or conditionally use the table by the size of the message.
/// </summary>
public sealed class TablePayloadStrategy : IPostgresBackplanePayloadStrategy
{
    private readonly IOptions<PostgresBackplaneOptions> _backplaneOptions;
    private readonly string _tableName;
    private readonly string _reqdQuery;
    private readonly string _cleanupQuery;

    private readonly EventPayloadStrategy? _eventStrategy;

    private readonly System.Timers.Timer _cleanupTimer = new();

    public TablePayloadStrategy(IOptions<PostgresBackplaneOptions> backplaneOptions, IOptions<PostgresBackplanePayloadTableOptions> tableOptions)
    {
        _backplaneOptions = backplaneOptions;
        _tableName = tableOptions.Value.QualifiedTableName;
        _reqdQuery = $"SELECT payload FROM {tableOptions.Value.QualifiedTableName} WHERE id = @id;";
        _cleanupQuery = $"DELETE FROM {tableOptions.Value.QualifiedTableName} WHERE EXTRACT(EPOCH FROM (NOW() - created_at)) * 1000 > {tableOptions.Value.AutomaticCleanupTtlMs};";

        if (tableOptions.Value.AutomaticCleanup)
        {
            _cleanupTimer.Elapsed += CleanupIds;
            _cleanupTimer.Interval = tableOptions.Value.AutomaticCleanupIntervalMs;
            _cleanupTimer.Start();
        }

        if (tableOptions.Value.StorageMode == PostgresBackplanePayloadTableStorage.Auto)
        {
            _eventStrategy = new EventPayloadStrategy(backplaneOptions);
        }
    }

    private void CleanupIds(object? sender, ElapsedEventArgs e)
    {
        using var connection = _backplaneOptions.Value.DataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = new NpgsqlCommand(_cleanupQuery, connection, transaction);

        command.ExecuteNonQuery();
        transaction.Commit();
    }

    /// <inheritdoc/>
    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        if (message.Length < 6000 && _eventStrategy is not null)
        {
            await _eventStrategy.NotifyAsync(channelName, message, ct);
            return;
        }

        var query = $"""
            WITH inserted AS
            (
                INSERT INTO {_tableName} (payload)
                VALUES (@payload)
                RETURNING id
            )

            SELECT pg_notify(@channelName, 'id:' || id::text)
            FROM inserted;
            """;

        using var connection = await _backplaneOptions.Value.DataSource.OpenConnectionAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);
        using var command = new NpgsqlCommand(query, connection, transaction);

        command.Parameters.Add(new("payload", message) { NpgsqlDbType = NpgsqlDbType.Bytea });
        command.Parameters.Add(new("channelName", channelName.Replace("\"", "\"\"")));

        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc/>
    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs)
    {
        if (eventArgs.Payload[..3] != "id:")
        {
            if (_eventStrategy is not null)
            {
                return _eventStrategy.ResolveNotificationPayload(eventArgs);
            }
            else
            {
                throw new InvalidOperationException($"Notification payload not in expected format. Expected 'id:<long>'. Payload: {eventArgs.Payload}");
            }
        }

        using var connection = _backplaneOptions.Value.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(_reqdQuery, connection);

        command.Parameters.Add(new("id", Convert.ToInt64(eventArgs.Payload[3..])));

        var reader = command.ExecuteReader();
        reader.Read();

        var message = (byte[])reader[0];
        return message;
    }

    /// <summary>
    /// Creates a standard table in Postgres to store payloads.
    /// </summary>
    /// <param name="ct">The <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Task"/> representing the work.</returns>
    public async Task InitializeTableAsync(CancellationToken ct = default)
    {
        await using var connection = await _backplaneOptions.Value.DataSource.OpenConnectionAsync(ct);

        var createQuery = $"""
            CREATE TABLE IF NOT EXISTS {_tableName}
            (
                id BIGSERIAL PRIMARY KEY,
                payload BYTEA NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX ON {_tableName} (created_at);
            """;

        using var createCommand = new NpgsqlCommand(createQuery, connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }
}
