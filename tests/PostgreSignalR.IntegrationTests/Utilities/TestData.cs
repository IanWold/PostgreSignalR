using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public abstract class TestData : IAsyncLifetime
{
    internal string GroupName { get; } = Guid.NewGuid().ToString();
    internal string ShortMessage { get; } = Guid.NewGuid().ToString();
    internal string LongMessage { get; } = new string('A', 10000);

    internal SimpleObject RandomSimpleObject { get; } = GetSimpleObject();
    internal ComplexObject RandomComplexObject { get; } = new()
    {
        SimpleObjectProperty = GetSimpleObject(),
        SimpleObjectsProperty = [ GetSimpleObject(), GetSimpleObject(), GetSimpleObject() ],
        SimpleObjectsDictionaryProperty = new()
        {
            ["a"] = GetSimpleObject(),
            ["b"] = GetSimpleObject(),
            ["c"] = GetSimpleObject()
        }
    };

    private static SimpleObject GetSimpleObject() => new(
        Random.Shared.Next(0, 100),
        Guid.NewGuid().ToString()
    );

    public virtual ValueTask InitializeAsync() =>
        default;

    public virtual async ValueTask DisposeAsync() =>
        await Task.Delay(TestTimeouts.DisconnectSettleDelay);
}
