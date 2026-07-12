using System.Collections.Immutable;

namespace ILInspector.Findings;

internal static class FindingValueEquality
{
    public static bool SequenceEqual<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
    {
        if (left.IsDefault || right.IsDefault)
            return left.IsDefault && right.IsDefault;

        return left.SequenceEqual(right, EqualityComparer<T>.Default);
    }

    public static int SequenceHashCode<T>(ImmutableArray<T> values)
    {
        if (values.IsDefault)
            return 0;

        var hash = new HashCode();
        hash.Add(values.Length);
        foreach (var value in values)
            hash.Add(value, EqualityComparer<T>.Default);
        return hash.ToHashCode();
    }

    public static bool SetEquals<T>(
        ImmutableHashSet<T> left,
        ImmutableHashSet<T> right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left.Count != right.Count)
            return false;

        var comparer = EqualityComparer<T>.Default;
        return left.All(value => right.Contains(value, comparer));
    }

    public static int SetHashCode<T>(ImmutableHashSet<T> values)
    {
        var comparer = EqualityComparer<T>.Default;
        int xor = 0;
        int sum = 0;
        foreach (var value in values)
        {
            int elementHash = value is null ? 0 : comparer.GetHashCode(value);
            xor ^= elementHash;
            sum = unchecked(sum + elementHash);
        }

        return HashCode.Combine(values.Count, xor, sum);
    }
}
