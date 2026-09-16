using System.Runtime.InteropServices;

namespace MetadataCorrespondenceFixture;

public sealed class Container<T>
    where T : class, new()
{
    public T StableField = new();
    public int ChangedField;
    public int StaticFieldChanged;

    public event Action<T>? StableEvent;
    public event Action<int>? ChangedEvent;
    public event Action? StaticEventChanged;

    public string StableProperty { get; set; } = "";
    public string AccessorChanged { get; set; } = "";
    public int ChangedProperty { get; set; }
    public int StaticPropertyChanged { get; set; }

    public static T StableMethod<U>(
        T value,
        [In, Out] ref U item)
        where U : struct
        => value;

    public TValue? NullableMethod<TValue>(
        string value,
        Container<T>? options)
        where TValue : class
        => default;

    public int ReturnChanged(string value) => value.Length;

    public int BodyOnly(int originalName = 1) => originalName + 1;

    public int StaticMethodChanged(int value) => value;

    public U MethodConstraintChanged<U>(U value)
        where U : struct
        => value;

    public void ParameterFlagsChanged([In, Out] ref int value)
        => value++;

    public int[,] Array(int[,] value) => value;

    public int[,] ArrayChanged(int[,] value) => value;

    public unsafe delegate* unmanaged[Cdecl]<int, int> FunctionPointer(
        delegate* unmanaged[Cdecl]<int, int> callback)
        => callback;

    public unsafe delegate* unmanaged[Cdecl]<int, int> FunctionPointerChanged(
        delegate* unmanaged[Cdecl]<int, int> callback)
        => callback;

    public void Raise(T value)
    {
        StableEvent?.Invoke(value);
        _ = ChangedEvent;
        _ = StaticEventChanged;
    }
}

public static class VarArgContainer
{
    public static void VarArg(int value, __arglist)
        => _ = value;
}

public sealed class ConstraintChanged<T>
    where T : class;

public sealed class Outer<T>
    where T : class
{
    public sealed class Inner<U>
        where U : struct
    {
        public T? Nested(U value) => default;
    }
}
