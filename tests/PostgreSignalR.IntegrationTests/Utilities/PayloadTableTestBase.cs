using Npgsql;
using NpgsqlTypes;

namespace PostgreSignalR.IntegrationTests;

public abstract class PayloadTableTestBase(ContainerFixture fixture, BackplaneTestConfiguration configuration) : TestData
{
    private const string TableName = "backplane_payloads";

    protected string DatabaseConnectionString { get; private set; } = null!;

    public override async ValueTask InitializeAsync() =>
        (_, DatabaseConnectionString) = await fixture.GetSingleServerAsync(configuration);

    public override ValueTask DisposeAsync() =>
        default;

    protected async Task<long> InsertPayloadRowAsync(TimeSpan age)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();

        await using var insertCommand = new NpgsqlCommand(
            $"INSERT INTO {TableName} (payload, created_at) VALUES (@payload, @createdAt) RETURNING id;",
            connection
        );

        insertCommand.Parameters.Add(new("payload", "test"u8.ToArray()) { NpgsqlDbType = NpgsqlDbType.Bytea });
        insertCommand.Parameters.Add(new("createdAt", DateTimeOffset.UtcNow - age));

        return (long)(await insertCommand.ExecuteScalarAsync())!;
    }

    private async Task<long> CountPayloadRowsAsync(long id)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();

        await using var countCommand = new NpgsqlCommand($"SELECT COUNT(*) FROM {TableName} WHERE id = @id;", connection);
        countCommand.Parameters.Add(new("id", id));

        return (long)(await countCommand.ExecuteScalarAsync())!;
    }

    protected async Task AssertRowRemovedWithinAsync(long id, int maxAttempts = 20)
    {
        for (var i = 0; i < maxAttempts; i++)
        {
            if (await CountPayloadRowsAsync(id) == 0)
            {
                return;
            }

            await Task.Delay(TestTimeouts.HealthCheckPollInterval);
        }

        Assert.Fail($"Payload row {id} was not removed by automatic cleanup within the expected window.");
    }

    protected async Task AssertRowStillExistsAsync(long id) =>
        Assert.Equal(1, await CountPayloadRowsAsync(id));
}
