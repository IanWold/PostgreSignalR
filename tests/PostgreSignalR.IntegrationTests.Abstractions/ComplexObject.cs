using System.Linq;

namespace PostgreSignalR.IntegrationTests.Abstractions;

public class ComplexObject
{
    SimpleObject SimpleObjectProperty { get; set; }
    List<SimpleObject> SimpleObjectsProperty { get; set; }
    Dictionary<string, SimpleObject> SimpleObjectsDictionaryProperty { get; set; }

    public override bool Equals(object o) =>
        o is ComplexObject c
        && c.SimpleObjectProperty == SimpleObjectProperty
        && c.SimpleObjectsProperty.SequenceEqual(SimpleObjectsProperty)
        && c.SimpleObjectsDictionaryProperty.OrderBy(d => d).SequenceEqual(SimpleObjectsDictionaryProperty.OrderBy(d => d));

    public override int GetHashCode() =>
        HashCode.Combine(SimpleObjectProperty, SimpleObjectsProperty, SimpleObjectsDictionaryProperty);
}