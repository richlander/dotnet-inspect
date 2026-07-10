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
    /// Applies producer-owned classification to a completed comparison while preserving the
    /// alignment's atoms and order. Failed comparisons pass through unchanged.
    /// </summary>
    public FindingComparison<T> TransformPairs(
        Func<ImmutableArray<PairFinding<T>>, ImmutableArray<PairFinding<T>>> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        var complete = this switch
        {
            Complete value => value,
            Failed => null,
        };
        if (complete is null)
            return this;

        var transformed = transform(complete.Pairs);
        ValidateTransformation(complete.Pairs, transformed);
        return new Complete(
            transformed,
            complete.Match,
            complete.OldInspection,
            complete.NewInspection);
    }

    /// <summary>
    /// Matching ran. An empty match is valid evidence of a trivial alignment, including
    /// <c>Absent</c> versus <c>Absent</c>.
    /// </summary>
    public sealed record Complete
    {
        internal Complete(
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
        public ImmutableArray<Finding<T>> OldAtoms => FindingComparison.InspectionAtoms(OldInspection);
        public ImmutableArray<Finding<T>> NewAtoms => FindingComparison.InspectionAtoms(NewInspection);

        public bool IsExact =>
            SameInspectionState(OldInspection, NewInspection)
            && FindingEquivalence.Exact.IsEquivalent(Pairs);
    }

    /// <summary>Matching never ran because at least one inspection failed.</summary>
    public sealed record Failed
    {
        internal Failed(
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

    static bool SameInspectionState(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection)
        => (oldInspection, newInspection) switch
        {
            (FindingInspection<T>.Complete, FindingInspection<T>.Complete) => true,
            (FindingInspection<T>.Absent, FindingInspection<T>.Absent) => true,
            _ => false,
        };

    static void ValidateTransformation(
        ImmutableArray<PairFinding<T>> original,
        ImmutableArray<PairFinding<T>> transformed)
    {
        if (transformed.IsDefault)
            throw new ArgumentException("Transformed pairs must be initialized.", nameof(transformed));
        if (transformed.Length != original.Length)
        {
            throw new ArgumentException(
                "Pair classification must preserve the alignment length.",
                nameof(transformed));
        }

        for (int i = 0; i < original.Length; i++)
        {
            var before = (IPairFinding)original[i];
            var after = transformed[i] as IPairFinding
                ?? throw new ArgumentException(
                    $"Transformed pair {i} must not be null.",
                    nameof(transformed));
            if (!ReferenceEquals(before.Old, after.Old)
                || !ReferenceEquals(before.New, after.New))
            {
                throw new ArgumentException(
                    $"Transformed pair {i} must preserve its old and new atoms.",
                    nameof(transformed));
            }
        }
    }
}

/// <summary>Runs the shared inspect-to-match-to-fold comparison operation.</summary>
public static class FindingComparison
{
    public static FindingComparison<T> Compare<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection,
        FindingMatchOptions? matchOptions = null,
        int acceptanceThreshold = 100)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(oldInspection);
        ArgumentNullException.ThrowIfNull(newInspection);

        if (oldInspection is FindingInspection<T>.Failed
            || newInspection is FindingInspection<T>.Failed)
        {
            return new FindingComparison<T>.Failed(oldInspection, newInspection);
        }

        var oldAtoms = InspectionAtoms(oldInspection);
        var newAtoms = InspectionAtoms(newInspection);
        var match = FindingMatcher.Match(oldAtoms.Keys(), newAtoms.Keys(), matchOptions);
        var pairs = FindingFold.ToPairs(match, oldAtoms, newAtoms, acceptanceThreshold);
        return new FindingComparison<T>.Complete(
            pairs,
            match,
            oldInspection,
            newInspection);
    }

    internal static ImmutableArray<Finding<T>> InspectionAtoms<T>(
        FindingInspection<T> inspection)
        where T : notnull
        => inspection switch
        {
            FindingInspection<T>.Complete complete => complete.Findings,
            FindingInspection<T>.Absent => [],
            FindingInspection<T>.Failed => throw new InvalidOperationException(
                "A failed inspection cannot be matched."),
        };
}
