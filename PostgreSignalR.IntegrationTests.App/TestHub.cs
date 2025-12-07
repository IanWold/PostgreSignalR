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
        await Clients.Caller.Receive(message);

    public async Task SendToOthers(string message) =>
        await Clients.Others.Receive(message);
}
