namespace PostgreSignalR.IntegrationTests.Abstractions;

/// <summary>
/// The state of a test server's listener connection, from its diagnostics endpoint.
/// </summary>
/// <param name="ChannelCount">The number of channels the listener is subscribed to.</param>
/// <param name="ListenCount">The number of LISTEN statements the listener has run.</param>
public sealed record ListenerDiagnostics(int ChannelCount, long ListenCount);
