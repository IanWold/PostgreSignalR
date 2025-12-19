namespace PostgreSignalR.IntegrationTests.Abstractions;

public interface IServer
{
    Task<string> Echo(string message);
    Task SendToAll(string message);
    Task SendToAllExcept(string message, string excludedConnectionId);
    Task SendToCaller(string message);
    Task SendToOthers(string message);
    Task<string> GetConnectionId();
    Task JoinGroup(string groupName);
    Task LeaveGroup(string groupName);
    Task SendToAllInGroup(string groupName, string message);
    Task SendToAllInGroups(string[] groupNames, string message);
    Task SendToOthersInGroup(string groupName, string message);
    Task SendToGroupExcept(string groupName, string excludedConnectionId, string message);
    Task SendToConnection(string connectionId, string message);
    Task SendToConnections(string[] connectionIds, string message);
    Task SendToUser(string userId, string message);
    Task SendToUsers(string[] userIds, string message);
    Task<string> InvokeConnectionEcho(string connectionId, string message);
}
