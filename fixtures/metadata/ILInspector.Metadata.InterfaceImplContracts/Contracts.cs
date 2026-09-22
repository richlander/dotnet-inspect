namespace ILInspector.Metadata.InterfaceImplContracts
{
    public interface IConstructed<T>
    {
        T Echo(T value);
    }

    public interface IUnrelated;
}

namespace ILInspector.Metadata.InterfaceImplContracts.Collision
{
    public sealed class Argument;
}
