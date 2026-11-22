namespace PostgreSignalR.IntegrationTests.App;

/// <summary>
/// A second hub with the same methods as <see cref="TestHub"/>, for tests that choose which hubs use the backplane.
/// </summary>
public sealed class SecondHub : TestHub;
