namespace DotnetInspector.Sections;

/// <summary>One exact cardinality for a declared semantic row set.</summary>
public sealed class SectionCountEntry<TIdentity>
    where TIdentity : notnull
{
    public SectionCountEntry(TIdentity identity, int value)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Identity = identity;
        Value = value;
    }

    public TIdentity Identity { get; }

    public int Value { get; }
}

/// <summary>
/// Owner-issued evidence explaining why one row set cannot supply an exact
/// Count.
/// </summary>
public sealed class SectionCountSourceEvidence<TIdentity, TEvidence>
    where TIdentity : notnull
    where TEvidence : notnull
{
    public SectionCountSourceEvidence(
        TIdentity identity,
        TEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(evidence);
        Identity = identity;
        Evidence = evidence;
    }

    public TIdentity Identity { get; }

    public TEvidence Evidence { get; }
}

/// <summary>
/// Presentation-free terminal Count result for declared semantic row sets.
/// </summary>
public abstract record SectionCountOutcome<TIdentity, TEvidence>
    where TIdentity : notnull
    where TEvidence : notnull
{
    private protected SectionCountOutcome()
    {
    }

    public sealed record Completed :
        SectionCountOutcome<TIdentity, TEvidence>
    {
        public Completed(
            IReadOnlyList<SectionCountEntry<TIdentity>> counts)
        {
            ArgumentNullException.ThrowIfNull(counts);
            if (counts.Count == 0)
            {
                throw new ArgumentException(
                    "A completed Count requires at least one row-set result.",
                    nameof(counts));
            }

            var identities = new HashSet<TIdentity>();
            for (int index = 0; index < counts.Count; index++)
            {
                SectionCountEntry<TIdentity> count = counts[index]
                    ?? throw new ArgumentNullException(
                        nameof(counts),
                        $"Count entry {index + 1} is null.");
                if (!identities.Add(count.Identity))
                {
                    throw new ArgumentException(
                        "A completed Count cannot repeat a row-set identity.",
                        nameof(counts));
                }
            }

            Counts = SectionContractSnapshot.Copy(counts);
        }

        public IReadOnlyList<SectionCountEntry<TIdentity>> Counts { get; }
    }

    public sealed record SourceForCount :
        SectionCountOutcome<TIdentity, TEvidence>
    {
        public SourceForCount(
            IReadOnlyList<
                SectionCountSourceEvidence<TIdentity, TEvidence>> sources)
        {
            ArgumentNullException.ThrowIfNull(sources);
            if (sources.Count == 0)
            {
                throw new ArgumentException(
                    "A Count source failure requires at least one row-set entry.",
                    nameof(sources));
            }

            var identities = new HashSet<TIdentity>();
            for (int index = 0; index < sources.Count; index++)
            {
                SectionCountSourceEvidence<TIdentity, TEvidence> source =
                    sources[index]
                    ?? throw new ArgumentNullException(
                        nameof(sources),
                        $"Source entry {index + 1} is null.");
                if (!identities.Add(source.Identity))
                {
                    throw new ArgumentException(
                        "A Count source failure cannot repeat a row-set identity.",
                        nameof(sources));
                }
            }

            Sources = SectionContractSnapshot.Copy(sources);
        }

        public IReadOnlyList<
            SectionCountSourceEvidence<TIdentity, TEvidence>> Sources
        {
            get;
        }
    }

    public sealed record Semantic :
        SectionCountOutcome<TIdentity, TEvidence>
    {
        public Semantic(
            TIdentity identity,
            int stageNumber,
            int requiredPosition,
            int availableCount)
        {
            ArgumentNullException.ThrowIfNull(identity);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(stageNumber);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
                requiredPosition);
            ArgumentOutOfRangeException.ThrowIfNegative(availableCount);
            Identity = identity;
            StageNumber = stageNumber;
            RequiredPosition = requiredPosition;
            AvailableCount = availableCount;
        }

        public TIdentity Identity { get; }

        public int StageNumber { get; }

        public int RequiredPosition { get; }

        public int AvailableCount { get; }
    }
}
