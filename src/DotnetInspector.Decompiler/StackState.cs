using System.Collections.Immutable;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Immutable representation of the IL evaluation stack at a point in the method.
/// Each element is a <see cref="StackValue"/> tracking the type category.
/// </summary>
public sealed class StackState
{
    public static readonly StackState Empty = new([]);

    readonly ImmutableArray<StackValue> _values;

    StackState(ImmutableArray<StackValue> values) => _values = values;

    public int Height => _values.Length;
    public bool IsEmpty => _values.IsEmpty;
    public ImmutableArray<StackValue> Values => _values;

    /// <summary>Peek at the top of the stack (last element).</summary>
    public StackValue Peek() => _values[^1];

    /// <summary>Peek at the i-th element from the top (0 = top).</summary>
    public StackValue Peek(int depth) => _values[^(depth + 1)];

    /// <summary>Push a value, returning a new state.</summary>
    public StackState Push(StackValue value) => new(_values.Add(value));

    /// <summary>Pop one value, returning the new state and the popped value.</summary>
    public StackState Pop(out StackValue value)
    {
        value = _values[^1];
        return new(_values.RemoveAt(_values.Length - 1));
    }

    /// <summary>Pop <paramref name="count"/> values, returning the new state.</summary>
    public StackState Pop(int count)
    {
        if (count == 0) return this;
        return new(_values.RemoveRange(_values.Length - count, count));
    }

    /// <summary>
    /// Merge this state with another at a control flow join point.
    /// Per ECMA-335 III.1.8.1.3, both states must have the same height.
    /// Returns the merged state and whether any slot changed.
    /// </summary>
    public (StackState Merged, bool Changed) Merge(StackState other)
    {
        if (Height != other.Height)
            return (this, false);

        if (Height == 0)
            return (Empty, false);

        var builder = _values.ToBuilder();
        bool changed = false;

        for (int i = 0; i < builder.Count; i++)
        {
            var merged = StackValue.Merge(builder[i], other._values[i]);
            if (merged != builder[i])
            {
                builder[i] = merged;
                changed = true;
            }
        }

        return changed
            ? (new StackState(builder.ToImmutable()), true)
            : (this, false);
    }

    public override string ToString() =>
        Height == 0 ? "[]" : $"[{string.Join(", ", _values)}]";
}
