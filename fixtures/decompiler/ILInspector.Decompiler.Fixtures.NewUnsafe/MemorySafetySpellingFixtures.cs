using System.Runtime.InteropServices;

namespace ILInspector.Decompiler.Fixtures.NewUnsafe;

[StructLayout(LayoutKind.Sequential)]
public sealed class MemorySafetySpellingFixture
{
    public unsafe MemorySafetySpellingFixture()
    {
    }

    public static unsafe int PointerFreeUnsafeMethod() => 42;

    public static int PointerNoneMethod(int* value)
    {
        unsafe
        {
            return *value;
        }
    }

    public int* PointerNoneField;

    public unsafe int UnsafeField;

    public int NormalField;

    public static int NormalMethod() => 42;

    [DllImport("__dotnet_inspect_memory_safety_fixture__")]
    public static safe extern int SafeExtern();
}

[StructLayout(LayoutKind.Explicit, Pack = 2, Size = 16)]
public struct MemorySafetyExplicitLayoutFixture
{
    [FieldOffset(0)]
    public safe int SafeInstanceField;

    [FieldOffset(4)]
    public unsafe int UnsafeInstanceField;

    public static int StaticField;
}

public interface IMemorySafetyAccessorContract
{
    unsafe int Value { get; }
}

public sealed class MemorySafetyExplicitAccessorFixture
    : IMemorySafetyAccessorContract
{
    unsafe int IMemorySafetyAccessorContract.Value => 42;
}
