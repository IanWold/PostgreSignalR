using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR.Examples.CustomPayloadStrategy;

public class CustomPayloadStrategy(IOptions<PostgresBackplaneOptions> backplaneOptions) : IPostgresBackplanePayloadStrategy
{
    public async Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default)
    {
        var query = """
            INSERT INTO backplane_notifications (channel, payload)
            VALUES (@channelName, @payload);
            """;

        using var connection = await backplaneOptions.Value.DataSource.OpenConnectionAsync(ct);
        using var transaction = await connection.BeginTransactionAsync(ct);
        using var command = new NpgsqlCommand(query, connection, transaction);

        command.Parameters.Add(new("payload", message) { NpgsqlDbType = NpgsqlDbType.Bytea });
        command.Parameters.Add(new("channelName", channelName.Replace("\"", "\"\"")));

        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs)
    {
        using var connection = backplaneOptions.Value.DataSource.OpenConnection();
        using var command = new NpgsqlCommand("SELECT payload FROM backplane_notifications WHERE id = @id", connection);

        command.Parameters.Add(new("id", Convert.ToInt64(eventArgs.Payload)));

        var reader = command.ExecuteReader();
        reader.Read();

        var message = (byte[])reader[0];
        return message;
    }
}
