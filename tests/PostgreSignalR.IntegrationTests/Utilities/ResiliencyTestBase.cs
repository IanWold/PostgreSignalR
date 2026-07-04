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

    protected static Task AssertEventuallyDeliveredAsync(TestClient sender, TestClient receiver, TimeSpan? timeout = null) =>
        AssertEventuallyDeliveredAsync(() => sender.Send.SendToAll(Guid.NewGuid().ToString()), receiver, nameof(IClient.Message), timeout);

    protected static Task AssertEventuallyDeliveredAsync(Func<Task> sendAction, TestClient receiver, string messageKey, TimeSpan? timeout = null) =>
        AssertEventuallySucceedsAsync(
            async () =>
            {
                var message = receiver.ExpectMessageAsync(messageKey, TimeSpan.FromSeconds(2));

                await sendAction();
                await message;
            },
            timeout
        );

    protected static async Task AssertEventuallySucceedsAsync(Func<Task> action, TimeSpan? timeout = null, TimeSpan? perAttemptTimeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var attemptTimeout = perAttemptTimeout ?? TestTimeouts.RetryAttemptTimeout;
        Exception? lastFailure = null;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var attempt = action();
                var winner = await Task.WhenAny(attempt, Task.Delay(attemptTimeout));

                if (winner != attempt)
                {
                    throw new TimeoutException($"Attempt did not complete within {attemptTimeout}.");
                }

                await attempt;
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
