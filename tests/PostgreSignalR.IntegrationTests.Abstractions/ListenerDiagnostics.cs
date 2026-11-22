namespace PostgreSignalR.IntegrationTests.Abstractions;

/// <summary>
/// The state of a test server's listener connection, from its diagnostics endpoint.
/// </summary>
/// <param name="ChannelCount">The number of channels the listener is subscribed to.</param>
/// <param name="ListenCount">The number of LISTEN statements the listener has run.</param>
/// <param name="ReconnectedCount">The number of times the backplane's <c>OnReconnected</c> event has been raised.</param>
public sealed record ListenerDiagnostics(int ChannelCount, long ListenCount, int ReconnectedCount);
