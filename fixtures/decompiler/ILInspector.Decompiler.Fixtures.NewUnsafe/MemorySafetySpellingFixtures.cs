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

    public static int CallPointerNoneMethod(int* value)
    {
        unsafe
        {
            return PointerNoneMethod(value);
        }
    }

    public static int CallPointerFreeUnsafeMethod()
    {
        unsafe
        {
            return PointerFreeUnsafeMethod();
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

public sealed class GenericCallerContractFixture<T>
{
    public T ContractChoice(T value) => value;

    public unsafe T ContractChoice<TMarker>(T value) => value;
}

public static class GenericCallerContractCalls
{
    public static int CallConstructedInstance(
        GenericCallerContractFixture<int> fixture)
    {
        unsafe
        {
            return fixture.ContractChoice<string>(42);
        }
    }
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

public enum MemorySafetyExtensionEnum
{
    Value,
}

public delegate void MemorySafetyExtensionDelegate();

public interface IMemorySafetyExtensionInterface
{
}

public abstract class MemorySafetyAbstractFixture
{
    public abstract int Read();
}

public sealed class MemorySafetyExtensionClass
{
}

public struct MemorySafetyExtensionStruct
{
}

public static class MemorySafetyReceiverExtensions
{
    public static unsafe int Examine(
        this MemorySafetyExtensionEnum value) => 42;

    public static unsafe int Examine(
        this MemorySafetyExtensionDelegate value) => 42;

    [DllImport("__dotnet_inspect_memory_safety_extension_fixture__")]
    public static safe extern int Examine(
        this IMemorySafetyExtensionInterface value);

    public static unsafe int Examine(
        this MemorySafetyExtensionClass value) => 42;

    public static unsafe int Examine(
        this MemorySafetyExtensionStruct value) => 42;
}
