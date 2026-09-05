namespace ILInspector.Metadata.MemorySafetyFixtures;

public static class MemorySafetyFixtures
{
    public static unsafe int MethodContract() => 42;

    public static void PointerOnly(int* value)
    {
        unsafe
        {
            _ = *value;
        }
    }

    public static unsafe int PropertyContract { get; set; }

    public static unsafe event Action? EventContract;

    public static unsafe int ExtensionContract(
        this MemorySafetyDeclarationFixtures value) => 42;

    public static void Raise()
    {
        unsafe
        {
            EventContract?.Invoke();
        }
    }
}

public class MemorySafetyDeclarationFixtures
{
    public unsafe MemorySafetyDeclarationFixtures() { }

    public int* PointerField;
    public unsafe int ContractField;
    public int Property { get; set; }
    public static int StaticProperty { get; set; }
    public int CustomProperty { get => 1; set { } }
    public event Action? Event;
    public static event Action? StaticEvent;
    public event Action CustomEvent { add { } remove { } }

    public void Raise()
    {
        Event?.Invoke();
        StaticEvent?.Invoke();
    }
}

[System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Explicit)]
public struct MemorySafetyExplicitLayout
{
    [System.Runtime.InteropServices.FieldOffset(0)]
    public safe int Field;
}

public class MemorySafetyGenericStorage<T>
{
    public T Property { get; set; } = default!;
    public T[] ArrayProperty { get; set; } = [];
    public int* PointerProperty { get; set; }
    public delegate*<int, int> FunctionPointerProperty { get; set; }
    public event Action<T>? Event;

    public void Raise(T value) => Event?.Invoke(value);
}
