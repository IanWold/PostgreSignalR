namespace PostgreSignalR.IntegrationTests.Abstractions;

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
    Task SendToAllExcept(string message, string excludedConnectionId);
    Task<string> EchoWithServerId(string message);
    IAsyncEnumerable<string> StreamSequence(int count, string prefix);
    Task<string> InvokeConnectionEcho(string connectionId, string message);
    Task SendStreamToConnection(string connectionId, int count, string prefix);
}
