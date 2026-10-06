namespace QuerySpace.Rows;

public sealed class RowSequenceKey :
    IEquatable<RowSequenceKey>
{
    private RowSequenceKey(int value)
    {
        Value = value;
    }

    public int Value { get; }

    public static RowSequenceKey Create(int value) =>
        new(value);

    public bool Equals(RowSequenceKey? other) =>
        other is not null && Value == other.Value;

    public override bool Equals(object? obj) =>
        obj is RowSequenceKey other && Equals(other);

    public override int GetHashCode() =>
        Value;
}
