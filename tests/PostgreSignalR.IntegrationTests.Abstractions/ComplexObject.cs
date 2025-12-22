namespace PostgreSignalR.IntegrationTests.Abstractions;

public record ComplexObject(
    SimpleObject SimpleObjectProperty,
    IEnumerable<SimpleObject> SimpleObjectsProperty,
    Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty
);
