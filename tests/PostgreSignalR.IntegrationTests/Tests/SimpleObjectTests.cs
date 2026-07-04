using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class SimpleObjectTests(ContainerFixture fixture) : PayloadTestBase<SimpleObject>(fixture)
{
    protected override SimpleObject Payload => RandomSimpleObject;
    protected override string MessageKey =>
        nameof(IClient.MessageSimpleObject);

    protected override Task SendToAllAsync(TestClient sender, SimpleObject payload) =>
        sender.Send.SendToAll_SimpleObject(payload);

    protected override Task SendToConnectionAsync(TestClient sender, string connectionId, SimpleObject payload) =>
        sender.Send.SendToConnection_SimpleObject(connectionId, payload);

    protected override Task SendToGroupAsync(TestClient sender, string groupName, SimpleObject payload) =>
        sender.Send.SendToAllInGroup_SimpleObject(groupName, payload);

    protected override Task SendToUsersAsync(TestClient sender, string[] userIds, SimpleObject payload) =>
        sender.Send.SendToUsers_SimpleObject(userIds, payload);
}
