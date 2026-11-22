namespace PostgreSignalR.Benchmarks.Abstractions;

/// <summary>
/// Returned by the server's <c>/listener</c> endpoint when the Postgres backplane is in use.
/// </summary>
public record ListenerStats(
    string ChannelRouting,
    int ChannelCount,
    long ListenCount
);
