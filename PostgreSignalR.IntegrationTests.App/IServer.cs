namespace PostgreSignalR.IntegrationTests.App;

public interface IServer
{
    Task<string> Echo(string message);
    Task SendToAll(string message);
    Task SendToCaller(string message);
    Task SendToOthers(string message);
    Task JoinGroup(string groupName);
    Task LeaveGroup(string groupName);
    Task SendToGroup(string groupName, string message);
}
