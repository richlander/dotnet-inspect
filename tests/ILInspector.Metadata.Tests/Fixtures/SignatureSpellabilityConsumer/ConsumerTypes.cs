using ILInspector.Metadata.Tests.SpellabilityReference;

namespace ILInspector.Metadata.Tests.SpellabilityConsumer;

public sealed class SignatureSpellabilityConsumerFixtures
{
    internal HiddenReferenceType HiddenField = new();
    internal VisibleReferenceType VisibleField = new();

    internal HiddenReferenceType HiddenProperty { get; set; } = new();
    internal VisibleReferenceType VisibleProperty { get; set; } = new();
    internal int this[HiddenReferenceType key] => 0;

    internal HiddenReferenceType HiddenMethod(HiddenReferenceType value) => value;
    internal VisibleReferenceType VisibleMethod(VisibleReferenceType value) => value;

    internal unsafe VisibleGeneric<VisibleReferenceType[]>[] ComplexMethod(
        VisibleValueType* pointer,
        delegate*<VisibleReferenceType, VisibleGeneric<VisibleReferenceType>> callback)
        => [];

    internal ConstructedVisibleString LocalMethod(ConstructedVisibleString value)
        => value;

    internal T GenericMethod<T>(T value) => value;
}

public class ConstructedVisibleString : VisibleGeneric<string>
{
}

public abstract class CompilerProducedConstraintHost<T>
    where T : ConstructedVisibleString
{
}
