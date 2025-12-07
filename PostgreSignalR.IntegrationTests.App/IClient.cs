namespace PostgreSignalR.IntegrationTests.App;

public interface IClient
{
    Task Receive(string message);
}
