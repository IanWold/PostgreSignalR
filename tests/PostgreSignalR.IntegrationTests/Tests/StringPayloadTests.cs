using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class StringPayloadTests(ContainerFixture fixture) : PayloadTestBase<string>(fixture)
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
