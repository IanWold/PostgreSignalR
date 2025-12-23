using PostgreSignalR.IntegrationTests.Abstractions;

namespace PostgreSignalR.IntegrationTests;

public class BaseTest(ContainerFixture fixture)
{
    internal TestServer Server1 => fixture.SharedServer1!;
    internal TestServer Server2 => fixture.SharedServer2!;

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
}
