namespace PostgreSignalR.IntegrationTests.Abstractions;

public class ComplexObject
{
    public required SimpleObject SimpleObjectProperty { get; set; }
    public required IEnumerable<SimpleObject> SimpleObjectsProperty { get; set; }
    public required Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty { get; set; }
}
