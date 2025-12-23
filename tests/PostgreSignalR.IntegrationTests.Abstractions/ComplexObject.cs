using System.Linq;

namespace PostgreSignalR.IntegrationTests.Abstractions;

public record ComplexObject(
    SimpleObject SimpleObjectProperty,
    List<SimpleObject> SimpleObjectsProperty,
    Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty
)
{
    public override bool Equals(object o) =>
        o is ComplexObject c
        && c.SimpleObjectProperty == SimpleObjectProperty
        && c.SimpleObjectsProperty.SequenceEqual(SimpleObjectsProperty)
        && c.SimpleObjectsDictionaryProperty.OrderBy(d => d).SequenceEqual(SimpleObjectsDictionaryProperty.OrderBy(d => d));

    public override int GetHashCode() =>
        HashCode.Combine(SimpleObjectProperty, SimpleObjectsProperty, SimpleObjectsDictionaryProperty);
}