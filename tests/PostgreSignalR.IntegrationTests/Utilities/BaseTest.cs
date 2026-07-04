namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture) : TestData
{
    internal TestServer Server1 => fixture.SharedServer1!;
    internal TestServer Server2 => fixture.SharedServer2!;
}
