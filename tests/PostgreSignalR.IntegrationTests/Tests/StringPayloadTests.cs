using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public abstract class StringPayloadTests(ContainerFixture fixture, BackplaneTestConfiguration configuration) : PayloadTestBase<string>(fixture, configuration)
{
    protected override string Payload =>
        ShortMessage;
        
    protected override string MessageKey =>
        nameof(IClient.Message);

    protected override Task SendToAllAsync(TestClient sender, string payload) =>
        sender.Send.SendToAll(payload);

    protected override Task SendToConnectionAsync(TestClient sender, string connectionId, string payload) =>
        sender.Send.SendToConnection(connectionId, payload);

    protected override Task SendToGroupAsync(TestClient sender, string groupName, string payload) =>
        sender.Send.SendToAllInGroup(groupName, payload);

    protected override Task SendToUsersAsync(TestClient sender, string[] userIds, string payload) =>
        sender.Send.SendToUsers(userIds, payload);
}

public sealed class PerTargetStringPayloadTests(ContainerFixture fixture) : StringPayloadTests(fixture, BackplaneTestConfiguration.Default);

public sealed class SharedStringPayloadTests(ContainerFixture fixture) : StringPayloadTests(fixture, BackplaneTestConfiguration.Shared);
