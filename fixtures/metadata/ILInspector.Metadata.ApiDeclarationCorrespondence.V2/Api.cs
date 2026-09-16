using System.Runtime.InteropServices;

namespace MetadataCorrespondenceFixture;

public sealed class Outer<T>
    where T : class
{
    public sealed class Inner<U>
        where U : struct
    {
        public T? Nested(U renamed) => default;
    }
}

public sealed class ConstraintChanged<T>
    where T : struct;

public sealed class Container<T>
    where T : class, new()
{
    public long ChangedField;
    public static int StaticFieldChanged;
    public T StableField = new();

    public event Action<long>? ChangedEvent;
    public event Action<T>? StableEvent;
    public static event Action? StaticEventChanged;

    public long ChangedProperty { get; set; }
    public string AccessorChanged { get; } = "";
    public static int StaticPropertyChanged { get; set; }
    public string StableProperty { get; set; } = "";

    public unsafe delegate* unmanaged[Cdecl]<int, int> FunctionPointer(
        delegate* unmanaged[Cdecl]<int, int> callback)
        => callback;

    public unsafe delegate* unmanaged[Stdcall]<int, int> FunctionPointerChanged(
        delegate* unmanaged[Stdcall]<int, int> callback)
        => callback;

    public int[,] Array(int[,] value) => value;

    public int[,,] ArrayChanged(int[,,] value) => value;

    public int BodyOnly(int renamed = 2) => renamed + 2;

    public TValue? NullableMethod<TValue>(
        string renamed,
        Container<T>? options)
        where TValue : class
        => default;

    public U MethodConstraintChanged<U>(U value)
        where U : class
        => value;

    public void ParameterFlagsChanged([In] ref int value)
        => value++;

    public long ReturnChanged(string value) => value.Length;

    public static int StaticMethodChanged(int value) => value;

    public static T StableMethod<U>(
        T value,
        [In, Out] ref U renamed)
        where U : struct
        => value;

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
