using System.Timers;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR;

/// <summary>
/// Defines the strategy for storing and passing payloads through Postgres notifications.
/// </summary>
public interface IPayloadStrategy
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
/// A payload strategy that can put a prefix, given by the backplane, in front of its own text in the notification payload.
/// </summary>
/// <remarks>
/// <para>
/// Under <see cref="ChannelRouting.Shared"/>, every server receives every connection, user and group message. The backplane
/// puts the message's targets in the prefix, so a server that holds none of them drops the message without resolving its payload.
/// For a strategy that stores payloads elsewhere, such as <see cref="TablePayloadStrategy"/>, that saves a read on every other server.
/// </para>
/// <para>
/// Strategies that only implement <see cref="IPayloadStrategy"/> still work with shared routing, but every server resolves every
/// connection, user and group payload. The backplane uses the methods on this interface only for those messages under
/// <see cref="ChannelRouting.Shared"/>; every other notification uses the <see cref="IPayloadStrategy"/> methods.
/// </para>
/// </remarks>
public interface IPrefixedPayloadStrategy : IPayloadStrategy
{
    /// <summary>
    /// Called to dispatch a notification through Postgres whose payload starts with a prefix.
    /// This method <i>must</i> call <c>NOTIFY</c> or <c>pg_notify</c>, and the notification payload must be
    /// <paramref name="payloadPrefix"/>, unchanged, followed by the strategy's own text.
    /// </summary>
    /// <remarks>
    /// The prefix is ASCII text, so its length in characters is its length in bytes, and it counts toward the Postgres limit:
    /// the whole notification payload must be less than 8000 bytes. Treat it as opaque; it may change between versions.
    /// </remarks>
    /// <param name="channelName">The name of the channel to notify.</param>
    /// <param name="payloadPrefix">The text that must start the notification payload.</param>
    /// <param name="message">The message to resolve into a payload.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the work.</returns>
    Task NotifyAsync(string channelName, string payloadPrefix, byte[] message, CancellationToken ct = default);

    /// <summary>
    /// Called after receiving a notification sent by <see cref="NotifyAsync(string, string, byte[], CancellationToken)"/>,
    /// on a server that holds one of the message's targets, to resolve the strategy's own text into the payload message.
    /// This is called within an event handler so must be synchronous.
    /// </summary>
    /// <param name="payload">The notification payload with the prefix removed: the text the strategy put after the prefix.</param>
    /// <returns>The message intended to be passed by the notification.</returns>
    byte[] ResolveNotificationPayload(string payload);
}

internal static class NotificationPayload
{
    /// <summary>
    /// Postgres rejects a notification payload of this many bytes or more.
    /// </summary>
    public const int MaxLength = 8000;

    /// <summary>
    /// Whether a message fits in a notification payload once base64 encoded and put after a prefix of ASCII text.
    /// Without a prefix, that's messages of up to 5997 bytes.
    /// </summary>
    public static bool FitsInline(int prefixLength, int messageLength) =>
        prefixLength + Base64Length(messageLength) < MaxLength;

    private static long Base64Length(int byteCount) =>
        (byteCount + 2L) / 3 * 4;
}

