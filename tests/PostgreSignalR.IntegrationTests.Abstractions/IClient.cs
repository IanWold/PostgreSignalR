namespace PostgreSignalR.IntegrationTests.Abstractions;

public interface IClient
{
    Task Message(string message);
    Task<string> EchoBack(string message);
    Task MessageSimpleObject(SimpleObject obj);
    Task MessageComplexObject(ComplexObject obj);
}
