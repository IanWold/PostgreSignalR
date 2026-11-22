using Microsoft.Extensions.Options;
using Npgsql;
using PostgreSignalR;

namespace PostgreSignalR.IntegrationTests.App;

/// <summary>
/// A payload strategy defined outside the library, like an app's own would be. Under shared channel routing, the backplane
/// can't put its routing header in front of this strategy's text, so it carries the header inside the message instead.
/// </summary>
public class CustomPayloadStrategy(IOptions<PostgresBackplaneOptions> options) : IPayloadStrategy
{
    protected NpgsqlDataSource DataSource => options.Value.DataSource;

    public Task NotifyAsync(string channelName, byte[] message, CancellationToken ct = default) =>
        SendNotificationAsync(channelName, Convert.ToHexString(message), ct);

    public byte[] ResolveNotificationPayload(NpgsqlNotificationEventArgs eventArgs) =>
        Convert.FromHexString(eventArgs.Payload);

    protected async Task SendNotificationAsync(string channelName, string payload, CancellationToken ct)
    {
        await using var connection = await DataSource.OpenConnectionAsync(ct);
        await using var command = new NpgsqlCommand("SELECT pg_notify(@channelName, @payload);", connection);

        command.Parameters.Add(new("channelName", channelName));
        command.Parameters.Add(new("payload", payload));

        await command.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// A custom payload strategy that implements <see cref="IPrefixedPayloadStrategy"/>, so under shared channel routing
/// a server only resolves the payloads of messages it holds a target for. Counts those resolutions so tests can check that.
/// </summary>
public sealed class CustomPrefixedPayloadStrategy(IOptions<PostgresBackplaneOptions> options) : CustomPayloadStrategy(options), IPrefixedPayloadStrategy
{
    private long _prefixedResolveCount;

    public long PrefixedResolveCount => Interlocked.Read(ref _prefixedResolveCount);

    public Task NotifyAsync(string channelName, string payloadPrefix, byte[] message, CancellationToken ct = default) =>
        SendNotificationAsync(channelName, payloadPrefix + Convert.ToHexString(message), ct);

    public byte[] ResolveNotificationPayload(string payload)
    {
        Interlocked.Increment(ref _prefixedResolveCount);
        return Convert.FromHexString(payload);
    }
}
