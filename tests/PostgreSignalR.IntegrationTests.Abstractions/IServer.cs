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

    #region SimpleObject

    Task SendToAll_SimpleObject(SimpleObject obj);
    Task SendToConnection_SimpleObject(string connectionId, SimpleObject obj);
    Task SendToAllInGroup_SimpleObject(string groupName, SimpleObject obj);
    Task SendToUsers_SimpleObject(string[] userIds, SimpleObject obj);

    #endregion

    #region ComplexObject

    Task SendToAll_ComplexObject(ComplexObject obj);
    Task SendToConnection_ComplexObject(string connectionId, ComplexObject obj);
    Task SendToAllInGroup_ComplexObject(string groupName, ComplexObject obj);
    Task SendToUsers_ComplexObject(string[] userIds, ComplexObject obj);
    
    #endregion
}
