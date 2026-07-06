using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

internal static class RetryAssertions
{
    public static Task AssertEventuallyDeliveredAsync(TestClient sender, TestClient receiver, TimeSpan? timeout = null) =>
        AssertEventuallyDeliveredAsync(() => sender.Send.SendToAll(Guid.NewGuid().ToString()), receiver, nameof(IClient.Message), timeout);

    public static Task AssertEventuallyDeliveredAsync(Func<Task> sendAction, TestClient receiver, string messageKey, TimeSpan? timeout = null) =>
        AssertEventuallySucceedsAsync(
            async () =>
            {
                var message = receiver.ExpectMessageAsync(messageKey, TimeSpan.FromSeconds(2));

                await sendAction();
                await message;
            },
            timeout
        );

    public static async Task AssertEventuallySucceedsAsync(Func<Task> action, TimeSpan? timeout = null, TimeSpan? perAttemptTimeout = null)
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
