using Microsoft.AspNetCore.SignalR;

namespace PostgreSignalR.IntegrationTests.App;

public class QueryStringUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var httpContext = connection.GetHttpContext();
        if (httpContext is null)
        {
            return null;
        }

        if (httpContext.Request.Query.TryGetValue("user", out var values) && values.Count > 0)
        {
            return values[0];
        }

        return null;
    }
}
