namespace ILInspector.Decompiler.Tests;

public sealed class RefKindBox<T>
{
    T _value = default!;

    public bool TryGet(out T value)
    {
        value = _value;
        return true;
    }

    public void Put(in T value) => _value = value;
}
