namespace PostgreSignalR.IntegrationTests;

public record ClientMessage(string Key, object?[] Args)
{
    public T Arg<T>(int index = 0) => (T)Args[index]!;
}
