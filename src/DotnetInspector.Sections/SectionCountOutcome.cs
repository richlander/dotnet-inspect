namespace DotnetInspector.Sections;

/// <summary>
/// One cardinality for a declared semantic row set: exact when the set's
/// evidence is complete for the logical request, and otherwise the observed
/// cardinality of the rows Rows would select from that evidence.
/// </summary>
public sealed class SectionCountEntry<TIdentity>
    where TIdentity : notnull
{
    public SectionCountEntry(TIdentity identity, int value)
        : this(identity, value, isExact: true)
    {
    }

    public SectionCountEntry(TIdentity identity, int value, bool isExact)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Identity = identity;
        Value = value;
        IsExact = isExact;
    }

    public TIdentity Identity { get; }

    public int Value { get; }

    /// <summary>
    /// False when the value is observed over incomplete evidence; it must
    /// then be disclosed like the incomplete Rows it counts, never as exact.
    /// </summary>
    public bool IsExact { get; }
}

/// <summary>
/// Owner-issued evidence for one row set: why it cannot supply a Count, or
/// the completion evidence that accompanies its Count entry.
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
            : this(
                counts,
                SectionContractSnapshot.Empty<
                    SectionCountSourceEvidence<TIdentity, TEvidence>>())
        {
        }

        /// <param name="sources">
        /// Either empty, or one owner-issued evidence entry per Count entry
        /// in the same row-set order.
        /// </param>
        public Completed(
            IReadOnlyList<SectionCountEntry<TIdentity>> counts,
            IReadOnlyList<
                SectionCountSourceEvidence<TIdentity, TEvidence>> sources)
        {
            ArgumentNullException.ThrowIfNull(counts);
            ArgumentNullException.ThrowIfNull(sources);
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

            if (sources.Count != 0)
            {
                if (sources.Count != counts.Count)
                {
                    throw new ArgumentException(
                        "Completed Count evidence requires one entry per "
                        + "row-set count.",
                        nameof(sources));
                }

                for (int index = 0; index < sources.Count; index++)
                {
                    SectionCountSourceEvidence<TIdentity, TEvidence> source =
                        sources[index]
                        ?? throw new ArgumentNullException(
                            nameof(sources),
                            $"Source entry {index + 1} is null.");
                    if (!EqualityComparer<TIdentity>.Default.Equals(
                            source.Identity,
                            counts[index].Identity))
                    {
                        throw new ArgumentException(
                            $"Source entry {index + 1} does not match its "
                            + "row-set count.",
                            nameof(sources));
                    }
                }
            }

            Counts = SectionContractSnapshot.Copy(counts);
            Sources = SectionContractSnapshot.Copy(sources);
        }

        public IReadOnlyList<SectionCountEntry<TIdentity>> Counts { get; }

        /// <summary>
        /// Owner-issued evidence for each entry in <see cref="Counts"/>, or
        /// empty when the producer supplied none.
        /// </summary>
        public IReadOnlyList<
            SectionCountSourceEvidence<TIdentity, TEvidence>> Sources
        {
            get;
        }
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
