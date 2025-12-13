using Npgsql;

namespace PostgreSignalR;

public static class PostgresPayloadTableHelper
{
    public static async Task CreateTableAsync(string tableName, NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);

        var createQuery = $"""
            CREATE TABLE IF NOT EXISTS {tableName}
            (
                id BIGSERIAL PRIMARY KEY,
                payload BYTEA NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE INDEX ON {tableName} (created_at);
            """;

        using var createCommand = new NpgsqlCommand(createQuery, connection);
        await createCommand.ExecuteNonQueryAsync(ct);
    }
}
