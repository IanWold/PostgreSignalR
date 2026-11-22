namespace PostgreSignalR.IntegrationTests.Abstractions;

/// <summary>
/// Which of a test server's hubs use the Postgres backplane, from its diagnostics endpoint.
/// </summary>
/// <param name="TestHubUsesBackplane">Whether the test hub's lifetime manager is the Postgres one.</param>
/// <param name="SecondHubUsesBackplane">Whether the second hub's lifetime manager is the Postgres one.</param>
public sealed record HubDiagnostics(bool TestHubUsesBackplane, bool SecondHubUsesBackplane);
