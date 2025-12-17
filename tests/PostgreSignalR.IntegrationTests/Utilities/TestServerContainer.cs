using DotNet.Testcontainers.Containers;

namespace PostgreSignalR.IntegrationTests;

public class TestServerContainer(IContainer container) : IAsyncDisposable
{
    public Uri HubUri =>
        new($"http://localhost:{container.GetMappedPublicPort(8080)}/hub");
    
    public Uri HealthUri =>
        new($"http://localhost:{container.GetMappedPublicPort(8080)}/health");

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
