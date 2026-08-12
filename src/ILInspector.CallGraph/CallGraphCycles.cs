using System.Collections.Immutable;

namespace ILInspector.CallGraph;

/// <summary>Limits reached while enumerating focus-member call cycles.</summary>
[Flags]
public enum CallGraphCycleSearchLimit
{
    None = 0,
    WitnessBudget = 1,
    PathBudget = 2,
}

/// <summary>Cost bounds for one projection-only cycle search.</summary>
public sealed record CallGraphCycleSearchOptions
{
    public int MaxWitnesses { get; init; } = 16;
    public int MaxPaths { get; init; } = 4096;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxWitnesses, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxPaths, 1);
    }
}

/// <summary>
/// One simple directed cycle that starts and ends at the selected member.
/// Edge rows are ordered traversal steps and remain in the projection's stable
/// row-number domain.
/// </summary>
public sealed class CallGraphCycleWitness
    : IEquatable<CallGraphCycleWitness>
{
    public CallGraphCycleWitness(ImmutableArray<int> edgeRows)
    {
        if (edgeRows.IsDefaultOrEmpty)
            throw new ArgumentException(
                "A cycle witness requires at least one edge row.",
                nameof(edgeRows));
        if (edgeRows.Any(static row => row <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(edgeRows),
                "Cycle witness edge rows must be positive.");
        }
        EdgeRows = edgeRows;
    }

    public ImmutableArray<int> EdgeRows { get; }

    public bool IsDirect => EdgeRows.Length == 1;

    public bool Equals(CallGraphCycleWitness? other) =>
        other is not null
        && EdgeRows.SequenceEqual(other.EdgeRows);

    public override bool Equals(object? obj) =>
        obj is CallGraphCycleWitness other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(EdgeRows.Length);
        foreach (int row in EdgeRows)
            hash.Add(row);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Projection-local cycle witnesses plus any enumeration limit reached.
/// A positive witness remains valid when <see cref="Limits"/> is nonzero;
/// only absence and exhaustiveness are bounded.
/// </summary>
public sealed record CallGraphCycleSearchResult(
    ImmutableArray<CallGraphCycleWitness> Witnesses,
    CallGraphCycleSearchLimit Limits)
{
    public bool IsComplete => Limits == CallGraphCycleSearchLimit.None;
}

public sealed partial class CallGraphProjection
{
    readonly record struct CyclePath(
        int Node,
        ImmutableArray<int> Nodes,
        ImmutableArray<int> EdgeRows);

    /// <summary>
    /// Enumerates simple cycles containing <see cref="Focus"/> using only this
    /// materialized projection. Breadth-first traversal makes witnesses stable:
    /// shortest first, then lexicographic stable edge-row order.
    /// </summary>
    public CallGraphCycleSearchResult FindFocusCycles(
        CallGraphCycleSearchOptions? options = null)
    {
        options ??= new CallGraphCycleSearchOptions();
        options.Validate();

        ImmutableArray<CallGraphRow>[] outgoing =
        [
            .. Enumerable.Range(0, Nodes.Length)
                .Select(node =>
                    Rows.Where(row => row.Edge.From == node)
                        .ToImmutableArray()),
        ];
        var queue = new Queue<CyclePath>();
        queue.Enqueue(
            new CyclePath(
                Focus.Id,
                [Focus.Id],
                []));
        var witnesses =
            ImmutableArray.CreateBuilder<CallGraphCycleWitness>();
        int generatedPaths = 1;
        bool pathLimited = false;

        while (queue.Count > 0)
        {
            CyclePath path = queue.Dequeue();
            foreach (CallGraphRow row in outgoing[path.Node])
            {
                ImmutableArray<int> edgeRows =
                    path.EdgeRows.Add(row.Number);
                if (row.Edge.To == Focus.Id)
                {
                    if (witnesses.Count == options.MaxWitnesses)
                    {
                        return Result(
                            witnesses,
                            CallGraphCycleSearchLimit.WitnessBudget
                                | (pathLimited
                                    ? CallGraphCycleSearchLimit.PathBudget
                                    : CallGraphCycleSearchLimit.None));
                    }

                    witnesses.Add(
                        new CallGraphCycleWitness(edgeRows));
                    continue;
                }

                if (path.Nodes.Contains(row.Edge.To))
                    continue;

                if (generatedPaths == options.MaxPaths)
                {
                    pathLimited = true;
                    continue;
                }

                queue.Enqueue(
                    new CyclePath(
                        row.Edge.To,
                        path.Nodes.Add(row.Edge.To),
                        edgeRows));
                generatedPaths++;
            }
        }

        return Result(
            witnesses,
            pathLimited
                ? CallGraphCycleSearchLimit.PathBudget
                : CallGraphCycleSearchLimit.None);
    }

    static CallGraphCycleSearchResult Result(
        ImmutableArray<CallGraphCycleWitness>.Builder witnesses,
        CallGraphCycleSearchLimit limits) =>
        new(witnesses.ToImmutable(), limits);
}
