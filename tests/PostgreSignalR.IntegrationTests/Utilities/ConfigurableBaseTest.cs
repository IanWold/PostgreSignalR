namespace PostgreSignalR.IntegrationTests;

public abstract class ConfigurableBaseTest(ContainerFixture fixture, BackplaneTestConfiguration configuration) : TestData, IAsyncLifetime
{
    internal TestServer Server1 { get; private set; } = null!;
    internal TestServer Server2 { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        (Server1, Server2) = await fixture.GetServerPairAsync(configuration);

    public ValueTask DisposeAsync() =>
        default;
}
