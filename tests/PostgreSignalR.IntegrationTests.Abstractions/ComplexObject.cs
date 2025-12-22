namespace PostgreSignalR.IntegrationTests.Abstractions;

public record ComplexObject(
    SimpleObject SimpleObjectProperty,
    List<SimpleObject> SimpleObjectsProperty,
    Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty
);
