using System.Collections.Immutable;

namespace ILInspector.Findings;

/// <summary>The producer's content assertion for one correspondence.</summary>
public enum AnalysisDiffContentKind
{
    Unclassified,
    Unchanged,
    Changed,
}

/// <summary>The producer's placement assertion for one correspondence.</summary>
public enum AnalysisDiffPlacementKind
{
    Unclassified,
    Stable,
    Moved,
}

/// <summary>
/// One closed relation in an <see cref="AnalysisDiff{T}"/>. One-sided relations expose only
/// their occupied endpoint; content and placement classifications exist only on correspondence.
/// </summary>
public abstract record AnalysisDiffRelation
{
    private AnalysisDiffRelation()
    {
    }

    // Records synthesize a protected copy constructor. This inaccessible abstract member prevents
    // external records from using that constructor to extend the closed relation hierarchy.
    private protected abstract void EnsureKnownRelation();

    /// <summary>One item present only in the After sequence.</summary>
    public sealed record Addition : AnalysisDiffRelation
    {
        public Addition(ImmutableArray<int> AfterCoordinates)
        {
            this.AfterCoordinates = ValidateCoordinates(
                AfterCoordinates,
                nameof(AfterCoordinates),
                expectedCount: 1);
        }

        public ImmutableArray<int> AfterCoordinates { get; }

        private protected override void EnsureKnownRelation()
        {
        }

        public bool Equals(Addition? other)
            => other is not null
                && FindingValueEquality.SequenceEqual(
                    AfterCoordinates,
                    other.AfterCoordinates);

        public override int GetHashCode()
            => FindingValueEquality.SequenceHashCode(AfterCoordinates);
    }

    /// <summary>One item present only in the Before sequence.</summary>
    public sealed record Removal : AnalysisDiffRelation
    {
        public Removal(ImmutableArray<int> BeforeCoordinates)
        {
            this.BeforeCoordinates = ValidateCoordinates(
                BeforeCoordinates,
                nameof(BeforeCoordinates),
                expectedCount: 1);
        }

        public ImmutableArray<int> BeforeCoordinates { get; }

        private protected override void EnsureKnownRelation()
        {
        }

        public bool Equals(Removal? other)
            => other is not null
                && FindingValueEquality.SequenceEqual(
                    BeforeCoordinates,
                    other.BeforeCoordinates);

        public override int GetHashCode()
            => FindingValueEquality.SequenceHashCode(BeforeCoordinates);
    }

    /// <summary>
    /// One producer-issued correspondence between non-empty Before and After populations.
    /// Population correspondence does not imply pairwise correspondence between their items.
    /// </summary>
    public sealed record Correspondence : AnalysisDiffRelation
    {
        public Correspondence(
            ImmutableArray<int> BeforeCoordinates,
            ImmutableArray<int> AfterCoordinates,
            AnalysisDiffContentKind Content,
            AnalysisDiffPlacementKind Placement)
        {
            this.BeforeCoordinates = ValidateCoordinates(
                BeforeCoordinates,
                nameof(BeforeCoordinates));
            this.AfterCoordinates = ValidateCoordinates(
                AfterCoordinates,
                nameof(AfterCoordinates));
            this.Content = Validate(Content, nameof(Content));
            this.Placement = Validate(Placement, nameof(Placement));
        }

        public ImmutableArray<int> BeforeCoordinates { get; }
        public ImmutableArray<int> AfterCoordinates { get; }
        public AnalysisDiffContentKind Content { get; }
        public AnalysisDiffPlacementKind Placement { get; }

        private protected override void EnsureKnownRelation()
        {
        }

        public bool Equals(Correspondence? other)
            => other is not null
                && FindingValueEquality.SequenceEqual(
                    BeforeCoordinates,
                    other.BeforeCoordinates)
                && FindingValueEquality.SequenceEqual(
                    AfterCoordinates,
                    other.AfterCoordinates)
                && Content == other.Content
                && Placement == other.Placement;

        public override int GetHashCode()
            => HashCode.Combine(
                FindingValueEquality.SequenceHashCode(BeforeCoordinates),
                FindingValueEquality.SequenceHashCode(AfterCoordinates),
                Content,
                Placement);
    }

    static ImmutableArray<int> ValidateCoordinates(
        ImmutableArray<int> coordinates,
        string parameterName,
        int? expectedCount = null)
    {
        if (coordinates.IsDefault)
            throw new ArgumentException("Coordinates must be initialized.", parameterName);
        if (expectedCount is int count && coordinates.Length != count)
        {
            throw new ArgumentException(
                $"The relation requires exactly {count} coordinate.",
                parameterName);
        }
        if (coordinates.IsEmpty)
            throw new ArgumentException("Coordinates must not be empty.", parameterName);

        for (int i = 0; i < coordinates.Length; i++)
        {
            int coordinate = coordinates[i];
            if (coordinate < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    coordinate,
                    "Coordinates must be non-negative.");
            }
            if (i > 0 && coordinate <= coordinates[i - 1])
            {
                throw new ArgumentException(
                    "Coordinates must be strictly ascending and duplicate-free.",
                    parameterName);
            }
        }

        return coordinates;
    }

    static AnalysisDiffContentKind Validate(
        AnalysisDiffContentKind content,
        string parameterName)
        => content switch
        {
            AnalysisDiffContentKind.Unclassified => content,
            AnalysisDiffContentKind.Unchanged => content,
            AnalysisDiffContentKind.Changed => content,
            _ => throw new ArgumentOutOfRangeException(parameterName, content, "Unknown content kind."),
        };

    static AnalysisDiffPlacementKind Validate(
        AnalysisDiffPlacementKind placement,
        string parameterName)
        => placement switch
        {
            AnalysisDiffPlacementKind.Unclassified => placement,
            AnalysisDiffPlacementKind.Stable => placement,
            AnalysisDiffPlacementKind.Moved => placement,
            _ => throw new ArgumentOutOfRangeException(parameterName, placement, "Unknown placement kind."),
        };
}

