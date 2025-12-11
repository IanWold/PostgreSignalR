namespace PostgreSignalR.IntegrationTests.Abstractions;

public interface IClient
{
    Task Receive(string message);
    Task ReceiveCaller(string message);
    Task ReceiveOthers(string message);
    Task ReceiveGroup(string message);
    Task ReceiveGroupRemoval(string message);
    Task ReceiveConnection(string message);
    Task ReceiveUser(string message);
    Task<string> EchoBack(string message);
}
