using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR.Benchmarks.Server;

public class BenchmarkHub : Hub
{
    /// <summary>
    /// A no-op round trip. Completing it means the backplane has finished setting up the connection.
    /// </summary>
    public Task Ping() => Task.CompletedTask;

    public Task JoinGroup(string group) => Groups.AddToGroupAsync(Context.ConnectionId, group);
}