/// <summary>
/// A complete immutable partition of two ordered item sequences into producer-issued one-sided
/// and corresponding relations.
/// </summary>
public sealed record AnalysisDiff<T>
    where T : notnull
{
    public AnalysisDiff(
        ImmutableArray<T> Before,
        ImmutableArray<T> After,
        ImmutableArray<AnalysisDiffRelation> Relations)
    {
        this.Before = ValidateItems(Before, nameof(Before));
        this.After = ValidateItems(After, nameof(After));
        this.Relations = ValidateAndCanonicalize(
            Relations,
            Before.Length,
            After.Length);
    }

    public ImmutableArray<T> Before { get; }
    public ImmutableArray<T> After { get; }
    public ImmutableArray<AnalysisDiffRelation> Relations { get; }

    public bool Equals(AnalysisDiff<T>? other)
        => other is not null
            && FindingValueEquality.SequenceEqual(Before, other.Before)
            && FindingValueEquality.SequenceEqual(After, other.After)
            && FindingValueEquality.SequenceEqual(Relations, other.Relations);

    public override int GetHashCode()
        => HashCode.Combine(
            FindingValueEquality.SequenceHashCode(Before),
            FindingValueEquality.SequenceHashCode(After),
            FindingValueEquality.SequenceHashCode(Relations));

    public void Deconstruct(
        out ImmutableArray<T> Before,
        out ImmutableArray<T> After,
        out ImmutableArray<AnalysisDiffRelation> Relations)
        => (Before, After, Relations) = (this.Before, this.After, this.Relations);

    static ImmutableArray<T> ValidateItems(
        ImmutableArray<T> items,
        string parameterName)
    {
        if (items.IsDefault)
            throw new ArgumentException("Endpoint items must be initialized.", parameterName);
        if (items.Any(item => item is null))
            throw new ArgumentException("Endpoint items must not contain null values.", parameterName);
        return items;
    }

    static ImmutableArray<AnalysisDiffRelation> ValidateAndCanonicalize(
        ImmutableArray<AnalysisDiffRelation> relations,
        int beforeCount,
        int afterCount)
    {
        if (relations.IsDefault)
            throw new ArgumentException("Relations must be initialized.", nameof(Relations));
        if (relations.Any(relation => relation is null))
            throw new ArgumentException("Relations must not contain null values.", nameof(Relations));

        var beforeCoverage = new bool[beforeCount];
        var afterCoverage = new bool[afterCount];

        for (int relationIndex = 0; relationIndex < relations.Length; relationIndex++)
        {
            switch (relations[relationIndex])
            {
                case AnalysisDiffRelation.Addition addition:
                    MarkCoordinates(
                        addition.AfterCoordinates,
                        afterCoverage,
                        "After",
                        relationIndex);
                    break;
                case AnalysisDiffRelation.Removal removal:
                    MarkCoordinates(
                        removal.BeforeCoordinates,
                        beforeCoverage,
                        "Before",
                        relationIndex);
                    break;
                case AnalysisDiffRelation.Correspondence correspondence:
                    MarkCoordinates(
                        correspondence.BeforeCoordinates,
                        beforeCoverage,
                        "Before",
                        relationIndex);
                    MarkCoordinates(
                        correspondence.AfterCoordinates,
                        afterCoverage,
                        "After",
                        relationIndex);
                    break;
                default:
                    throw new ArgumentException(
                        $"Relation {relationIndex} has an unknown relation type.",
                        nameof(Relations));
            }
        }

        RequireCompleteCoverage(beforeCoverage, "Before");
        RequireCompleteCoverage(afterCoverage, "After");

        return
        [
            .. relations
                .OrderBy(CanonicalGroup)
                .ThenBy(FirstCoordinate),
        ];
    }

    static void MarkCoordinates(
        ImmutableArray<int> coordinates,
        bool[] coverage,
        string endpoint,
        int relationIndex)
    {
        foreach (int coordinate in coordinates)
        {
            if ((uint)coordinate >= (uint)coverage.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Relations),
                    coordinate,
                    $"{endpoint} coordinate {coordinate} in relation {relationIndex} is out of range.");
            }
            if (coverage[coordinate])
            {
                throw new ArgumentException(
                    $"{endpoint} coordinate {coordinate} occurs in more than one relation.",
                    nameof(Relations));
            }

            coverage[coordinate] = true;
        }
    }

    static void RequireCompleteCoverage(bool[] coverage, string endpoint)
    {
        int missingCoordinate = Array.IndexOf(coverage, false);
        if (missingCoordinate >= 0)
        {
            throw new ArgumentException(
                $"{endpoint} coordinate {missingCoordinate} does not occur in any relation.",
                nameof(Relations));
        }
    }

    static int CanonicalGroup(AnalysisDiffRelation relation)
        => relation is AnalysisDiffRelation.Addition ? 1 : 0;

    static int FirstCoordinate(AnalysisDiffRelation relation)
        => relation switch
        {
            AnalysisDiffRelation.Addition addition => addition.AfterCoordinates[0],
            AnalysisDiffRelation.Removal removal => removal.BeforeCoordinates[0],
            AnalysisDiffRelation.Correspondence correspondence
                => correspondence.BeforeCoordinates[0],
            _ => throw new InvalidOperationException("Unknown analysis diff relation type."),
        };
}
