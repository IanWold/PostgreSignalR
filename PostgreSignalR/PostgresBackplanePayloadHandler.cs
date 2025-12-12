using System.Timers;
using Npgsql;

namespace PostgreSignalR;

internal interface IPostgresBackplanePayloadHandler
{
    Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default);
    byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs);
}

internal class AlwaysUseEventPayloadHandler(PostgresBackplaneOptions options) : IPostgresBackplanePayloadHandler
{
    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        using var connection = await options.DataSource.OpenConnectionAsync(ct);
        using var command = new NpgsqlCommand($"NOTIFY {channelName}, '{Convert.ToBase64String(message)}';", connection);

        await command.ExecuteNonQueryAsync(ct);
    }

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        Convert.FromBase64String(eventArgs.Payload);
}

internal class AlwaysUseTablePayloadHandler : IPostgresBackplanePayloadHandler
{
    private readonly PostgresBackplaneOptions _options;
    private readonly string _tableName;
    private readonly string _reqdQuery;
    private readonly string _cleanupQuery;

    private readonly System.Timers.Timer _cleanupTimer = new();

    public AlwaysUseTablePayloadHandler(PostgresBackplaneOptions options)
    {
        _options = options;
        _tableName = options.PayloadTable.QualifiedTableName;
        _reqdQuery = $"SELECT payload FROM {_options.PayloadTable.QualifiedTableName} WHERE id = @id;";
        _cleanupQuery = $"DELETE FROM {_options.PayloadTable.QualifiedTableName} WHERE EXTRACT(EPOCH FROM (NOW() - created_at)) * 1000 > {_options.PayloadTable.AutomaticCleanupTtlMs};";

        if (options.PayloadTable.AutomaticCleanup)
        {
            _cleanupTimer.Elapsed += CleanupIds;
            _cleanupTimer.Interval = options.PayloadTable.AutomaticCleanupIntervalMs;
            _cleanupTimer.Start();
        }
    }

    private void CleanupIds(object? sender, ElapsedEventArgs e)
    {
        using var connection = _options.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(_cleanupQuery, connection);
        command.ExecuteNonQuery();
    }

    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        var query = $"""
            WITH inserted AS
            (
                INSERT INTO {_tableName} (payload)
                VALUES (@payload)
                RETURNING id
            )

            SELECT pg_notify('{channelName}', 'id:' || id)
            FROM inserted;
            """;

        using var connection = await _options.DataSource.OpenConnectionAsync(ct);
        using var command = new NpgsqlCommand(query, connection);

        command.Parameters.Add(new("payload", message));

        await command.ExecuteScalarAsync(ct);
    }

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs)
    {
        using var connection = _options.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(_reqdQuery, connection);

        command.Parameters.Add(new("id", Convert.ToInt64(eventArgs.Payload[3..])));

        var reader = command.ExecuteReader();
        reader.Read();

        var message = (byte[])reader[0];
        return message;
    }
}

internal class UseTableWhenLargePayloadHandler(PostgresBackplaneOptions options) : IPostgresBackplanePayloadHandler
{
    private readonly AlwaysUseEventPayloadHandler _useEvent = new(options);
    private readonly AlwaysUseTablePayloadHandler _useTable = new(options);

    public Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default) =>
        message.Length > 6000
        ? _useTable.NotifyAsync(channelName, message, ct)
        : _useEvent.NotifyAsync(channelName, message, ct);

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        eventArgs.Payload[..3] == "id:"
        ? _useTable.ResolveNotificationPayload(eventArgs)
        : _useEvent.ResolveNotificationPayload(eventArgs);
}