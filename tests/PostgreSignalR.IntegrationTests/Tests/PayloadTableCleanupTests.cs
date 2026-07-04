using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR.IntegrationTests;

public class PayloadTableCleanupTests(ContainerFixture fixture) : ConfigurableBaseTest(fixture, new(PayloadTableStorage: PayloadTableStorage.Always, AutomaticCleanupIntervalMs: 500))
{
    private const string TableName = "backplane_payloads";

    [RetryFact]
    public async Task ExpiredPayloadRowIsRemovedByAutomaticCleanup()
    {
        long id;
        await using (var connection = new NpgsqlConnection(DatabaseConnectionString))
        {
            await connection.OpenAsync();

            await using var insertCommand = new NpgsqlCommand(
                $"INSERT INTO {TableName} (payload, created_at) VALUES (@payload, now() - interval '1 hour') RETURNING id;",
                connection
            );

            insertCommand.Parameters.Add(new("payload", "test"u8.ToArray()) { NpgsqlDbType = NpgsqlDbType.Bytea });

            id = (long)(await insertCommand.ExecuteScalarAsync())!;
        }

        for (var i = 0; i < 20; i++)
        {
            await using var connection = new NpgsqlConnection(DatabaseConnectionString);
            await connection.OpenAsync();

            await using var countCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName} WHERE id = @id;", connection);
            countCommand.Parameters.Add(new("id", id));

            var remaining = (long)(await countCommand.ExecuteScalarAsync())!;
            if (remaining == 0)
            {
                return;
            }

            await Task.Delay(TestTimeouts.HealthCheckPollInterval);
        }

        Assert.Fail("Expired payload row was not removed by automatic cleanup within the expected window.");
    }
}
