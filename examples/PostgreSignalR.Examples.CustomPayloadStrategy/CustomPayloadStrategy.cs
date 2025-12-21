using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR.Examples.CustomPayloadStrategy;

// Inject IOptions<PostgresBackplaneOptions> backplaneOptions to get the npgsql data source
//     that was configured along with the backplane.
// This ensures consistency.
public class CustomPayloadStrategy(IOptions<PostgresBackplaneOptions> backplaneOptions) : IPostgresBackplanePayloadStrategy
{
    // This method is called whenever we need to NOTIFY in postgres.
    // This method should NOT modify the channel name or message.
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
        command.Parameters.Add(new("channelName", channelName));

        // You must ensure that this method will either call NOTIFY or pg_notify()
        //     otherwise the backplane will not work!
        // In this case, on the database we've created a trigger to notify on insert,
        //     so an explicit call in the query isn't needed.
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    // This method is called after we receive a notification from Postgres (from LISTEN)
    // Npgsql implements this as an event callback, which in C# must run synchronously.
    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs)
    {
        var query = """
            SELECT payload
            FROM backplane_notifications
            WHERE id = @id;
            """;

        using var connection = backplaneOptions.Value.DataSource.OpenConnection();
        using var command = new NpgsqlCommand(query, connection);

        command.Parameters.Add(new("id", Convert.ToInt64(eventArgs.Payload)));

        var reader = command.ExecuteReader();
        reader.Read();

        var message = (byte[])reader[0];
        return message;
    }
}
