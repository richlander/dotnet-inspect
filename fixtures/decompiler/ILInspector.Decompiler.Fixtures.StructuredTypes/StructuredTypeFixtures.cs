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