/// <summary>
/// A backplane payload strategy which always writes payloads directly to the notification event payload.
/// </summary>
/// <remarks>
/// Postgres limits notification payloads to less than 8000 bytes, so after base64 encoding, messages can be up to 5997 bytes.
/// </remarks>
/// <param name="options"></param>
public sealed class EventPayloadStrategy(IOptions<PostgresBackplaneOptions> options) : IPrefixedPayloadStrategy
{
    /// <inheritdoc/>
    public Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default) =>
        SendNotificationAsync(options.Value.DataSource, channelName, Convert.ToBase64String(message), ct);

    /// <inheritdoc/>
    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        Convert.FromBase64String(eventArgs.Payload);

    /// <inheritdoc/>
    Task IPrefixedPayloadStrategy.NotifyAsync(string channelName, string payloadPrefix, byte[] message, CancellationToken ct) =>
        SendNotificationAsync(options.Value.DataSource, channelName, payloadPrefix + Convert.ToBase64String(message), ct);

    /// <inheritdoc/>
    byte[] IPrefixedPayloadStrategy.ResolveNotificationPayload(string payload) =>
        Convert.FromBase64String(payload);

    internal static async Task SendNotificationAsync(NpgsqlDataSource dataSource, string channelName, string payload, CancellationToken ct)
    {
        using var connection = await dataSource.OpenConnectionAsync(ct);
        using var command = new NpgsqlCommand("SELECT pg_notify(@channelName, @payload);", connection);

        command.Parameters.Add(new("channelName", channelName));
        command.Parameters.Add(new("payload", payload));

        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// A backplane payload strategy that uses a table to store payloads, only dispatching a reference to the table through the notification event payload.
/// Can be configured to always use the table, or conditionally use the table by the size of the message.
/// </summary>
public sealed class TablePayloadStrategy : IPrefixedPayloadStrategy
{
    private const string ReferencePrefix = "id:";

    private readonly IOptions<PostgresBackplaneOptions> _backplaneOptions;
    private readonly string _tableName;
    private readonly string _readQuery;
    private readonly string _cleanupQuery;
    private readonly string _insertAndNotifyQuery;

    /// <summary>
    /// Whether messages that fit in a notification payload are sent there instead of in the table (<see cref="PayloadTableStorage.Auto"/>).
    /// </summary>
    private readonly bool _sendsSmallPayloadsInline;

    private readonly System.Timers.Timer _cleanupTimer = new();

    public TablePayloadStrategy(IOptions<PostgresBackplaneOptions> backplaneOptions, IOptions<PayloadTableOptions> tableOptions)
    {
        _backplaneOptions = backplaneOptions;
        _tableName = tableOptions.Value.QualifiedTableName;
        _readQuery = $"SELECT payload FROM {tableOptions.Value.QualifiedTableName} WHERE id = @id;";
        _cleanupQuery = $"DELETE FROM {tableOptions.Value.QualifiedTableName} WHERE EXTRACT(EPOCH FROM (NOW() - created_at)) * 1000 > {tableOptions.Value.AutomaticCleanupTtlMs};";
        _insertAndNotifyQuery = $"""
            WITH inserted AS
            (
                INSERT INTO {_tableName} (payload)
                VALUES (@payload)
                RETURNING id
            )

            SELECT pg_notify(@channelName, @payloadPrefix || '{ReferencePrefix}' || id::text)
            FROM inserted;
            """;

        if (tableOptions.Value.AutomaticCleanup)
        {
            _cleanupTimer.Elapsed += CleanupIds;
            _cleanupTimer.Interval = tableOptions.Value.AutomaticCleanupIntervalMs;
            _cleanupTimer.Start();
        }

        _sendsSmallPayloadsInline = tableOptions.Value.StorageMode == PayloadTableStorage.Auto;
    }

    private async void CleanupIds(object? sender, ElapsedEventArgs e)
    {
        try
        {
            await using var connection = await _backplaneOptions.Value.DataSource.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(_cleanupQuery, connection, transaction);

            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
        catch { }
    }

    /// <inheritdoc/>
    public Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default) =>
        NotifyCoreAsync(channelName, string.Empty, message, ct);

    /// <inheritdoc/>
    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        ResolvePayload(eventArgs.Payload);

    /// <inheritdoc/>
    Task IPrefixedPayloadStrategy.NotifyAsync(string channelName, string payloadPrefix, byte[] message, CancellationToken ct) =>
        NotifyCoreAsync(channelName, payloadPrefix, message, ct);

    /// <inheritdoc/>
    byte[] IPrefixedPayloadStrategy.ResolveNotificationPayload(string payload) =>
        ResolvePayload(payload);

    private async Task NotifyCoreAsync(string channelName, string payloadPrefix, byte[] message, CancellationToken ct)
    {
        if (_sendsSmallPayloadsInline && NotificationPayload.FitsInline(payloadPrefix.Length, message.Length))
        {
            await EventPayloadStrategy.SendNotificationAsync(_backplaneOptions.Value.DataSource, channelName, payloadPrefix + Convert.ToBase64String(message), ct);
            return;
        }

        using var connection = await _backplaneOptions.Value.DataSource.OpenConnectionAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);
        using var command = new NpgsqlCommand(_insertAndNotifyQuery, connection, transaction);

        command.Parameters.Add(new("payload", message) { NpgsqlDbType = NpgsqlDbType.Bytea });
        command.Parameters.Add(new("channelName", channelName));
        command.Parameters.Add(new("payloadPrefix", payloadPrefix));

        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private byte[] ResolvePayload(string payload)
    {
        if (!payload.StartsWith(ReferencePrefix, StringComparison.Ordinal))
        {
            if (_sendsSmallPayloadsInline)
            {
                return Convert.FromBase64String(payload);
            }
            else
            {
                throw new InvalidOperationException($"Notification payload not in expected format. Expected '{ReferencePrefix}<long>'. Payload: {payload}");
            }
        }

        var id = Convert.ToInt64(payload[ReferencePrefix.Length..]);

        using var connection = _backplaneOptions.Value.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(_readQuery, connection);

        command.Parameters.Add(new("id", id));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Payload record {id} was not found in {_tableName}. It may have been removed by automatic cleanup before it could be read; consider increasing AutomaticCleanupTtlMs.");
        }

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
