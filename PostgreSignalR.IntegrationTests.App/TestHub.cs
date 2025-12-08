using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR.IntegrationTests.App;

public class TestHub : Hub<IClient>, IServer
{
    public override async Task OnConnectedAsync() =>
        await base.OnConnectedAsync();

    public Task<string> Echo(string message) =>
        Task.FromResult(message);

    public async Task SendToAll(string message) =>
        await Clients.All.Receive(message);

    public async Task SendToCaller(string message) =>
        await Clients.Caller.ReceiveCaller(message);

    public async Task SendToOthers(string message) =>
        await Clients.Others.ReceiveOthers(message);

    public async Task JoinGroup(string groupName) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

    public async Task LeaveGroup(string groupName) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

    public async Task SendToGroup(string groupName, string message) =>
        await Clients.Group(groupName).ReceiveGroup(message);
}
