using Npgsql;

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

    protected static Task AssertEventuallyDeliveredAsync(TestClient sender, TestClient receiver, TimeSpan? timeout = null) =>
        RetryAssertions.AssertEventuallyDeliveredAsync(sender, receiver, timeout);

    protected static Task AssertEventuallyDeliveredAsync(Func<Task> sendAction, TestClient receiver, string messageKey, TimeSpan? timeout = null) =>
        RetryAssertions.AssertEventuallyDeliveredAsync(sendAction, receiver, messageKey, timeout);

    protected static Task AssertEventuallySucceedsAsync(Func<Task> action, TimeSpan? timeout = null, TimeSpan? perAttemptTimeout = null) =>
        RetryAssertions.AssertEventuallySucceedsAsync(action, timeout, perAttemptTimeout);
}
