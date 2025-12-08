namespace PostgreSignalR.IntegrationTests.App;

public interface IServer
{
    Task<string> Echo(string message);
    Task SendToAll(string message);
    Task SendToCaller(string message);
    Task SendToOthers(string message);
    Task<string> GetConnectionId();
    Task JoinGroup(string groupName);
    Task LeaveGroup(string groupName);
    Task SendToGroup(string groupName, string message);
    Task SendToGroupExcept(string groupName, string excludedConnectionId, string message);
    Task SendToConnection(string connectionId, string message);
    Task SendToConnections(string[] connectionIds, string message);
    Task SendToUser(string userId, string message);
    Task SendToUsers(string[] userIds, string message);
}
