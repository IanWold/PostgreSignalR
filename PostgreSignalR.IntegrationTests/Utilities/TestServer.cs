namespace PostgreSignalR.IntegrationTests;

public class TestServer(TestServerContainer container) : IAsyncDisposable
{
    public async Task<TestClient> CreateClientAsync()
    {
        var client = await TestClient.CreateAsync(container.HubUri);

        using var httpClient = new HttpClient();
        var isReady = false;

        for (var i = 0; i < 120; i++)
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

            await Task.Delay(25);
        }

        return isReady ? client : throw new TimeoutException($"Health check did not report ready.");
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
