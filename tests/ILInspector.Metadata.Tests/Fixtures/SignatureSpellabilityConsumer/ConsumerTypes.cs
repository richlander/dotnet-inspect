using ILInspector.Metadata.Tests.SpellabilityReference;

namespace ILInspector.Metadata.Tests.SpellabilityConsumer;

public sealed class SignatureSpellabilityConsumerFixtures
{
    internal HiddenReferenceType HiddenField = new();
    internal VisibleReferenceType VisibleField = new();

    internal HiddenReferenceType HiddenProperty { get; set; } = new();
    internal VisibleReferenceType VisibleProperty { get; set; } = new();

    internal HiddenReferenceType HiddenMethod(HiddenReferenceType value) => value;
    internal VisibleReferenceType VisibleMethod(VisibleReferenceType value) => value;
}
