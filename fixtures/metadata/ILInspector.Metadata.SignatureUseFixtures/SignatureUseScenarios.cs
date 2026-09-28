namespace ILInspector.Metadata.SignatureUseFixtures;

public sealed class FixtureAnchor;

internal interface IContract<T>;

internal class Base<T>;

internal class LocalTarget;

internal sealed class InternalPeer;

internal struct LocalValue;

internal enum LocalEnum
{
    Value,
}

internal delegate void LocalDelegate(LocalTarget value);

internal sealed class LocalException : Exception;

[AttributeUsage(AttributeTargets.All)]
internal sealed class LocalAttribute : Attribute;

internal sealed unsafe class SignatureOwner<T> :
    Base<LocalTarget>,
    IContract<InternalPeer>
    where T : LocalTarget
{
    internal LocalTarget? Field = null;
    internal LocalTarget[] Array = [];
    internal LocalValue* ValuePointer = null;
    internal SignatureOwner<T>? Self = null;
    internal delegate*<LocalTarget, InternalPeer> Pointer = null;

    internal InternalPeer? this[LocalTarget key] => null;

    internal event Func<LocalTarget, InternalPeer>? Changed
    {
        add { }
        remove { }
    }

    internal Container<LocalTarget>? Method(
        InternalPeer peer,
        SignatureOwner<T> self) => null;

    internal U Generic<U>(U value)
        where U : LocalTarget =>
        value;

    internal ref LocalTarget ByRef(ref LocalTarget value) => ref value;

    internal sealed class Container<TValue>;
}
