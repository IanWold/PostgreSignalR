namespace PostgreSignalR.IntegrationTests.Abstractions;

public interface IClient
{
    Task Message(string message);
    Task<string> EchoBack(string message);
    Task<string> EchoBackWithError(string message);
    Task<string> EchoBackSlow(string message, int delayMs);
    Task MessageSimpleObject(SimpleObject obj);
    Task MessageComplexObject(ComplexObject obj);
}
