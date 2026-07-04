using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class ComplexObjectTests(ContainerFixture fixture) : PayloadTestBase<ComplexObject>(fixture)
{
    protected override ComplexObject Payload => RandomComplexObject;
    protected override string MessageKey =>
        nameof(IClient.MessageComplexObject);

    protected override Task SendToAllAsync(TestClient sender, ComplexObject payload) =>
        sender.Send.SendToAll_ComplexObject(payload);

    protected override Task SendToConnectionAsync(TestClient sender, string connectionId, ComplexObject payload) =>
        sender.Send.SendToConnection_ComplexObject(connectionId, payload);

    protected override Task SendToGroupAsync(TestClient sender, string groupName, ComplexObject payload) =>
        sender.Send.SendToAllInGroup_ComplexObject(groupName, payload);

    protected override Task SendToUsersAsync(TestClient sender, string[] userIds, ComplexObject payload) =>
        sender.Send.SendToUsers_ComplexObject(userIds, payload);
}
