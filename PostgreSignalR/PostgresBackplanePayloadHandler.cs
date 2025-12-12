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

internal class AlwaysUseTablePayloadHandler(PostgresBackplaneOptions options) : IPostgresBackplanePayloadHandler
{
    private readonly string _tableName = options.PayloadTable.QualifiedTableName;
    private readonly string _reqdQuery = $"SELECT payload FROM {options.PayloadTable.QualifiedTableName} WHERE id = @id";

    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        var query = $"""
            WITH inserted AS
            (
                INSERT INTO {_tableName} (payload)
                VALUES (@payload)
                RETURNING id;
            )
            NOTIFY {channelName}, (SELECT 'id:' || id FROM inserted);
            """;

        using var connection = await options.DataSource.OpenConnectionAsync(ct);
        using var command = new NpgsqlCommand(query, connection);

        command.Parameters.Add(new("payload", message));

        await command.ExecuteNonQueryAsync(ct);
    }

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs)
    {
        var id = Convert.ToInt32(eventArgs.Payload[3..]);

        using var connection = options.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(_reqdQuery, connection);

        var message = (byte[])command.ExecuteScalar()!;
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