namespace PostgreSignalR.IntegrationTests;

public abstract class ConfigurableBaseTest(ContainerFixture fixture, BackplaneTestConfiguration configuration) : TestData
{
    internal TestServer Server1 { get; private set; } = null!;
    internal TestServer Server2 { get; private set; } = null!;

    public override async ValueTask InitializeAsync() =>
        (Server1, Server2) = await fixture.GetServerPairAsync(configuration);
}
