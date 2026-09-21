namespace SourceDiffFixture;

public class FieldGetter
{
    public int Count => field + 1;
    public int AutomaticCount { get; }
    public int ChangingCount => ++field;
    public int WriteOnly { set { } }
}

public readonly struct InitializedFieldGetter(int value)
{
    public int Count { get => field + 1; } = value;
}

public readonly struct CalculatedFieldGetter(int value)
{
    public int Count { get => field + 1; } = value + 1;
}
