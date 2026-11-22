using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR.Benchmarks.Server;

/// <summary>
/// Benchmark clients don't authenticate, so they pick their user with the <c>user</c> query string parameter.
/// Connections without it have no user, the same as the default provider without auth.
/// </summary>
public sealed class QueryStringUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.GetHttpContext()?.Request.Query["user"].ToString() is { Length: > 0 } user ? user : null;
}
