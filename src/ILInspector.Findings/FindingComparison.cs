using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ILInspector.Findings;

/// <summary>
/// The outcome of comparing two finding inspections. A completed comparison carries the alignment;
/// a failed comparison carries only the inspections that prevented matching from running.
/// </summary>
[Union]
public sealed record FindingComparison<T> where T : notnull
{
    public FindingComparison(Complete value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public FindingComparison(Failed value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public object Value { get; }

    public FindingInspection<T> OldInspection => this switch
    {
        Complete complete => complete.OldInspection,
        Failed failed => failed.OldInspection,
    };

    public FindingInspection<T> NewInspection => this switch
    {
        Complete complete => complete.NewInspection,
        Failed failed => failed.NewInspection,
    };

    public string? Failure => this switch
    {
        Complete => null,
        Failed failed => failed.Failure,
    };

    public bool IsExact => this is Complete { IsExact: true };

    /// <summary>
    /// Matching ran. An empty match is valid evidence of a trivial alignment, including
    /// <c>Absent</c> versus <c>Absent</c>.
    /// </summary>
    public sealed record Complete
    {
        public Complete(
            ImmutableArray<PairFinding<T>> pairs,
            FindingMatch match,
            FindingInspection<T> oldInspection,
            FindingInspection<T> newInspection)
        {
            if (pairs.IsDefault)
                throw new ArgumentException("Pairs must be initialized.", nameof(pairs));
            ArgumentNullException.ThrowIfNull(match);
            if (match.Edges.IsDefault || match.MoveCandidates.IsDefault)
                throw new ArgumentException("Match arrays must be initialized.", nameof(match));
            ArgumentNullException.ThrowIfNull(oldInspection);
            ArgumentNullException.ThrowIfNull(newInspection);
            if (oldInspection is FindingInspection<T>.Failed)
                throw new ArgumentException("A completed comparison cannot contain a failed old inspection.", nameof(oldInspection));
            if (newInspection is FindingInspection<T>.Failed)
                throw new ArgumentException("A completed comparison cannot contain a failed new inspection.", nameof(newInspection));

            Pairs = pairs;
            Match = match;
            OldInspection = oldInspection;
            NewInspection = newInspection;
        }

        public ImmutableArray<PairFinding<T>> Pairs { get; }
        public FindingMatch Match { get; }
        public FindingInspection<T> OldInspection { get; }
        public FindingInspection<T> NewInspection { get; }
        public ImmutableArray<Finding<T>> OldAtoms => InspectionAtoms(OldInspection);
        public ImmutableArray<Finding<T>> NewAtoms => InspectionAtoms(NewInspection);

        public bool IsExact =>
            SameInspectionState(OldInspection, NewInspection)
            && FindingEquivalence.Exact.IsEquivalent(Pairs);
    }

    /// <summary>Matching never ran because at least one inspection failed.</summary>
    public sealed record Failed
    {
        public Failed(
            FindingInspection<T> oldInspection,
            FindingInspection<T> newInspection)
        {
            ArgumentNullException.ThrowIfNull(oldInspection);
            ArgumentNullException.ThrowIfNull(newInspection);
            if (oldInspection is not FindingInspection<T>.Failed
                && newInspection is not FindingInspection<T>.Failed)
            {
                throw new ArgumentException(
                    "A failed comparison requires at least one failed inspection.",
                    nameof(oldInspection));
            }

            OldInspection = oldInspection;
            NewInspection = newInspection;
        }

        public FindingInspection<T> OldInspection { get; }
        public FindingInspection<T> NewInspection { get; }

        public string Failure
        {
            get
            {
                var failures = new List<string>(2);
                if (OldInspection is FindingInspection<T>.Failed oldFailed)
                    failures.Add($"old: {oldFailed.Error.Detail}");
                if (NewInspection is FindingInspection<T>.Failed newFailed)
                    failures.Add($"new: {newFailed.Error.Detail}");
                return string.Join("; ", failures);
            }
        }
    }

    static ImmutableArray<Finding<T>> InspectionAtoms(FindingInspection<T> inspection)
        => inspection switch
        {
            FindingInspection<T>.Complete complete => complete.Findings,
            FindingInspection<T>.Absent => [],
            FindingInspection<T>.Failed => throw new InvalidOperationException(
                "A completed comparison cannot contain a failed inspection."),
        };

    static bool SameInspectionState(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection)
        => (oldInspection, newInspection) switch
        {
            (FindingInspection<T>.Complete, FindingInspection<T>.Complete) => true,
            (FindingInspection<T>.Absent, FindingInspection<T>.Absent) => true,
            _ => false,
        };
}
