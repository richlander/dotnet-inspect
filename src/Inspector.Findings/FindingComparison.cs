using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Inspector.Findings;

/// <summary>The non-failed state of one finding inspection endpoint.</summary>
public enum FindingInspectionState
{
    Complete,
    SubjectAbsent,
    NoApplicableInput,
}

/// <summary>
/// The typed endpoint topology retained by a completed finding comparison.
/// </summary>
public sealed record FindingInspectionTransition
{
    internal FindingInspectionTransition(
        FindingInspectionState oldState,
        FindingInspectionState newState)
    {
        Old = Validate(oldState, nameof(oldState));
        New = Validate(newState, nameof(newState));
    }

    public FindingInspectionState Old { get; }
    public FindingInspectionState New { get; }
    public bool IsSameTopology => Old == New;

    internal static FindingInspectionTransition Create<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection)
        where T : notnull
        => new(State(oldInspection), State(newInspection));

    static FindingInspectionState State<T>(FindingInspection<T> inspection)
        where T : notnull
        => inspection switch
        {
            FindingInspection<T>.Complete => FindingInspectionState.Complete,
            FindingInspection<T>.Absent => ((FindingInspection<T>.Absent)inspection.Value!).Kind switch
            {
                FindingInspectionAbsenceKind.SubjectAbsent => FindingInspectionState.SubjectAbsent,
                FindingInspectionAbsenceKind.NoApplicableInput => FindingInspectionState.NoApplicableInput,
                _ => throw new InvalidOperationException("The inspection has an unknown absence kind."),
            },
            FindingInspection<T>.Failed => throw new ArgumentException(
                "A failed inspection has no completed inspection state.",
                nameof(inspection)),
        };

    static FindingInspectionState Validate(FindingInspectionState state, string parameterName)
        => state switch
        {
            FindingInspectionState.Complete => state,
            FindingInspectionState.SubjectAbsent => state,
            FindingInspectionState.NoApplicableInput => state,
            _ => throw new ArgumentOutOfRangeException(parameterName),
        };
}

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
        Complete => ((Complete)Value).OldInspection,
        Failed => ((Failed)Value).OldInspection,
    };

    public FindingInspection<T> NewInspection => this switch
    {
        Complete => ((Complete)Value).NewInspection,
        Failed => ((Failed)Value).NewInspection,
    };

    public string? Failure => this switch
    {
        Complete => null,
        Failed => ((Failed)Value).Failure,
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
            Complete => (Complete)Value,
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
    /// Narrows a completed producer comparison to the pair that carries one
    /// exact source identity while preserving producer classification and
    /// non-exact match provenance.
    /// </summary>
    public FindingComparison<T> Focus(FindingCorrelationKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (Value is Failed)
        {
            return FindingComparison.Compare(
                FocusInspection(OldInspection, key),
                FocusInspection(NewInspection, key));
        }

        var complete = (Complete)Value;
        ImmutableArray<PairFinding<T>> pairs =
        [
            .. complete.Pairs.Where(pair =>
                (((IPairFinding)pair).Old is Finding<T> oldFinding
                    && key.Matches(oldFinding))
                || (((IPairFinding)pair).New is Finding<T> newFinding
                    && key.Matches(newFinding))),
        ];
        FindingInspection<T> oldInspection =
            FocusInspection(complete.OldInspection, pairs, old: true);
        FindingInspection<T> newInspection =
            FocusInspection(complete.NewInspection, pairs, old: false);
        return new Complete(
            pairs,
            FocusMatch(
                complete.Match,
                complete.OldAtoms,
                complete.NewAtoms,
                FindingComparison.InspectionAtoms(oldInspection),
                FindingComparison.InspectionAtoms(newInspection)),
            oldInspection,
            newInspection);
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
            if (match.Edges.IsDefault
                || match.MoveCandidates.IsDefault
                || match.SoftCandidates.IsDefault)
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
            Transition = FindingInspectionTransition.Create(oldInspection, newInspection);
        }

        public ImmutableArray<PairFinding<T>> Pairs { get; }
        public FindingMatch Match { get; }
        public FindingInspection<T> OldInspection { get; }
        public FindingInspection<T> NewInspection { get; }
        public FindingInspectionTransition Transition { get; }
        public ImmutableArray<Finding<T>> OldAtoms => FindingComparison.InspectionAtoms(OldInspection);
        public ImmutableArray<Finding<T>> NewAtoms => FindingComparison.InspectionAtoms(NewInspection);

        public bool Equals(Complete? other)
            => other is not null
                && FindingValueEquality.SequenceEqual(Pairs, other.Pairs)
                && Match == other.Match
                && OldInspection == other.OldInspection
                && NewInspection == other.NewInspection;

        public override int GetHashCode()
            => HashCode.Combine(
                FindingValueEquality.SequenceHashCode(Pairs),
                Match,
                OldInspection,
                NewInspection);

        public bool IsExact =>
            Transition.IsSameTopology
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
                if (OldInspection.Value is FindingInspection<T>.Failed oldFailed)
                    failures.Add($"old: {oldFailed.Error.Reason}");
                if (NewInspection.Value is FindingInspection<T>.Failed newFailed)
                    failures.Add($"new: {newFailed.Error.Reason}");
                return string.Join("; ", failures);
            }
        }
    }

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
            if (original[i].Value is IMatchedPairFinding { Match: not null } matchedBefore
                && (transformed[i].Value is not IMatchedPairFinding matchedAfter
                    || matchedAfter.Match != matchedBefore.Match))
            {
                throw new ArgumentException(
                    $"Transformed pair {i} must preserve its match provenance.",
                    nameof(transformed));
            }
        }
    }

    static FindingInspection<T> FocusInspection(
        FindingInspection<T> inspection,
        FindingCorrelationKey key) =>
        inspection.Value switch
        {
            FindingInspection<T>.Complete complete =>
                new FindingInspection<T>.Complete(
                [
                    .. complete.Findings.Where(key.Matches),
                ]),
            FindingInspection<T>.Absent absent => absent,
            FindingInspection<T>.Failed failed => failed,
            _ => throw new InvalidOperationException(
                "Finding inspection returned an unknown outcome."),
        };

    static FindingInspection<T> FocusInspection(
        FindingInspection<T> inspection,
        ImmutableArray<PairFinding<T>> pairs,
        bool old) =>
        inspection.Value switch
        {
            FindingInspection<T>.Complete =>
                new FindingInspection<T>.Complete(
                [
                    .. pairs
                        .Select(pair => old
                            ? ((IPairFinding)pair).Old
                            : ((IPairFinding)pair).New)
                        .OfType<Finding<T>>(),
                ]),
            FindingInspection<T>.Absent absent => absent,
            FindingInspection<T>.Failed failed => failed,
            _ => throw new InvalidOperationException(
                "Finding inspection returned an unknown outcome."),
        };

    static FindingMatch FocusMatch(
        FindingMatch match,
        ImmutableArray<Finding<T>> oldAtoms,
        ImmutableArray<Finding<T>> newAtoms,
        ImmutableArray<Finding<T>> focusedOldAtoms,
        ImmutableArray<Finding<T>> focusedNewAtoms)
    {
        int[] oldIndices = FocusedIndices(oldAtoms, focusedOldAtoms);
        int[] newIndices = FocusedIndices(newAtoms, focusedNewAtoms);

        return new FindingMatch(
        [
            .. match.Edges
                .Where(edge =>
                    IsFocused(edge.OldIndex, oldIndices)
                    && IsFocused(edge.NewIndex, newIndices))
                .Select(edge => edge with
                {
                    OldIndex = Remap(edge.OldIndex, oldIndices),
                    NewIndex = Remap(edge.NewIndex, newIndices),
                }),
        ],
        [
            .. match.MoveCandidates
                .Where(candidate =>
                    IsFocused(candidate.OldIndex, oldIndices)
                    && IsFocused(candidate.NewIndex, newIndices))
                .Select(candidate => candidate with
                {
                    OldIndex = Remap(candidate.OldIndex, oldIndices),
                    NewIndex = Remap(candidate.NewIndex, newIndices),
                }),
        ])
        {
            SoftCandidates =
            [
                .. match.SoftCandidates
                    .Where(candidate =>
                        IsFocused(candidate.OldIndex, oldIndices)
                        && IsFocused(candidate.NewIndex, newIndices))
                    .Select(candidate => candidate with
                    {
                        OldIndex = Remap(candidate.OldIndex, oldIndices),
                        NewIndex = Remap(candidate.NewIndex, newIndices),
                    }),
            ],
        };

        static int[] FocusedIndices(
            ImmutableArray<Finding<T>> atoms,
            ImmutableArray<Finding<T>> focusedAtoms)
        {
            var indices = new int[atoms.Length];
            Array.Fill(indices, -1);
            for (int sourceIndex = 0; sourceIndex < atoms.Length; sourceIndex++)
            {
                for (int focusedIndex = 0;
                    focusedIndex < focusedAtoms.Length;
                    focusedIndex++)
                {
                    if (ReferenceEquals(
                        atoms[sourceIndex],
                        focusedAtoms[focusedIndex]))
                    {
                        indices[sourceIndex] = focusedIndex;
                        break;
                    }
                }
            }

            return indices;
        }

        static bool IsFocused(int index, int[] indices)
            => index < 0 || indices[index] >= 0;

        static int Remap(int index, int[] indices)
            => index < 0 ? index : indices[index];
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
            FindingInspection<T>.Complete
                => ((FindingInspection<T>.Complete)inspection.Value!).Findings,
            FindingInspection<T>.Absent => [],
            FindingInspection<T>.Failed => throw new InvalidOperationException(
                "A failed inspection cannot be matched."),
        };
}
