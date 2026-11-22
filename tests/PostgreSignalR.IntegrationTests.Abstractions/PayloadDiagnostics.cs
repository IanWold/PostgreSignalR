namespace PostgreSignalR.IntegrationTests.Abstractions;

/// <summary>
/// The state of a test server's custom prefixed payload strategy, from its diagnostics endpoint.
/// </summary>
/// <param name="PrefixedResolveCount">The number of prefixed payloads the server has resolved.</param>
public sealed record PayloadDiagnostics(long PrefixedResolveCount);
