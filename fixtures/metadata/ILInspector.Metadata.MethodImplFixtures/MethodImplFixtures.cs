using ILInspector.Metadata.MethodImplContracts;

namespace ILInspector.Metadata.MethodImplFixtures;

public interface IOpen<T>
{
    T Echo(T value);
}

public interface IOuter<T>
{
    public interface IInner<U>
    {
        U Convert(T left, U right);
    }
}

public sealed class ExternalImplementation : IExternalContract
{
    void IExternalContract.External()
    {
    }
}

public sealed class OpenImplementation<T> : IOpen<T>
{
    T IOpen<T>.Echo(T value) => value;
}

public sealed class ConstructedImplementation<T> : IOpen<List<T>>
{
    List<T> IOpen<List<T>>.Echo(List<T> value) => value;
}

public sealed class NestedImplementation<T, U> : IOuter<T>.IInner<U>
{
    U IOuter<T>.IInner<U>.Convert(T left, U right) => right;
}

public unsafe sealed class FunctionPointerImplementation :
    IFunctionPointerContract
{
    void IFunctionPointerContract.M<T>(delegate*<T, void> callback)
    {
    }
}
