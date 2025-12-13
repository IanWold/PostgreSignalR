namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture)
{
    internal TestServer Server1 => fixture.SharedServer1!;
    internal TestServer Server2 => fixture.SharedServer2!;
}
