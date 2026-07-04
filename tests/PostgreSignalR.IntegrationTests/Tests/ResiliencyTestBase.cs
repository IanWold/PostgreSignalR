using Npgsql;
using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public abstract class ResiliencyTestBase(ContainerFixture fixture, BackplaneTestConfiguration configuration) : ConfigurableBaseTest(fixture, configuration)
{
    protected async Task<int> TerminateListenerConnectionsAsync()
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT pg_terminate_backend(pid)
            FROM pg_stat_activity
            WHERE datname = current_database() AND state = 'idle' AND query ILIKE 'LISTEN %';
            """,
            connection
        );

        await using var reader = await command.ExecuteReaderAsync();

        var terminatedCount = 0;
        while (await reader.ReadAsync())
        {
            terminatedCount++;
        }

        return terminatedCount;
    }

    protected static async Task AssertEventuallyDeliveredAsync(TestClient sender, TestClient receiver, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        Exception? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var message = receiver.ExpectMessageAsync(nameof(IClient.Message), TimeSpan.FromSeconds(2));
                
                await sender.Send.SendToAll(Guid.NewGuid().ToString());
                await message;

                return;
            }
            catch (Exception ex)
            {
                lastFailure = ex;
                await Task.Delay(TestTimeouts.HealthCheckPollInterval);
            }
        }

        Assert.Fail($"Backplane did not recover within the expected window. Last failure: {lastFailure}");
    }
}
