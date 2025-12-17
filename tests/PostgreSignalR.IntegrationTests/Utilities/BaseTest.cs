namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture)
{
    internal TestServer Server1 => fixture.SharedServer1!;
    internal TestServer Server2 => fixture.SharedServer2!;

    internal string GroupName { get; } = Guid.NewGuid().ToString();
    internal string ShortMessage { get; } = Guid.NewGuid().ToString();
    internal string LongMessage { get; } = new string('A', 10000);
}
