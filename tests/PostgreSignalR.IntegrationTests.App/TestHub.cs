using Microsoft.AspNetCore.SignalR;
using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests.App;

public class TestHub : Hub<IClient>, IServer
{
    public override async Task OnConnectedAsync() =>
        await base.OnConnectedAsync();

    public Task<string> Echo(string message) =>
        Task.FromResult(message);

    public async Task SendToAll(string message) =>
        await Clients.All.Message(message);

    public async Task SendToAllExcept(string message, string excludedConnectionId) =>
        await Clients.AllExcept([excludedConnectionId]).Message(message);

    public async Task SendToCaller(string message) =>
        await Clients.Caller.Message(message);

    public async Task SendToOthers(string message) =>
        await Clients.Others.Message(message);

    public Task<string> GetConnectionId() =>
        Task.FromResult(Context.ConnectionId);

    public async Task JoinGroup(string groupName) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

    public async Task LeaveGroup(string groupName) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    public async Task SendToAllInGroup(string groupName, string message) =>
        await Clients.Group(groupName).Message(message);

    public async Task SendToAllInGroups(string[] groupNames, string message) =>
        await Clients.Groups(groupNames).Message(message);

    public async Task SendToOthersInGroup(string groupName, string message) =>
        await Clients.OthersInGroup(groupName).Message(message);

    public async Task SendToGroupExcept(string groupName, string excludedConnectionId, string message) =>
        await Clients.GroupExcept(groupName, [excludedConnectionId]).Message(message);

    public async Task SendToConnection(string connectionId, string message) =>
        await Clients.Client(connectionId).Message(message);

    public async Task SendToConnections(string[] connectionIds, string message) =>
        await Clients.Clients(connectionIds).Message(message);

    public async Task SendToUser(string userId, string message) =>
        await Clients.User(userId).Message(message);

    public async Task SendToUsers(string[] userIds, string message) =>
        await Clients.Users(userIds).Message(message);

    public async Task<string> InvokeConnectionEcho(string connectionId, string message) =>
        await Clients.Client(connectionId).EchoBack(message);

    #region SimpleObject

    public async Task SendToAll_SimpleObject(SimpleObject obj) =>
        await Clients.All.MessageSimpleObject(obj);

    public async Task SendToConnection_SimpleObject(string connectionId, SimpleObject obj) =>
        await Clients.Client(connectionId).MessageSimpleObject(obj);

    public async Task SendToAllInGroup_SimpleObject(string groupName, SimpleObject obj) =>
        await Clients.Group(groupName).MessageSimpleObject(obj);

    public async Task SendToUsers_SimpleObject(string[] userIds, SimpleObject obj) =>
        await Clients.Users(userIds).MessageSimpleObject(obj);

    #endregion

    #region ComplexObject

    public async Task SendToAll_ComplexObject(ComplexObject obj) =>
        await Clients.All.MessageComplexObject(obj);

    public async Task SendToConnection_ComplexObject(string connectionId, ComplexObject obj) =>
        await Clients.Client(connectionId).MessageComplexObject(obj);

    public async Task SendToAllInGroup_ComplexObject(string groupName, ComplexObject obj) =>
        await Clients.Group(groupName).MessageComplexObject(obj);

    public async Task SendToUsers_ComplexObject(string[] userIds, ComplexObject obj) =>
        await Clients.Users(userIds).MessageComplexObject(obj);

    #endregion
}
