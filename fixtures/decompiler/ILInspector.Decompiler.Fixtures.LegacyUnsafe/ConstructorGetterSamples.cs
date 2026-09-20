namespace ILInspector.Decompiler.Fixtures;

public readonly struct ConstructorGetterList<T>(IList<T> items)
{
    public IList<T> Items { get => field ?? Array.Empty<T>(); } = items;
}

public readonly struct ConstructorGetterCounter(int value)
{
    public int Value { get; } = value;
}

public struct ConstructorGetterComputed(int value)
{
    public int Value { get => field + 1; } = value;
}

public readonly struct ConstructorGetterParameterName
{
    public ConstructorGetterParameterName(int Value) => this.Value = Value;
    public int Value { get => field + 1; }
}

public readonly struct ConstructorGetterKeywordParameter
{
    public ConstructorGetterKeywordParameter(int @event) => Value = @event;
    public int Value { get; }
}

public readonly struct ConstructorGetterCalculated(int value)
{
    public int Value { get; } = value + 1;
}

public readonly struct ConstructorGetterConditional(int value)
{
    public int Value { get; } = value > 0 ? value : -value;
}

public readonly struct ConstructorGetterOverloads
{
    public ConstructorGetterOverloads() => Value = 7;
    public ConstructorGetterOverloads(int value) => Value = value;
    public int Value { get; }
}

public readonly struct ConstructorGetterOtherStorage(int value)
{
    public int Other { get; } = value;
    public int Value { get; } = value;
}

public class ConstructorGetterClass(int value)
{
    public int Value { get; } = value;
}

public readonly struct ConstructorGetterUnusedParameter
{
    public ConstructorGetterUnusedParameter(int ignored, int value) => Value = value;
    public int Value { get; }
}

public interface IConstructorGetterValue
{
    int Value { get; }
}

public readonly struct ConstructorGetterExplicitAutomatic(int value) : IConstructorGetterValue
{
    int IConstructorGetterValue.Value { get; } = value;
}

public readonly struct ConstructorGetterExplicitComputed(int value) : IConstructorGetterValue
{
    int IConstructorGetterValue.Value { get => field + 1; } = value;
}
