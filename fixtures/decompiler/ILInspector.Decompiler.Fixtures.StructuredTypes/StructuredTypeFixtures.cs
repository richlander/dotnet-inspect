using System;

namespace ILInspector.Decompiler.Fixtures.StructuredTypes;

public interface IExplicitValue
{
    int Value { get; }
}

public sealed class StructuredSample : IExplicitValue
{
    private int _seed = 7;

    public StructuredSample()
    {
        Value = _seed;
    }

    public int Value { get; set; }

    int IExplicitValue.Value => Value;

    public event EventHandler? Changed;

    public int this[int index]
    {
        get => Value + index;
        set => Value = value - index;
    }

    public int Overload() => Value;

    public int Overload(int value) => Value + value;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class EmptyType;

public interface IBodylessType
{
    int Read();
}

public enum Choice : short
{
    None,
    One = 1,
}

public delegate int Converter(string value);

public sealed class Outer<T>
{
    public readonly struct Inner<U>
    {
        public T? OuterValue { get; init; }

        public U? InnerValue { get; init; }
    }
}

public sealed class InterfaceObligationOuter : IDisposable
{
    public void Dispose() { }

    public sealed class Inner;
}

public class ContainingRequiredBase
{
    public ContainingRequiredBase(int value) { }
}

public sealed class RequiredBaseOuter : ContainingRequiredBase
{
    public RequiredBaseOuter()
        : base(7) { }

    public sealed class Inner;
}

public interface InterfaceContext : IDisposable
{
    public sealed class Inner
    {
        public int Read() => 7;
    }
}

public interface DefaultInterface
{
    private int Read() => 7;

    public int Value => Read();
}

[System.ComponentModel.Description("structured-frame")]
public sealed class MultipleConstructors
{
    private int _value = 7;

    public MultipleConstructors() { }

    public MultipleConstructors(int ignored) { }

    public int Read() => _value;
}

public sealed class CustomEvent
{
    private EventHandler? _handlers;

    public int Changes { get; private set; }

    public event EventHandler Changed
    {
        add { Changes++; _handlers += value; }
        remove { Changes--; _handlers -= value; }
    }
}

public sealed class LocalHelper
{
    public int Read(int value)
    {
        return Twice(value);
        static int Twice(int input) => input * 2;
    }
}

public sealed class CapturingLocalHelper
{
    public int Read(int value)
    {
        if (value == 0)
            return AddSquare(5);
        if (value == 1)
            return AddSquare(7);
        return AddSquare(9);

        int AddSquare(int input)
        {
            int sum = input + value;
            return sum * sum;
        }
    }
}

public sealed class BackingStorageInitializers
{
    private static readonly EventHandler? InitialHandler = null;

    public BackingStorageInitializers() { }

    public BackingStorageInitializers(int ignored) { }

    public int Value { get; } = 7;

    public int Number { get; init; } = 9;

    public event EventHandler? Changed = InitialHandler;
}

public sealed class InterleavedInitializers
{
    private static int _next;

    public int First = Next();

    public int Second { get; } = Next();

    public int Third = Next();

    private static int Next() => ++_next;
}

public static class ConstantField
{
    public const int Value = 7;

    public static int Read() => Value;
}

public sealed class RefReturnProperties
{
    private int _value;

    public ref int Value => ref _value;

    public ref readonly int ReadOnlyValue => ref _value;
}

public sealed class UnsafeAccessorContexts
{
    private EventHandler? _handlers;

    public unsafe int Value
    {
        get
        {
            int value = 7;
            return *(int*)(&value);
        }
    }

    public unsafe event EventHandler Changed
    {
        add
        {
            int local = 7;
            _ = *(int*)(&local);
            _handlers += value;
        }
        remove => _handlers -= value;
    }
}

public class RequiredBase
{
    public RequiredBase(int value) { }
}

public sealed class DerivedConstructorChain : RequiredBase
{
    public DerivedConstructorChain()
        : base(7) { }

    public int Read() => 7;
}
