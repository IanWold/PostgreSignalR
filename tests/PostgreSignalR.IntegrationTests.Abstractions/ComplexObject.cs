using System.Linq;

namespace PostgreSignalR.IntegrationTests.Abstractions;

public class ComplexObject
{
    public required SimpleObject SimpleObjectProperty { get; set; }
    public required List<SimpleObject> SimpleObjectsProperty { get; set; }
    public required Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty { get; set; }

    public override bool Equals(object o) =>
        o is ComplexObject c
        && c.SimpleObjectProperty == SimpleObjectProperty
        && c.SimpleObjectsProperty.SequenceEqual(SimpleObjectsProperty)
        && c.SimpleObjectsDictionaryProperty.OrderBy(d => d).SequenceEqual(SimpleObjectsDictionaryProperty.OrderBy(d => d));

    public override int GetHashCode() =>
        HashCode.Combine(SimpleObjectProperty, SimpleObjectsProperty, SimpleObjectsDictionaryProperty);
}