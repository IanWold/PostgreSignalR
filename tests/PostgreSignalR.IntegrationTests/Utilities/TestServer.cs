namespace PostgreSignalR.IntegrationTests;

public class TestServer(TestServerContainer container) : IAsyncDisposable
{
    public async Task<TestClient> CreateClientAsync(string? user = null, bool waitForHealthy = true, bool useMessagePack = false)
    {
        var client = await TestClient.CreateAsync(container.HubUri, user, useMessagePack);

        if (!waitForHealthy)
        {
            return client;
        }

        try
        {
            using var httpClient = new HttpClient();
            var isReady = false;

            for (var i = 0; i < TestTimeouts.HealthCheckMaxAttempts; i++)
            {
                try
                {
                    using var response = await httpClient.GetAsync(container.HealthUri);
                    if (response.IsSuccessStatusCode)
                    {
                        isReady = true;
                        break;
                    }
                }
                catch { }

                await Task.Delay(TestTimeouts.HealthCheckPollInterval);
            }

            if (!isReady)
            {
                throw new TimeoutException("Health check did not report ready.");
            }

            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task RestartAsync()
    {
        await container.StopAsync();
        await container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
