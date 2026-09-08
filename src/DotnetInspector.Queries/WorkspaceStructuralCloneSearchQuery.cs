using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspector.Queries;

/// <summary>
/// Which Workspace populations may contribute candidate methods to one
/// structural-clone search.
/// </summary>
public enum StructuralCloneCandidateBreadth
{
    /// <summary>The selected subject's containing exact library only.</summary>
    Self,

    /// <summary>
    /// <see cref="Self"/> plus every realized participant contributed by an
    /// ecosystem registration in the bound Workspace revision.
    /// </summary>
    SelfAndRegisteredEcosystems,

    /// <summary>
    /// Every participant realized for the bound Workspace revision.
    /// </summary>
    Everything,
}

/// <summary>
/// Which methods inside the selected breadth may be ranked against the seeds.
/// </summary>
public enum StructuralCloneCandidateDiscovery
{
    /// <summary>
    /// Methods whose decoded declaring-type and member names both qualify
    /// against at least one seed.
    /// </summary>
    SimilarNames,

    /// <summary>Every method in the realized breadth.</summary>
    All,
}

/// <summary>
/// The breadth membership one realized participant carries, as issued by the
/// Workspace and registration owner.
/// </summary>
/// <remarks>
/// Membership is supplied, never inferred. This query has no registration
/// lookup, so it cannot recover ecosystem membership from package names,
/// assembly identities, or provenance.
/// </remarks>
public enum StructuralCloneParticipantMembership
{
    /// <summary>
    /// The selected subject's containing exact library. Exactly one snapshot
    /// entry carries this membership and every breadth admits it.
    /// </summary>
    ContainingLibrary,

    /// <summary>
    /// A participant contributed by an ecosystem registration in the bound
    /// Workspace revision.
    /// </summary>
    RegisteredEcosystem,

    /// <summary>
    /// A participant available through the bound Workspace revision without
    /// ecosystem registration.
    /// </summary>
    Available,
}

/// <summary>
/// One realized participant and the breadth membership its owner issued.
/// </summary>
public sealed class StructuralCloneParticipantEntry
{
    public StructuralCloneParticipantEntry(
        AssemblyContextGroup group,
        AssemblyContextParticipant participant,
        StructuralCloneParticipantMembership membership)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(participant);
        if (!Enum.IsDefined(membership))
        {
            throw new ArgumentOutOfRangeException(nameof(membership));
        }
        if (!group.Participants.Any(
                candidate => ReferenceEquals(candidate, participant)))
        {
            throw new ArgumentException(
                "The selected participant is not a member of its assembly context group.",
                nameof(participant));
        }

        Group = group;
        Participant = participant;
        Membership = membership;
        Subject = new AssemblyContextSubject(participant.Assembly);
    }

    public AssemblyContextGroup Group { get; }
    public AssemblyContextParticipant Participant { get; }
    public StructuralCloneParticipantMembership Membership { get; }

    /// <summary>
    /// The participant's exact registration, identity, and provenance without
    /// its content-opening capability.
    /// </summary>
    public AssemblyContextSubject Subject { get; }

    internal bool AdmittedBy(StructuralCloneCandidateBreadth breadth)
        => breadth switch
        {
            StructuralCloneCandidateBreadth.Self =>
                Membership
                    == StructuralCloneParticipantMembership
                        .ContainingLibrary,
            StructuralCloneCandidateBreadth.SelfAndRegisteredEcosystems =>
                Membership
                    is StructuralCloneParticipantMembership
                            .ContainingLibrary
                        or StructuralCloneParticipantMembership
                            .RegisteredEcosystem,
            _ => true,
        };
}

/// <summary>
/// Opaque process-local identity for one immutable clone-search participant
/// snapshot.
/// </summary>
/// <remarks>Equality is reference identity.</remarks>
public sealed class StructuralCloneParticipantSnapshotIdentity
{
    internal StructuralCloneParticipantSnapshotIdentity()
    {
    }
}

/// <summary>
/// Opaque snapshot-local identity for one participant in one immutable
/// clone-search participant snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Equality is reference identity. A snapshot issues exactly one identity per
/// entry, so two entries that carry equal MVIDs, equal MethodDef tokens, equal
/// retained content, or equal <see cref="AssemblyContextSubject"/> values still
/// receive distinct identities. This is the endpoint identity component that
/// keeps two distinct acquisition registrations of the same bytes apart.
/// </para>
/// <para>
/// <see cref="Ordinal"/> is the participant's position in the snapshot's
/// owner-issued entry order. That order is part of the snapshot's exact
/// identity: the snapshot owner chooses it, the snapshot validates that it
/// contains no repeated participant or repeated acquisition registration, and
/// the search uses it as the snapshot-local total order for deterministic
/// endpoint tie-breaking. It is meaningful only within
/// <see cref="Snapshot"/> and carries no cross-snapshot, Workspace, or
/// registration meaning.
/// </para>
/// </remarks>
public sealed class StructuralCloneParticipantIdentity
{
    internal StructuralCloneParticipantIdentity(
        StructuralCloneParticipantSnapshotIdentity snapshot,
        int ordinal)
    {
        Snapshot = snapshot;
        Ordinal = ordinal;
    }

    /// <summary>The snapshot that issued this identity.</summary>
    public StructuralCloneParticipantSnapshotIdentity Snapshot { get; }

    /// <summary>
    /// The participant's position in its snapshot's owner-issued entry order.
    /// </summary>
    public int Ordinal { get; }

    public override string ToString() => $"participant #{Ordinal}";
}

/// <summary>
/// One complete owner-issued participant snapshot and the exact Workspace
/// revisions it is bound to.
/// </summary>
/// <remarks>
/// <para>
/// Breadth realization belongs to the Workspace and registration owner. This
/// type is the exact hand-off: the caller supplies the finite realized
/// participants and their breadth membership, and the search evaluates
/// candidates only against this snapshot. A later Workspace edit does not
/// change a bound snapshot.
/// </para>
/// <para>
/// The constructor rejects a snapshot that could carry inconsistent or
/// duplicated membership: one acquisition registration may appear once, one
/// participant may appear once, exactly one entry may be the containing
/// library, and both revisions must come from the same Workspace.
/// </para>
/// <para>
/// The snapshot owns a deterministic snapshot-local endpoint identity order.
/// The supplied entry order is part of this snapshot's exact identity: it is
/// captured once, never reordered, and each entry receives one opaque
/// <see cref="StructuralCloneParticipantIdentity"/> whose ordinal is its
/// position in that order. Because the constructor rejects a repeated
/// participant and a repeated acquisition registration, those ordinals are a
/// total order over distinct registrations, which is what the search uses for
/// deterministic tie-breaking. The same entry supplied to two snapshots
/// receives two identities; the ordinal has no meaning outside its snapshot.
/// </para>
/// <para>
/// That an entry's assembly context group was created by the Workspace that
/// issued the bound revisions is <c>unverified</c>: the group exposes no
/// owning-Workspace identity, so the snapshot's issuer owns that association.
/// The issuer also owns lifetime: it must keep every entry's assembly context
/// group and participant alive for the whole execution the snapshot is passed
/// to, because a snapshot retains group references rather than immutable
/// images. Neither obligation is enforced here, and enforcement stays
/// <c>unverified</c> until the concrete Workspace and registration producer
/// lands. Premature release is made visible instead of silently changing
/// results: a released containing library returns
/// <see cref="StructuralCloneSearchFailureKind.SeedLibraryReleased"/> as a
/// typed failed result, and a released candidate becomes that library's
/// <see cref="StructuralCloneSearchFailureKind.CandidateLibraryReleased"/>
/// incomplete coverage beside the evidence already ranked.
/// </para>
/// </remarks>
public sealed class StructuralCloneParticipantSnapshot
{
    public StructuralCloneParticipantSnapshot(
        WorkspaceScopeRevision startingRevision,
        WorkspaceScopeRevision effectiveRevision,
        IEnumerable<StructuralCloneParticipantEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(startingRevision);
        ArgumentNullException.ThrowIfNull(effectiveRevision);
        ArgumentNullException.ThrowIfNull(entries);
        if (!ReferenceEquals(
                startingRevision.Workspace,
                effectiveRevision.Workspace))
        {
            throw new ArgumentException(
                "The starting and effective Workspace revisions must come from the same Workspace.",
                nameof(effectiveRevision));
        }

        var builder =
            ImmutableArray.CreateBuilder<
                StructuralCloneParticipantEntry>();
        var participants =
            new HashSet<AssemblyContextParticipant>(
                ReferenceEqualityComparer.Instance);
        var registrations =
            new HashSet<AssemblyAcquisitionRegistration>(
                ReferenceEqualityComparer.Instance);
        StructuralCloneParticipantEntry? containing = null;
        int containingOrdinal = -1;
        foreach (StructuralCloneParticipantEntry entry in entries)
        {
            ArgumentNullException.ThrowIfNull(entry);
            if (!participants.Add(entry.Participant))
            {
                throw new ArgumentException(
                    "A participant may appear only once in a clone-search participant snapshot.",
                    nameof(entries));
            }
            if (!registrations.Add(
                    entry.Participant.Assembly.Registration))
            {
                throw new ArgumentException(
                    "An acquisition registration may appear only once in a clone-search participant snapshot.",
                    nameof(entries));
            }
            if (entry.Membership
                == StructuralCloneParticipantMembership
                    .ContainingLibrary)
            {
                if (containing is not null)
                {
                    throw new ArgumentException(
                        "A clone-search participant snapshot has exactly one containing library.",
                        nameof(entries));
                }

                containing = entry;
                containingOrdinal = builder.Count;
            }

            builder.Add(entry);
        }

        ContainingLibrary =
            containing
            ?? throw new ArgumentException(
                "A clone-search participant snapshot requires the selected subject's containing library.",
                nameof(entries));
        StartingRevision = startingRevision;
        EffectiveRevision = effectiveRevision;
        Entries = builder.ToImmutable();
        Identity = new StructuralCloneParticipantSnapshotIdentity();
        var identities =
            ImmutableArray.CreateBuilder<
                StructuralCloneParticipantIdentity>(Entries.Length);
        for (int ordinal = 0; ordinal < Entries.Length; ordinal++)
        {
            identities.Add(
                new StructuralCloneParticipantIdentity(Identity, ordinal));
        }

        ParticipantIdentities = identities.MoveToImmutable();
        ContainingLibraryIdentity =
            ParticipantIdentities[containingOrdinal];
    }

    public StructuralCloneParticipantSnapshotIdentity Identity { get; }

    /// <summary>The exact Workspace revision the request was bound to.</summary>
    public WorkspaceScopeRevision StartingRevision { get; }

    /// <summary>
    /// The exact Workspace revision this snapshot was realized against. It may
    /// differ from <see cref="StartingRevision"/> when bounded discovery,
    /// resolution, or acquisition committed additional participants.
    /// </summary>
    public WorkspaceScopeRevision EffectiveRevision { get; }

    public ImmutableArray<StructuralCloneParticipantEntry> Entries { get; }

    /// <summary>
    /// One opaque snapshot-local identity per entry, in the snapshot's exact
    /// owner-issued order and aligned with <see cref="Entries"/>.
    /// </summary>
    public ImmutableArray<StructuralCloneParticipantIdentity>
        ParticipantIdentities
    { get; }

    /// <summary>The selected subject's containing exact library.</summary>
    public StructuralCloneParticipantEntry ContainingLibrary { get; }

    /// <summary>
    /// The snapshot-local identity issued to <see cref="ContainingLibrary"/>.
    /// </summary>
    public StructuralCloneParticipantIdentity ContainingLibraryIdentity
    {
        get;
    }
}

/// <summary>
/// The exact owner-issued subject that supplies the seed population.
/// </summary>
public abstract record StructuralCloneSearchSeed
{
    private protected StructuralCloneSearchSeed()
    {
    }

    /// <summary>Every MethodDef in the containing exact library.</summary>
    public sealed record Library : StructuralCloneSearchSeed;

    /// <summary>Every MethodDef declared by one exact type.</summary>
    public sealed record Type : StructuralCloneSearchSeed
    {
        public Type(MetadataTypeDefinitionName definition)
        {
            Definition =
                definition
                ?? throw new ArgumentNullException(nameof(definition));
        }

        public MetadataTypeDefinitionName Definition { get; }
    }

    /// <summary>
    /// Every exact method body occupied by one selected member identity.
    /// </summary>
    public sealed record Member : StructuralCloneSearchSeed
    {
        public Member(
            MetadataTypeDefinitionName type,
            MemberAnchor member)
        {
            Definition =
                type
                ?? throw new ArgumentNullException(nameof(type));
            MemberIdentity =
                member
                ?? throw new ArgumentNullException(nameof(member));
        }

        public MetadataTypeDefinitionName Definition { get; }
        public MemberAnchor MemberIdentity { get; }
    }
}

/// <summary>
/// Independent result and work bounds for one clone search.
/// </summary>
/// <remarks>
/// <para>
/// The bounds are layered. <see cref="MaximumSeedMethods"/> and
/// <see cref="MaximumCandidateMethods"/> bound one library's contribution;
/// <see cref="MaximumParticipants"/> and <see cref="MaximumRetrievalPairs"/>
/// bound the whole search, because per-library bounds alone permit an
/// arbitrary number of libraries each contributing a quadratic
/// seed-by-candidate retrieval population.
/// </para>
/// <para>
/// The two aggregate bounds are discovered at different points, and both are
/// whole-unit exclusions. <see cref="MaximumParticipants"/> is a preflight
/// bound: it is decided over the snapshot's exact entry order before any
/// candidate image is opened. <see cref="MaximumRetrievalPairs"/> is a
/// whole-participant admission bound: under
/// <see cref="StructuralCloneCandidateDiscovery.SimilarNames"/> a
/// participant's pair population is only known while its per-seed candidate
/// groups are being formed, so admission stops mid-formation and abandons the
/// participant, and only the latched exhausted state that follows excludes
/// later participants without opening them. Either way a participant is
/// evaluated completely or excluded completely with a visible failure, so a
/// bounded run can never present a partial pair population as a complete
/// global top N. Every whole-unit exclusion makes coverage incomplete, which
/// is separate from
/// <see cref="StructuralCloneSearchReceipt.ResultLimitReached"/>, the
/// intentional row bound over complete evidence.
/// </para>
/// <para>
/// Name work is bounded three ways: one decoded name may not exceed
/// <see cref="MaximumNameCharacters"/>, the whole search may not charge more
/// than <see cref="MaximumNameComparisonWork"/> edit-distance cells, and the
/// memoized score vectors may not retain more than
/// <see cref="MaximumNameCacheCells"/> cells. Reaching either work bound is
/// visible library coverage, never a silently narrowed candidate population;
/// reaching the cache bound is invisible because the cache is a pure
/// optimization.
/// </para>
/// </remarks>
/// <param name="MaximumResults">
/// Intentional returned-row bound, applied only after the global merge.
/// </param>
/// <param name="MaximumParticipants">
/// The greatest number of breadth-admitted participants one search may
/// evaluate. The containing library is always the first admitted participant;
/// every later admitted participant beyond this bound is excluded whole.
/// </param>
/// <param name="MaximumRetrievalPairs">
/// The greatest total number of seed-by-candidate retrieval methods one search
/// may submit to Analysis, summed over every participant and seed.
/// </param>
/// <param name="MaximumRetrievalChunkMethods">
/// The greatest number of candidate methods one <c>RetrieveSimilar</c> call
/// submits. One seed's candidate group is retrieved in consecutive chunks of
/// this size so cancellation is observed between bounded units of Analysis
/// work. Every chunk requests its complete ranked population and the search
/// merges the chunks into one global ranking, so the chunk size changes only
/// cancellation granularity and the seed-body production count, never the
/// ranked rows or the aggregate <see cref="MaximumRetrievalPairs"/> charge.
/// </param>
/// <param name="MaximumNameCacheCells">
/// The greatest number of memoized name-similarity score cells one
/// participant's decoded-name caches may retain at once, shared by the
/// declaring-type and member caches. One cached name costs one cell per
/// distinct seed name, so an entry-count bound alone would let a large seed
/// population retain gigabytes. A name whose whole score vector does not fit
/// the remaining cells is scored without being retained.
/// </param>
public sealed record WorkspaceStructuralCloneSearchLimits(
    int MaximumResults = 100,
    int MaximumSeedMethods = 50_000,
    int MaximumCandidateMethods = 50_000,
    int MaximumParticipants = 256,
    long MaximumRetrievalPairs = 1L << 20,
    int MaximumRetrievalChunkMethods = 1024,
    int MaximumNameCharacters = 256,
    long MaximumNameComparisonWork = 1L << 30,
    long MaximumNameCacheCells = 1L << 20,
    StructuralCloneComparisonLimits? ComparisonLimits = null)
{
    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumResults,
            1,
            nameof(MaximumResults));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumSeedMethods,
            1,
            nameof(MaximumSeedMethods));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumCandidateMethods,
            1,
            nameof(MaximumCandidateMethods));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumParticipants,
            1,
            nameof(MaximumParticipants));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumRetrievalPairs,
            1L,
            nameof(MaximumRetrievalPairs));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumRetrievalChunkMethods,
            1,
            nameof(MaximumRetrievalChunkMethods));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumNameCharacters,
            1,
            nameof(MaximumNameCharacters));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumNameComparisonWork,
            1L,
            nameof(MaximumNameComparisonWork));
        ArgumentOutOfRangeException.ThrowIfLessThan(
            MaximumNameCacheCells,
            1L,
            nameof(MaximumNameCacheCells));
    }
}

/// <summary>The bound request for one Workspace structural-clone search.</summary>
public sealed record WorkspaceStructuralCloneSearchInput
{
    public WorkspaceStructuralCloneSearchInput(
        StructuralCloneParticipantSnapshot participants,
        StructuralCloneSearchSeed seed,
        StructuralCloneCandidateBreadth breadth =
            StructuralCloneCandidateBreadth.Everything,
        StructuralCloneCandidateDiscovery discovery =
            StructuralCloneCandidateDiscovery.SimilarNames,
        WorkspaceStructuralCloneSearchLimits? limits = null)
    {
        Participants =
            participants
            ?? throw new ArgumentNullException(nameof(participants));
        Seed =
            seed
            ?? throw new ArgumentNullException(nameof(seed));
        if (!Enum.IsDefined(breadth))
        {
            throw new ArgumentOutOfRangeException(nameof(breadth));
        }
        if (!Enum.IsDefined(discovery))
        {
            throw new ArgumentOutOfRangeException(nameof(discovery));
        }

        limits?.Validate();
        Breadth = breadth;
        Discovery = discovery;
        Limits = limits;
    }

    public StructuralCloneParticipantSnapshot Participants { get; }
    public StructuralCloneSearchSeed Seed { get; }
    public StructuralCloneCandidateBreadth Breadth { get; }
    public StructuralCloneCandidateDiscovery Discovery { get; }
    public WorkspaceStructuralCloneSearchLimits? Limits { get; }
}

/// <summary>
/// One exact method endpoint: the participant that retains the content, its
/// owner-issued registration identity, and the physical method address.
/// </summary>
/// <remarks>
/// Equal MethodDef tokens in different modules, and equal MVIDs from different
/// retained contents, do not establish endpoint identity. The opaque
/// <see cref="Participant"/> identity is issued once per snapshot entry, so
/// two distinct acquisition registrations of identical bytes remain distinct
/// endpoints, and the <see cref="Subject"/> reference carries the exact
/// registration and content association.
/// </remarks>
public sealed record StructuralCloneSearchEndpoint
{
    internal StructuralCloneSearchEndpoint(
        StructuralCloneParticipantIdentity participant,
        StructuralCloneParticipantEntry entry,
        MetadataMethodAddress method)
    {
        Participant = participant;
        Subject = entry.Subject;
        Membership = entry.Membership;
        Method = method;
    }

    /// <summary>
    /// The endpoint's opaque snapshot-local participant identity. Its ordinal
    /// is the snapshot's owner-issued order, which supplies the deterministic
    /// tie-break order for one search.
    /// </summary>
    public StructuralCloneParticipantIdentity Participant { get; }

    public AssemblyContextSubject Subject { get; }
    public StructuralCloneParticipantMembership Membership { get; }
    public MetadataMethodAddress Method { get; }
}

/// <summary>
/// The decoded-name evidence that admitted one candidate method against the
/// exact seed it is paired with under
/// <see cref="StructuralCloneCandidateDiscovery.SimilarNames"/>.
/// </summary>
/// <remarks>
/// Both scores belong to the same seed-candidate pair as the row that carries
/// them. A candidate admitted by one seed is never ranked against a different
/// seed that did not itself clear both thresholds.
/// </remarks>
public sealed record StructuralCloneNameQualification(
    double DeclaringTypeSimilarity,
    double MemberSimilarity);

/// <summary>
/// One globally ranked pair of exact method bodies and the Analysis-issued
/// retrieval evidence for that orientation.
/// </summary>
/// <remarks>
/// Retrieval rank is evidence for candidate selection. It is not a checked
/// clone relation, correspondence, or provenance conclusion.
/// </remarks>
public sealed record StructuralCloneSearchPair(
    int Rank,
    StructuralCloneSearchEndpoint Left,
    StructuralCloneSearchEndpoint Right,
    StructuralCloneSimilarityEvidence Similarity,
    StructuralCloneNameQualification? NameQualification);

/// <summary>Typed clone-search coverage and target failures.</summary>
public enum StructuralCloneSearchFailureKind
{
    SeedTypeNotFound,
    SeedTypeAmbiguous,
    SeedMemberNotFound,
    SeedMemberAmbiguous,

    /// <summary>
    /// The exact seed member exists but occupies no method body. A property or
    /// event with no <c>MethodSemantics</c> association, and every field,
    /// selects an empty seed population rather than an arbitrary body.
    /// </summary>
    SeedMemberHasNoMethodBody,
    SeedPopulationLimitReached,
    CandidatePopulationLimitReached,
    ParticipantPopulationLimitReached,
    RetrievalWorkLimitReached,
    CandidateLibraryUnavailable,

    /// <summary>
    /// The containing library's assembly context group or participant was
    /// released before the search could read its immutable image. The snapshot
    /// issuer must retain both for the search's execution.
    /// </summary>
    SeedLibraryReleased,

    /// <summary>
    /// A candidate participant's assembly context group or participant was
    /// released before the search could read its immutable image. The library
    /// contributes no ranked row and its coverage is incomplete.
    /// </summary>
    CandidateLibraryReleased,
    MetadataInspectionFailed,
    NameDecodeFailed,
    NameWorkLimitReached,
}

/// <summary>One visible clone-search failure.</summary>
public sealed record StructuralCloneSearchFailure(
    StructuralCloneSearchFailureKind Kind,
    AssemblyContextSubject? Subject,
    string Detail);

/// <summary>Per-seed coverage for one clone search.</summary>
public sealed record StructuralCloneSearchSeedCoverage(
    StructuralCloneSearchEndpoint Seed,
    StructuralCloneRetrievalDisposition Disposition,
    int RankedPairs,
    int SuppressedPairs,
    ImmutableArray<StructuralCloneRetrievalBlocker> Blockers,
    ImmutableArray<StructuralCloneSearchFailure> Failures)
{
    /// <summary>
    /// Every admitted candidate of this seed was evaluated. Row limits are a
    /// separate, intentional result bound.
    /// </summary>
    public bool CoverageIsComplete =>
        Disposition == StructuralCloneRetrievalDisposition.Completed
        && Blockers.IsEmpty
        && Failures.IsEmpty;
}

/// <summary>Per-library coverage for one clone search.</summary>
/// <param name="Admitted">
/// The participant passed both admission decisions: the requested breadth and
/// the search's aggregate work bounds. A per-library outcome after that
/// point — an unreadable image, malformed metadata, or a rejected candidate
/// population — leaves this true and is recorded in
/// <paramref name="Failures"/>.
/// </param>
/// <param name="RetrievalPairs">
/// The seed-candidate pairs this participant charged against the search's
/// aggregate retrieval budget.
/// </param>
/// <param name="AnalysisBlockers">
/// The distinct Analysis-issued blockers that omitted candidate methods of
/// this participant, aggregated over every seed and every retrieval chunk run
/// against it. The blockers are Analysis's own typed evidence, not a string
/// rendering of it.
/// </param>
public sealed record StructuralCloneSearchLibraryCoverage(
    StructuralCloneParticipantIdentity Participant,
    AssemblyContextSubject Subject,
    StructuralCloneParticipantMembership Membership,
    bool Admitted,
    int CandidateMethods,
    int DiscoveredMethods,
    long RetrievalPairs,
    long NameComparisonWork,
    ImmutableArray<StructuralCloneSearchFailure> Failures,
    ImmutableArray<StructuralCloneRetrievalBlocker> AnalysisBlockers)
{
    /// <summary>
    /// The library contributed its complete admitted candidate population.
    /// A library excluded by breadth is complete: breadth is a request
    /// decision, not missing evidence. A library excluded whole by a work
    /// bound is not: it carries the failure that omitted its pairs, and
    /// neither is a candidate body Analysis could not produce, which carries
    /// its Analysis blocker.
    /// </summary>
    /// <remarks>
    /// A seed body Analysis could not produce is not this library's
    /// incompleteness. It omits no candidate of this participant: the seed
    /// produced no evidence against any library, which is already visible as
    /// that seed's own coverage and in the whole result's coverage. Keeping
    /// the two separate is what lets a reader ask which participant Analysis
    /// could not fully evaluate.
    /// </remarks>
    public bool CoverageIsComplete =>
        Failures.IsEmpty && AnalysisBlockers.IsEmpty;
}

/// <summary>Bounded-work receipt for one clone search.</summary>
/// <param name="RetrievalPairs">
/// The seed-by-candidate methods submitted to Analysis, summed over every
/// participant and seed. It is independent of how those methods were divided
/// into calls.
/// </param>
/// <param name="RetrievalCalls">
/// The <c>RetrieveSimilar</c> calls the search issued. One seed's candidate
/// group in one participant is retrieved in bounded chunks, so this counts
/// chunks, not seed-participant pairs.
/// </param>
public sealed record StructuralCloneSearchReceipt(
    int SeedMethods,
    int AdmittedLibraries,
    int ExcludedLibraries,
    int CandidateMethods,
    int DiscoveredCandidateMethods,
    long RetrievalPairs,
    int RetrievalCalls,
    int RankedPairs,
    int SuppressedPairs,
    int ReturnedPairs,
    bool ResultLimitReached);

/// <summary>
/// Typed outcome of one Workspace structural-clone search.
/// </summary>
public abstract record WorkspaceStructuralCloneSearchResult
{
    private protected WorkspaceStructuralCloneSearchResult()
    {
    }

    /// <summary>
    /// The search ran against the bound participant snapshot. Pairs are one
    /// global ranking; coverage reports what the ranking is over.
    /// </summary>
    public sealed record Available(
        AssemblyContextSubject SeedSubject,
        StructuralCloneSearchSeed Seed,
        StructuralCloneCandidateBreadth Breadth,
        StructuralCloneCandidateDiscovery Discovery,
        double NameSimilarityThreshold,
        WorkspaceStructuralCloneSearchLimits Limits,
        WorkspaceScopeRevision StartingRevision,
        WorkspaceScopeRevision EffectiveRevision,
        StructuralCloneParticipantSnapshotIdentity ParticipantSnapshot,
        ImmutableArray<StructuralCloneSearchPair> Pairs,
        ImmutableArray<StructuralCloneSearchSeedCoverage> Seeds,
        ImmutableArray<StructuralCloneSearchLibraryCoverage> Libraries,
        StructuralCloneSearchReceipt Receipt)
        : WorkspaceStructuralCloneSearchResult
    {
        /// <summary>
        /// Every admitted seed and library was evaluated completely. This is
        /// independent of <see cref="StructuralCloneSearchReceipt.ResultLimitReached"/>,
        /// which reports intentional row suppression over complete evidence.
        /// </summary>
        public bool CoverageIsComplete =>
            Seeds.All(static seed => seed.CoverageIsComplete)
            && Libraries.All(
                static library => library.CoverageIsComplete);
    }

    /// <summary>The containing library's immutable image could not be acquired.</summary>
    public sealed record Rejected(
        AssemblyContextSubject SeedSubject,
        CandidateOpenFailure Failure)
        : WorkspaceStructuralCloneSearchResult;

    /// <summary>
    /// The seed population could not be selected from the containing library.
    /// </summary>
    public sealed record Failed(
        AssemblyContextSubject SeedSubject,
        StructuralCloneSearchFailure Failure)
        : WorkspaceStructuralCloneSearchResult;
}

/// <summary>
/// Runs one Library, Type, or Member structural-clone search over an exact
/// caller-supplied participant snapshot, returning one globally ranked bounded
/// result.
/// </summary>
/// <remarks>
/// <para>
/// Breadth selects the Workspace population, discovery selects which methods
/// inside it may be ranked, and the result limit applies only after the global
/// merge. The query never infers registered-ecosystem membership and never
/// degrades <see cref="StructuralCloneCandidateBreadth.SelfAndRegisteredEcosystems"/>
/// to <see cref="StructuralCloneCandidateBreadth.Self"/>: membership arrives
/// with the snapshot.
/// </para>
/// <para>
/// Ranking currency is Analysis-issued. Cross-image rank remains retrieval
/// evidence; this query establishes no checked clone relation and produces no
/// comparison document.
/// </para>
/// <para>
/// The normative contract is
/// <c>docs/design/structural-clone-search-scope.md</c>.
/// </para>
/// </remarks>
public static class WorkspaceStructuralCloneSearchQuery
{
    /// <summary>
    /// The fixed normalized name-similarity threshold. It follows the existing
    /// default of <c>TypeMatcher.FindClosest</c> and is not a user control.
    /// </summary>
    public const double NameSimilarityThreshold = 0.6;

    /// <summary>
    /// Maximum decoded candidate names memoized per participant. It bounds the
    /// retained decoded-name keys;
    /// <see cref="WorkspaceStructuralCloneSearchLimits.MaximumNameCacheCells"/>
    /// separately bounds the retained score cells, which grow with the seed
    /// name population. The cache is an optimization: an exhausted name-work
    /// budget is checked before a cached vector is returned, so neither bound
    /// can change admission.
    /// </summary>
    const int MaximumNameCacheEntries = 4096;

    public static InspectionQuery<WorkspaceStructuralCloneSearchResult>
        Definition { get; } =
            new(
                "Workspace structural-clone search",
                InspectionCost.Unbounded);

    public static WorkspaceStructuralCloneSearchResult Execute(
        WorkspaceStructuralCloneSearchInput input)
        => Execute(input, CancellationToken.None);

    public static WorkspaceStructuralCloneSearchResult Execute(
        WorkspaceStructuralCloneSearchInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        StructuralCloneParticipantSnapshot snapshot = input.Participants;
        StructuralCloneParticipantEntry self = snapshot.ContainingLibrary;
        WorkspaceStructuralCloneSearchLimits limits =
            input.Limits ?? new WorkspaceStructuralCloneSearchLimits();
        limits.Validate();

        AssemblyImageAccessResult<WorkspaceStructuralCloneSearchResult>
            access;
        try
        {
            access =
                self.Group.UseSnapshot(
                    self.Participant,
                    cancellationToken,
                    seedSnapshot => ExecuteWithSeedImage(
                        seedSnapshot,
                        input,
                        limits,
                        cancellationToken));
        }
        catch (ObjectDisposedException ex)
        {
            // The snapshot issuer owns group and participant lifetime. A
            // release before execution is a visible typed failure, never an
            // exception escaping the query or a silently narrowed result. A
            // candidate release is contained at its own access boundary, so
            // only the seed's own access can reach this handler.
            return Failed(
                self.Subject,
                StructuralCloneSearchFailureKind.SeedLibraryReleased,
                "The containing library's assembly context group or "
                    + "participant was released before the search could read "
                    + $"its immutable image: {ex.Message}");
        }

        return access switch
        {
            AssemblyImageAccessResult<WorkspaceStructuralCloneSearchResult>
                .Available available =>
                    available.Value,
            AssemblyImageAccessResult<WorkspaceStructuralCloneSearchResult>
                .Rejected rejected =>
                    new WorkspaceStructuralCloneSearchResult.Rejected(
                        self.Subject,
                        rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown seed image access result."),
        };
    }

    static WorkspaceStructuralCloneSearchResult ExecuteWithSeedImage(
        AssemblyImageSnapshot seedSnapshot,
        WorkspaceStructuralCloneSearchInput input,
        WorkspaceStructuralCloneSearchLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StructuralCloneParticipantSnapshot snapshot = input.Participants;
        StructuralCloneParticipantEntry self = snapshot.ContainingLibrary;
        using var seedImage = new PEReader(seedSnapshot.Content);
        SeedPopulation seeds;
        try
        {
            seeds = ResolveSeeds(seedImage, input, limits);
        }
        catch (Exception ex)
            when (StructuralCloneMetadataResolution
                .IsMalformedMetadata(ex))
        {
            return Failed(
                self.Subject,
                StructuralCloneSearchFailureKind.MetadataInspectionFailed,
                ex.Message);
        }

        if (seeds.Failure is { } seedFailure)
        {
            return new WorkspaceStructuralCloneSearchResult.Failed(
                self.Subject,
                seedFailure);
        }

        var search = new SearchState(input, limits, seeds);
        FrozenSet<int> beyondParticipantLimit =
            SelectParticipantsBeyondLimit(snapshot, input.Breadth, limits);
        for (int index = 0; index < snapshot.Entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StructuralCloneParticipantEntry entry =
                snapshot.Entries[index];
            StructuralCloneParticipantIdentity identity =
                snapshot.ParticipantIdentities[index];
            if (!entry.AdmittedBy(input.Breadth))
            {
                search.AddExcludedLibrary(identity, entry);
                continue;
            }

            if (beyondParticipantLimit.Contains(index))
            {
                search.AddLimitExcludedLibrary(
                    identity,
                    entry,
                    StructuralCloneSearchFailureKind
                        .ParticipantPopulationLimitReached,
                    "The breadth-admitted participant population exceeds "
                        + $"{limits.MaximumParticipants}; this participant "
                        + "was excluded whole and contributed no ranked "
                        + "pair.");
                continue;
            }

            // The aggregate retrieval bound latches: once one participant's
            // pair population does not fit, no later participant is opened,
            // so a bounded run cannot silently interleave partial evidence.
            if (search.RetrievalBudgetExhausted)
            {
                search.AddLimitExcludedLibrary(
                    identity,
                    entry,
                    StructuralCloneSearchFailureKind
                        .RetrievalWorkLimitReached,
                    "The aggregate retrieval budget of "
                        + $"{limits.MaximumRetrievalPairs} seed-candidate "
                        + "pairs was already exhausted; this participant was "
                        + "excluded whole and contributed no ranked pair.");
                continue;
            }

            if (ReferenceEquals(entry, self))
            {
                search.AddLibrary(
                    SearchParticipant(
                        search,
                        identity,
                        entry,
                        seedImage,
                        seedImage,
                        sameImage: true,
                        cancellationToken));
                continue;
            }

            AssemblyImageAccessResult<
                StructuralCloneSearchLibraryCoverage> access;
            try
            {
                access =
                    entry.Group.UseSnapshot(
                        entry.Participant,
                        cancellationToken,
                        candidateSnapshot =>
                        {
                            using var candidateImage =
                                new PEReader(candidateSnapshot.Content);
                            return SearchParticipant(
                                search,
                                identity,
                                entry,
                                seedImage,
                                candidateImage,
                                sameImage: false,
                                cancellationToken);
                        });
            }
            catch (ObjectDisposedException ex)
            {
                // Group and participant lifetime belongs to the snapshot
                // issuer. Release is detected before the callback runs, so no
                // pair was merged and no retrieval budget was reserved for
                // this participant: the evidence already ranked stays intact
                // beside this library's visible incomplete coverage.
                search.AddLibrary(
                    ReleasedLibrary(identity, entry, ex));
                continue;
            }

            search.AddLibrary(
                access switch
                {
                    AssemblyImageAccessResult<
                        StructuralCloneSearchLibraryCoverage>
                        .Available available =>
                            available.Value,
                    AssemblyImageAccessResult<
                        StructuralCloneSearchLibraryCoverage>
                        .Rejected rejected =>
                            UnavailableLibrary(
                                identity,
                                entry,
                                rejected.Failure),
                    _ => throw new InvalidOperationException(
                        "Unknown candidate image access result."),
                });
        }

        return search.Complete(self.Subject);
    }

    /// <summary>
    /// Preflights the participant bound over the snapshot's exact order,
    /// returning the snapshot positions excluded whole.
    /// </summary>
    /// <remarks>
    /// The containing library is always the first admitted participant, so a
    /// bound of one keeps <c>Self</c> rather than whichever entry the snapshot
    /// owner happened to list first.
    /// </remarks>
    static FrozenSet<int> SelectParticipantsBeyondLimit(
        StructuralCloneParticipantSnapshot snapshot,
        StructuralCloneCandidateBreadth breadth,
        WorkspaceStructuralCloneSearchLimits limits)
    {
        var excluded = new HashSet<int>();
        int admitted = 1;
        for (int index = 0; index < snapshot.Entries.Length; index++)
        {
            StructuralCloneParticipantEntry entry =
                snapshot.Entries[index];
            if (ReferenceEquals(entry, snapshot.ContainingLibrary)
                || !entry.AdmittedBy(breadth))
            {
                continue;
            }
            if (admitted >= limits.MaximumParticipants)
            {
                excluded.Add(index);
                continue;
            }

            admitted++;
        }

        return excluded.ToFrozenSet();
    }

    /// <summary>
    /// Ranks every seed against its own admitted candidate population inside
    /// one participant and merges the results into the shared global ranking.
    /// </summary>
    static StructuralCloneSearchLibraryCoverage SearchParticipant(
        SearchState search,
        StructuralCloneParticipantIdentity identity,
        StructuralCloneParticipantEntry entry,
        PEReader seedImage,
        PEReader candidateImage,
        bool sameImage,
        CancellationToken cancellationToken)
    {
        CandidatePopulation population;
        try
        {
            population =
                search.ResolveCandidates(
                    entry.Subject,
                    candidateImage,
                    cancellationToken);
        }
        catch (Exception ex)
            when (StructuralCloneMetadataResolution
                .IsMalformedMetadata(ex))
        {
            return new StructuralCloneSearchLibraryCoverage(
                identity,
                entry.Subject,
                entry.Membership,
                Admitted: true,
                CandidateMethods: 0,
                DiscoveredMethods: 0,
                RetrievalPairs: 0,
                NameComparisonWork: 0,
                [
                    new StructuralCloneSearchFailure(
                        StructuralCloneSearchFailureKind
                            .MetadataInspectionFailed,
                        entry.Subject,
                        ex.Message),
                ],
                []);
        }

        ImmutableArray<StructuralCloneSearchFailure> failures =
            population.Failures;
        if (!population.OverRetrievalBudget
            && population.Methods.Length
                > search.Limits.MaximumCandidateMethods)
        {
            return new StructuralCloneSearchLibraryCoverage(
                identity,
                entry.Subject,
                entry.Membership,
                Admitted: true,
                population.InspectedMethods,
                population.Methods.Length,
                RetrievalPairs: 0,
                population.NameComparisonWork,
                failures.Add(
                    new StructuralCloneSearchFailure(
                        StructuralCloneSearchFailureKind
                            .CandidatePopulationLimitReached,
                        entry.Subject,
                        $"Candidate population {population.Methods.Length} "
                            + "exceeds "
                            + $"{search.Limits.MaximumCandidateMethods}; "
                            + "the library contributed no ranked rows.")),
                []);
        }

        long pairs = population.RetrievalPairs;
        if (!search.TryReserveRetrievalPairs(pairs))
        {
            return new StructuralCloneSearchLibraryCoverage(
                identity,
                entry.Subject,
                entry.Membership,
                Admitted: false,
                population.InspectedMethods,
                population.Methods.Length,
                RetrievalPairs: 0,
                population.NameComparisonWork,
                failures.Add(
                    new StructuralCloneSearchFailure(
                        StructuralCloneSearchFailureKind
                            .RetrievalWorkLimitReached,
                        entry.Subject,
                        "The participant's seed-candidate pair population "
                            + (population.OverRetrievalBudget
                                ? $"passed {pairs} before admission stopped"
                                : $"of {pairs}")
                            + " does not fit the aggregate retrieval budget "
                            + $"of {search.Limits.MaximumRetrievalPairs}; "
                            + "the library was excluded whole and "
                            + "contributed no ranked rows.")),
                []);
        }

        var analysisBlockers = new LibraryAnalysisBlockers();
        if (pairs > 0)
        {
            foreach (SeedMethod seed in search.Seeds.Methods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CandidateGroup group = population.GroupFor(seed);
                if (group.Methods.IsEmpty)
                {
                    continue;
                }

                // One seed's candidate group is retrieved in consecutive
                // bounded chunks so cancellation is observed between units of
                // Analysis work rather than after one whole-population call.
                // Each chunk requests its complete ranked population and the
                // search merges every chunk into the same global ranking, so
                // the chunk size cannot reorder or drop a row.
                int chunkSize = search.Limits.MaximumRetrievalChunkMethods;
                for (int start = 0;
                    start < group.Methods.Length;
                    start += chunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int length =
                        Math.Min(
                            chunkSize,
                            group.Methods.Length - start);
                    ImmutableArray<MethodDefinitionHandle> chunk =
                        group.Methods.Slice(start, length);
                    var retrievalLimits =
                        new StructuralCloneRetrievalLimits(
                            MaximumMethods: search.Limits
                                .MaximumCandidateMethods,
                            MaximumResults: length,
                            ComparisonLimits: search.Limits
                                .ComparisonLimits);
                    StructuralCloneRetrievalResult retrieval =
                        sameImage
                            ? StructuralCloneAnalysis.RetrieveSimilar(
                                seedImage,
                                seed.Handle,
                                chunk,
                                retrievalLimits)
                            : StructuralCloneAnalysis.RetrieveSimilar(
                                seedImage,
                                seed.Handle,
                                candidateImage,
                                chunk,
                                retrievalLimits);
                    search.Merge(
                        seed,
                        identity,
                        entry,
                        group,
                        retrieval,
                        analysisBlockers);
                }
            }
        }

        return new StructuralCloneSearchLibraryCoverage(
            identity,
            entry.Subject,
            entry.Membership,
            Admitted: true,
            population.InspectedMethods,
            population.Methods.Length,
            pairs,
            population.NameComparisonWork,
            failures,
            analysisBlockers.Build());
    }

    /// <summary>
    /// The distinct Analysis blockers that omitted candidate methods of one
    /// participant.
    /// </summary>
    /// <remarks>
    /// One participant is retrieved once per seed and once per candidate
    /// chunk, and a blocker carries no subject, so an identical kind and
    /// detail from two retrievals is indistinguishable evidence and is
    /// recorded once. That keeps the reported blockers bounded by the distinct
    /// failures Analysis reported rather than by the seed and chunk counts.
    /// </remarks>
    sealed class LibraryAnalysisBlockers
    {
        readonly ImmutableArray<StructuralCloneRetrievalBlocker>.Builder
            _blockers =
                ImmutableArray.CreateBuilder<
                    StructuralCloneRetrievalBlocker>();
        readonly HashSet<StructuralCloneRetrievalBlocker> _seen = [];

        internal void Observe(StructuralCloneRetrievalResult retrieval)
        {
            foreach (StructuralCloneRetrievalBlocker blocker
                in retrieval.Blockers)
            {
                if (OmitsCandidates(blocker.Kind) && _seen.Add(blocker))
                {
                    _blockers.Add(blocker);
                }
            }
        }

        internal ImmutableArray<StructuralCloneRetrievalBlocker> Build()
            => _blockers.ToImmutable();

        /// <summary>
        /// Whether one blocker omitted candidate methods of the participant it
        /// was produced against.
        /// </summary>
        /// <remarks>
        /// A seed-side blocker is excluded: it reports that the seed produced
        /// no evidence anywhere, which belongs to that seed's coverage. Every
        /// other blocker reports candidate methods this participant did not
        /// contribute, so it makes this library's coverage incomplete.
        /// </remarks>
        static bool OmitsCandidates(
            StructuralCloneRetrievalBlockerKind kind)
            => kind
                is not StructuralCloneRetrievalBlockerKind.SeedUnsupported
                and not StructuralCloneRetrievalBlockerKind
                    .SeedProductionLimit
                and not StructuralCloneRetrievalBlockerKind
                    .SeedProductionFailure;
    }

    static StructuralCloneSearchLibraryCoverage UnavailableLibrary(
        StructuralCloneParticipantIdentity identity,
        StructuralCloneParticipantEntry entry,
        CandidateOpenFailure failure)
        => new(
            identity,
            entry.Subject,
            entry.Membership,
            Admitted: true,
            CandidateMethods: 0,
            DiscoveredMethods: 0,
            RetrievalPairs: 0,
            NameComparisonWork: 0,
            [
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind
                        .CandidateLibraryUnavailable,
                    entry.Subject,
                    $"{failure.Kind}: {failure.Detail}"),
            ],
            []);

    static StructuralCloneSearchLibraryCoverage ReleasedLibrary(
        StructuralCloneParticipantIdentity identity,
        StructuralCloneParticipantEntry entry,
        ObjectDisposedException failure)
        => new(
            identity,
            entry.Subject,
            entry.Membership,
            Admitted: true,
            CandidateMethods: 0,
            DiscoveredMethods: 0,
            RetrievalPairs: 0,
            NameComparisonWork: 0,
            [
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind
                        .CandidateLibraryReleased,
                    entry.Subject,
                    "The participant's assembly context group or participant "
                        + "was released before the search could read its "
                        + "immutable image, so it contributed no ranked row: "
                        + failure.Message),
            ],
            []);

    static SeedPopulation ResolveSeeds(
        PEReader seedImage,
        WorkspaceStructuralCloneSearchInput input,
        WorkspaceStructuralCloneSearchLimits limits)
    {
        StructuralCloneValidatedImage image =
            StructuralCloneValidatedImage.Create(seedImage);
        MetadataReader reader = image.Reader;
        TypeDefinitionHandle scope = default;
        FrozenSet<MethodDefinitionHandle> exact =
            FrozenSet<MethodDefinitionHandle>.Empty;
        switch (input.Seed)
        {
            case StructuralCloneSearchSeed.Library:
                break;
            case StructuralCloneSearchSeed.Type type:
            {
                StructuralCloneTypeResolution resolved =
                    StructuralCloneMetadataResolution.ResolveType(
                        reader,
                        type.Definition);
                if (SeedTypeFailure(resolved, type.Definition)
                    is { } failure)
                {
                    return SeedPopulation.Failed(failure);
                }

                scope = resolved.Handle;
                break;
            }
            case StructuralCloneSearchSeed.Member member:
            {
                // The Member seed population is every exact method body the
                // selected member occupies, so a property or event expands to
                // its associated accessor bodies while an explicit accessor
                // anchor stays one body.
                StructuralCloneMemberBodyResolution resolved =
                    StructuralCloneMetadataResolution.ResolveMemberBodies(
                        reader,
                        member.Definition,
                        member.MemberIdentity);
                if (SeedMemberFailure(resolved, member.Definition)
                    is { } failure)
                {
                    return SeedPopulation.Failed(failure);
                }

                exact = resolved.Methods.ToFrozenSet();
                break;
            }
            default:
                throw new InvalidOperationException(
                    $"Unknown structural-clone seed '{input.Seed.GetType().Name}'.");
        }

        bool namesRequired =
            input.Discovery
                == StructuralCloneCandidateDiscovery.SimilarNames;
        var methods = ImmutableArray.CreateBuilder<SeedMethod>();
        var names = new SeedNameIndexBuilder();
        foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
        {
            TypeDefinition definition =
                reader.GetTypeDefinition(typeHandle);
            bool wholeType =
                input.Seed is StructuralCloneSearchSeed.Library
                || typeHandle == scope;
            if (!wholeType
                && input.Seed is not StructuralCloneSearchSeed.Member)
            {
                continue;
            }

            string? typeName = null;
            bool typeNameDecoded = false;
            foreach (MethodDefinitionHandle methodHandle
                in definition.GetMethods())
            {
                if (!wholeType && !exact.Contains(methodHandle))
                {
                    continue;
                }
                if (methods.Count >= limits.MaximumSeedMethods)
                {
                    return SeedPopulation.Failed(
                        new StructuralCloneSearchFailure(
                            StructuralCloneSearchFailureKind
                                .SeedPopulationLimitReached,
                            null,
                            "The seed population exceeds "
                                + $"{limits.MaximumSeedMethods} methods."));
                }

                bool nameAdmitted = true;

                // Under All discovery every seed shares the participant's one
                // candidate group. Under SimilarNames a seed's group is its
                // own decoded (declaring type, member) name pair, so a
                // candidate admitted by one seed is never handed to another.
                int candidateGroup = 0;
                if (namesRequired)
                {
                    if (!typeNameDecoded)
                    {
                        typeNameDecoded = true;
                        typeName =
                            DecodeName(
                                reader,
                                definition.Name,
                                limits.MaximumNameCharacters,
                                stripArity: true);
                    }

                    string? methodName =
                        DecodeName(
                            reader,
                            reader.GetMethodDefinition(methodHandle).Name,
                            limits.MaximumNameCharacters,
                            stripArity: false);

                    // A seed whose own names cannot be decoded admits no
                    // candidate. That is visible per-seed coverage, not a
                    // silently narrowed search.
                    nameAdmitted =
                        typeName is not null && methodName is not null;
                    candidateGroup =
                        nameAdmitted
                            ? names.Add(typeName!, methodName!)
                            : SeedMethod.NoCandidateGroup;
                }

                methods.Add(
                    new SeedMethod(
                        methodHandle,
                        MetadataMethodAddress.Create(
                            reader,
                            methodHandle),
                        nameAdmitted,
                        candidateGroup));
            }
        }

        return SeedPopulation.Resolved(
            methods.ToImmutable(),
            names.Build());
    }

    static StructuralCloneSearchFailure? SeedTypeFailure(
        StructuralCloneTypeResolution resolution,
        MetadataTypeDefinitionName name)
        => resolution.Status switch
        {
            StructuralCloneTypeResolutionStatus.Resolved => null,
            StructuralCloneTypeResolutionStatus.NotFound =>
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind.SeedTypeNotFound,
                    null,
                    $"Type '{name.ToEscapedFullName()}' does not exist."),
            _ => new StructuralCloneSearchFailure(
                StructuralCloneSearchFailureKind.SeedTypeAmbiguous,
                null,
                $"Type '{name.ToEscapedFullName()}' is ambiguous."),
        };

    static StructuralCloneSearchFailure? SeedMemberFailure(
        StructuralCloneMemberBodyResolution resolution,
        MetadataTypeDefinitionName name)
        => resolution.Status switch
        {
            StructuralCloneMemberResolutionStatus.Resolved => null,
            StructuralCloneMemberResolutionStatus.TypeNotFound =>
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind.SeedTypeNotFound,
                    null,
                    $"Type '{name.ToEscapedFullName()}' does not exist."),
            StructuralCloneMemberResolutionStatus.TypeAmbiguous =>
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind.SeedTypeAmbiguous,
                    null,
                    $"Type '{name.ToEscapedFullName()}' is ambiguous."),
            StructuralCloneMemberResolutionStatus.MemberNotFound =>
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind.SeedMemberNotFound,
                    null,
                    "The exact seed member does not exist in the selected seed type."),
            StructuralCloneMemberResolutionStatus.MemberHasNoMethodBody =>
                new StructuralCloneSearchFailure(
                    StructuralCloneSearchFailureKind.SeedMemberHasNoMethodBody,
                    null,
                    "The exact seed member occupies no method body, so it "
                        + "supplies no seed method."),
            _ => new StructuralCloneSearchFailure(
                StructuralCloneSearchFailureKind.SeedMemberAmbiguous,
                null,
                "The exact seed member identifies more than one member."),
        };

    /// <summary>
    /// Decodes one metadata name only when it fits the per-name character
    /// bound, so an artifact-authored name cannot buy unbounded decode or
    /// edit-distance work.
    /// </summary>
    /// <remarks>
    /// The decoded name is preserved as decoded. Case folding belongs to
    /// comparison, not to decoding: folding here would lose the distinction
    /// between names the product must still treat as exactly equal.
    /// </remarks>
    static string? DecodeName(
        MetadataReader reader,
        StringHandle handle,
        int maximumCharacters,
        bool stripArity)
    {
        int remaining = maximumCharacters;
        if (!MetadataSafetyPolicy.TryReadTypeNameComponent(
                reader,
                handle,
                ref remaining,
                out string value))
        {
            return null;
        }

        return stripArity
            ? MetadataNameArity.StripFromFlattenedName(value)
            : value;
    }

    /// <summary>
    /// The invariant case-folded form of one decoded name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two names are exactly equal under
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> exactly when their
    /// folded forms are ordinally equal, because that comparison is defined by
    /// the same invariant uppercase mapping. Lowercasing is not equivalent:
    /// Greek capital sigma lowercases to the medial sigma while the final
    /// sigma lowercases to itself, so a lowercased comparison excludes two
    /// names the product promises to score <c>1.0</c>.
    /// </para>
    /// <para>
    /// Folding is therefore also the memoization currency: two names that must
    /// score identically share one cached score vector, and two names that
    /// must score differently never do.
    /// </para>
    /// </remarks>
    static string Fold(string name) => name.ToUpperInvariant();

    static WorkspaceStructuralCloneSearchResult Failed(
        AssemblyContextSubject subject,
        StructuralCloneSearchFailureKind kind,
        string detail)
        => new WorkspaceStructuralCloneSearchResult.Failed(
            subject,
            new StructuralCloneSearchFailure(kind, subject, detail));

    /// <summary>One resolved seed method and the candidate group it owns.</summary>
    /// <param name="CandidateGroup">
    /// The index of this seed's own candidate group inside one participant's
    /// <see cref="CandidatePopulation"/>. Under
    /// <see cref="StructuralCloneCandidateDiscovery.All"/> every seed shares
    /// group <c>0</c>; under
    /// <see cref="StructuralCloneCandidateDiscovery.SimilarNames"/> it is the
    /// seed's distinct decoded name pair, or
    /// <see cref="NoCandidateGroup"/> when the seed's own names could not be
    /// decoded.
    /// </param>
    internal sealed record SeedMethod(
        MethodDefinitionHandle Handle,
        MetadataMethodAddress Address,
        bool NameAdmitted,
        int CandidateGroup)
    {
        internal const int NoCandidateGroup = -1;
    }

    internal readonly record struct SeedNameKey(
        int DeclaringTypeIndex,
        int MemberIndex);

    /// <summary>
    /// Distinct seed declaring-type and member names plus the exact pairs that
    /// occur. Both conditions must hold for the same seed, so the pairs are
    /// kept rather than the two name sets alone, and each pair is one seed
    /// candidate group.
    /// </summary>
    internal sealed class SeedNameIndex
    {
        internal SeedNameIndex(
            ImmutableArray<string> declaringTypes,
            ImmutableArray<string> foldedDeclaringTypes,
            ImmutableArray<string> members,
            ImmutableArray<string> foldedMembers,
            ImmutableArray<SeedNameKey> pairs)
        {
            DeclaringTypes = declaringTypes;
            FoldedDeclaringTypes = foldedDeclaringTypes;
            Members = members;
            FoldedMembers = foldedMembers;
            Pairs = pairs;
        }

        internal ImmutableArray<string> DeclaringTypes { get; }
        internal ImmutableArray<string> FoldedDeclaringTypes { get; }
        internal ImmutableArray<string> Members { get; }
        internal ImmutableArray<string> FoldedMembers { get; }
        internal ImmutableArray<SeedNameKey> Pairs { get; }
    }

    /// <summary>
    /// Interns the distinct seed names by exact ordinal-ignore-case identity.
    /// </summary>
    /// <remarks>
    /// Two seed names that must score <c>1.0</c> against the same candidates
    /// are one entry, so a candidate cannot be admitted against one spelling
    /// of a name and excluded against another spelling of the same name.
    /// </remarks>
    sealed class SeedNameIndexBuilder
    {
        readonly Dictionary<string, int> _types =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, int> _members =
            new(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<SeedNameKey, int> _pairs = [];
        readonly ImmutableArray<string>.Builder _typeNames =
            ImmutableArray.CreateBuilder<string>();
        readonly ImmutableArray<string>.Builder _foldedTypeNames =
            ImmutableArray.CreateBuilder<string>();
        readonly ImmutableArray<string>.Builder _memberNames =
            ImmutableArray.CreateBuilder<string>();
        readonly ImmutableArray<string>.Builder _foldedMemberNames =
            ImmutableArray.CreateBuilder<string>();
        readonly ImmutableArray<SeedNameKey>.Builder _pairList =
            ImmutableArray.CreateBuilder<SeedNameKey>();

        /// <summary>Returns the candidate-group index for one seed name pair.</summary>
        internal int Add(string declaringType, string member)
        {
            if (!_types.TryGetValue(declaringType, out int typeIndex))
            {
                typeIndex = _typeNames.Count;
                _typeNames.Add(declaringType);
                _foldedTypeNames.Add(Fold(declaringType));
                _types.Add(declaringType, typeIndex);
            }
            if (!_members.TryGetValue(member, out int memberIndex))
            {
                memberIndex = _memberNames.Count;
                _memberNames.Add(member);
                _foldedMemberNames.Add(Fold(member));
                _members.Add(member, memberIndex);
            }

            var key = new SeedNameKey(typeIndex, memberIndex);
            if (_pairs.TryGetValue(key, out int group))
            {
                return group;
            }

            group = _pairList.Count;
            _pairList.Add(key);
            _pairs.Add(key, group);
            return group;
        }

        internal SeedNameIndex Build()
            => new(
                _typeNames.ToImmutable(),
                _foldedTypeNames.ToImmutable(),
                _memberNames.ToImmutable(),
                _foldedMemberNames.ToImmutable(),
                _pairList.ToImmutable());
    }

    internal sealed record SeedPopulation(
        ImmutableArray<SeedMethod> Methods,
        SeedNameIndex Names,
        StructuralCloneSearchFailure? Failure)
    {
        internal FrozenSet<MethodDefinitionHandle> Handles { get; init; } =
            FrozenSet<MethodDefinitionHandle>.Empty;

        internal static SeedPopulation Resolved(
            ImmutableArray<SeedMethod> methods,
            SeedNameIndex names)
            => new(methods, names, null)
            {
                Handles = methods
                    .Select(static method => method.Handle)
                    .ToFrozenSet(),
            };

        internal static SeedPopulation Failed(
            StructuralCloneSearchFailure failure)
            => new(
                [],
                new SeedNameIndex([], [], [], [], []),
                failure);
    }

    /// <summary>
    /// One seed candidate group inside one participant: the exact candidate
    /// methods admitted for that seed, and the name evidence that admitted
    /// each of them against that seed.
    /// </summary>
    internal sealed record CandidateGroup(
        ImmutableArray<MethodDefinitionHandle> Methods,
        FrozenDictionary<
            MethodDefinitionHandle,
            StructuralCloneNameQualification> Qualifications)
    {
        internal static CandidateGroup Empty { get; } =
            new(
                [],
                FrozenDictionary<
                    MethodDefinitionHandle,
                    StructuralCloneNameQualification>.Empty);
    }

    /// <summary>
    /// The candidate methods one participant contributes, plus the visible
    /// cost and coverage of producing them.
    /// </summary>
    /// <param name="Methods">
    /// The union of every group's methods, used for the per-library candidate
    /// bound and coverage. It is not a retrieval population: a seed is only
    /// ever ranked against its own <see cref="Groups"/> entry.
    /// </param>
    /// <param name="RetrievalPairs">
    /// The exact seed-candidate pairs these groups would submit to Analysis,
    /// or the running total at the point admission abandoned the participant.
    /// </param>
    /// <param name="OverRetrievalBudget">
    /// Admission stopped because the participant's pair population already
    /// exceeded the search's remaining aggregate retrieval budget. The groups
    /// are partial and the participant is excluded whole.
    /// </param>
    internal sealed record CandidatePopulation(
        ImmutableArray<MethodDefinitionHandle> Methods,
        ImmutableArray<CandidateGroup> Groups,
        int InspectedMethods,
        long RetrievalPairs,
        bool OverRetrievalBudget,
        long NameComparisonWork,
        ImmutableArray<StructuralCloneSearchFailure> Failures)
    {
        internal CandidateGroup GroupFor(SeedMethod seed)
            => (uint)seed.CandidateGroup < (uint)Groups.Length
                ? Groups[seed.CandidateGroup]
                : CandidateGroup.Empty;
    }

    /// <summary>
    /// Charges normalized-similarity work so a hostile name population cannot
    /// buy quadratic edit-distance work under a bounded request.
    /// </summary>
    sealed class NameWorkBudget(long maximum)
    {
        long _remaining = maximum;

        internal long Charged { get; private set; }
        internal bool Exhausted { get; private set; }

        internal bool TryChargeUnits(long work)
        {
            if (Exhausted || work > _remaining)
            {
                Exhausted = true;
                return false;
            }

            _remaining -= work;
            Charged += work;
            return true;
        }
    }

    /// <summary>
    /// Bounded memoization of decoded-name score vectors for one participant,
    /// shared by the declaring-type and member name populations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retained memory is bounded on both axes a large seed population can
    /// grow: the entry count bounds the retained decoded-name keys, and the
    /// shared cell budget bounds the retained score vectors, which are one
    /// cell per distinct seed name each. A vector that does not fit the
    /// remaining cells whole is not retained, so a partial vector can never be
    /// served.
    /// </para>
    /// <para>
    /// The cache changes no admission decision. Callers charge the whole
    /// logical comparison work of a name before consulting it, so a hit and a
    /// miss cost the shared budget the same amount and the cache only avoids
    /// recomputing <see cref="StringDistance"/>. Names are keyed by exact
    /// ordinal-ignore-case identity, which is the same equivalence the scores
    /// themselves respect.
    /// </para>
    /// </remarks>
    sealed class NameScoreCache(long maximumCells, int maximumEntries)
    {
        long _remainingCells = maximumCells;
        int _remainingEntries = maximumEntries;

        internal Dictionary<string, double[]> DeclaringTypes { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, double[]> Members { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        internal void Retain(
            Dictionary<string, double[]> cache,
            string name,
            double[] scores)
        {
            if (_remainingEntries <= 0 || scores.Length > _remainingCells)
            {
                return;
            }
            if (!cache.TryAdd(name, scores))
            {
                return;
            }

            _remainingEntries--;
            _remainingCells -= scores.Length;
        }
    }

    /// <summary>
    /// Shared seed population, ranking accumulator, and coverage for one
    /// search.
    /// </summary>
    sealed class SearchState(
        WorkspaceStructuralCloneSearchInput input,
        WorkspaceStructuralCloneSearchLimits limits,
        SeedPopulation seeds)
    {
        readonly List<StructuralCloneSearchPair> _rows = [];
        readonly Dictionary<
            MethodDefinitionHandle,
            SeedCoverageState> _seedCoverage = [];
        readonly List<StructuralCloneSearchLibraryCoverage> _libraries = [];
        readonly NameWorkBudget _nameWork =
            new(limits.MaximumNameComparisonWork);
        readonly int _trimThreshold =
            (int)Math.Min(
                (long)limits.MaximumResults * 4L + 64L,
                1L << 20);
        int _ranked;
        int _suppressed;
        int _retrievalCalls;
        int _candidateMethods;
        int _discoveredMethods;
        int _admittedLibraries;
        int _excludedLibraries;
        long _retrievalPairs;
        long _retrievalPairBudget = limits.MaximumRetrievalPairs;

        /// <summary>
        /// The seeds sharing each candidate group. One admitted candidate in
        /// group <c>g</c> costs <c>_seedsPerGroup[g]</c> retrieval pairs, so
        /// admission can charge the aggregate budget as it builds.
        /// </summary>
        readonly int[] _seedsPerGroup = SeedsPerGroup(input, seeds);

        internal WorkspaceStructuralCloneSearchLimits Limits => limits;
        internal SeedPopulation Seeds => seeds;

        static int[] SeedsPerGroup(
            WorkspaceStructuralCloneSearchInput input,
            SeedPopulation seeds)
        {
            if (input.Discovery == StructuralCloneCandidateDiscovery.All)
            {
                return [seeds.Methods.Length];
            }

            var counts = new int[seeds.Names.Pairs.Length];
            foreach (SeedMethod seed in seeds.Methods)
            {
                if ((uint)seed.CandidateGroup < (uint)counts.Length)
                {
                    counts[seed.CandidateGroup]++;
                }
            }

            return counts;
        }

        /// <summary>
        /// The aggregate retrieval budget latched. Every remaining admitted
        /// participant is excluded whole rather than partially evaluated.
        /// </summary>
        internal bool RetrievalBudgetExhausted { get; private set; }

        internal void AddExcludedLibrary(
            StructuralCloneParticipantIdentity identity,
            StructuralCloneParticipantEntry entry)
        {
            _excludedLibraries++;
            _libraries.Add(
                new StructuralCloneSearchLibraryCoverage(
                    identity,
                    entry.Subject,
                    entry.Membership,
                    Admitted: false,
                    CandidateMethods: 0,
                    DiscoveredMethods: 0,
                    RetrievalPairs: 0,
                    NameComparisonWork: 0,
                    [],
                    []));
        }

        /// <summary>
        /// Records one whole-unit exclusion forced by an aggregate work bound.
        /// The failure keeps the omitted pair population visible, so the
        /// result cannot present itself as a complete global top N.
        /// </summary>
        internal void AddLimitExcludedLibrary(
            StructuralCloneParticipantIdentity identity,
            StructuralCloneParticipantEntry entry,
            StructuralCloneSearchFailureKind kind,
            string detail)
        {
            _excludedLibraries++;
            _libraries.Add(
                new StructuralCloneSearchLibraryCoverage(
                    identity,
                    entry.Subject,
                    entry.Membership,
                    Admitted: false,
                    CandidateMethods: 0,
                    DiscoveredMethods: 0,
                    RetrievalPairs: 0,
                    NameComparisonWork: 0,
                    [
                        new StructuralCloneSearchFailure(
                            kind,
                            entry.Subject,
                            detail),
                    ],
                    []));
        }

        internal void AddLibrary(
            StructuralCloneSearchLibraryCoverage coverage)
        {
            _libraries.Add(coverage);
            if (!coverage.Admitted)
            {
                _excludedLibraries++;
                return;
            }

            _admittedLibraries++;
            _candidateMethods += coverage.CandidateMethods;
            _discoveredMethods += coverage.DiscoveredMethods;
        }

        /// <summary>
        /// Reserves one participant's whole pair population against the
        /// aggregate budget, latching exhaustion so no later participant is
        /// opened or partially evaluated.
        /// </summary>
        internal bool TryReserveRetrievalPairs(long pairs)
        {
            if (RetrievalBudgetExhausted)
            {
                return false;
            }
            if (pairs > _retrievalPairBudget)
            {
                RetrievalBudgetExhausted = true;
                return false;
            }

            _retrievalPairBudget -= pairs;
            _retrievalPairs += pairs;
            return true;
        }

        /// <summary>
        /// Selects the candidate methods one participant contributes, applying
        /// name discovery before any method body is produced.
        /// </summary>
        internal CandidatePopulation ResolveCandidates(
            AssemblyContextSubject subject,
            PEReader candidateImage,
            CancellationToken cancellationToken)
        {
            StructuralCloneValidatedImage image =
                StructuralCloneValidatedImage.Create(candidateImage);
            MetadataReader reader = image.Reader;
            if (input.Discovery == StructuralCloneCandidateDiscovery.All)
            {
                ImmutableArray<MethodDefinitionHandle> all =
                    reader.MethodDefinitions.ToImmutableArray();
                long allPairs = (long)all.Length * _seedsPerGroup[0];
                return new CandidatePopulation(
                    all,
                    [
                        new CandidateGroup(
                            all,
                            FrozenDictionary<
                                MethodDefinitionHandle,
                                StructuralCloneNameQualification>.Empty),
                    ],
                    all.Length,
                    allPairs,
                    allPairs > _retrievalPairBudget,
                    NameComparisonWork: 0,
                    []);
            }

            long chargedBefore = _nameWork.Charged;
            var union =
                ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
            var groups =
                new CandidateGroupBuilder[seeds.Names.Pairs.Length];
            for (int group = 0; group < groups.Length; group++)
            {
                groups[group] = new CandidateGroupBuilder();
            }

            var cache =
                new NameScoreCache(
                    limits.MaximumNameCacheCells,
                    MaximumNameCacheEntries);
            int inspected = 0;
            int undecodableNames = 0;

            // Group construction charges the aggregate retrieval budget as it
            // builds, so a hostile name population cannot materialize a
            // quadratic admitted-pair structure before the bound is checked.
            long pairs = 0;
            bool overBudget = false;
            foreach (TypeDefinitionHandle typeHandle
                in reader.TypeDefinitions)
            {
                if (overBudget)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                TypeDefinition definition =
                    reader.GetTypeDefinition(typeHandle);
                string? typeName =
                    DecodeName(
                        reader,
                        definition.Name,
                        limits.MaximumNameCharacters,
                        stripArity: true);
                double[]? typeScores =
                    typeName is null
                        ? null
                        : Score(
                            typeName,
                            seeds.Names.DeclaringTypes,
                            seeds.Names.FoldedDeclaringTypes,
                            cache,
                            cache.DeclaringTypes);
                foreach (MethodDefinitionHandle methodHandle
                    in definition.GetMethods())
                {
                    inspected++;

                    // An undecodable or unscored name excludes the method
                    // and stays visible; it never becomes a silent absence.
                    if (typeName is null)
                    {
                        undecodableNames++;
                        continue;
                    }
                    if (typeScores is null)
                    {
                        continue;
                    }

                    string? memberName =
                        DecodeName(
                            reader,
                            reader.GetMethodDefinition(methodHandle).Name,
                            limits.MaximumNameCharacters,
                            stripArity: false);
                    if (memberName is null)
                    {
                        undecodableNames++;
                        continue;
                    }

                    double[]? memberScores =
                        Score(
                            memberName,
                            seeds.Names.Members,
                            seeds.Names.FoldedMembers,
                            cache,
                            cache.Members);
                    if (memberScores is null)
                    {
                        continue;
                    }

                    if (Qualify(
                            typeScores,
                            memberScores,
                            methodHandle,
                            groups,
                            ref pairs,
                            out overBudget))
                    {
                        union.Add(methodHandle);
                    }
                    if (overBudget)
                    {
                        break;
                    }
                }
            }

            var failures =
                ImmutableArray.CreateBuilder<
                    StructuralCloneSearchFailure>();
            if (undecodableNames > 0)
            {
                failures.Add(
                    new StructuralCloneSearchFailure(
                        StructuralCloneSearchFailureKind.NameDecodeFailed,
                        subject,
                        $"{undecodableNames} candidate methods carry names "
                            + "that could not be decoded within "
                            + $"{limits.MaximumNameCharacters} characters; "
                            + "they were not admitted."));
            }
            if (_nameWork.Exhausted)
            {
                failures.Add(
                    new StructuralCloneSearchFailure(
                        StructuralCloneSearchFailureKind
                            .NameWorkLimitReached,
                        subject,
                        "The name-similarity work budget of "
                            + $"{limits.MaximumNameComparisonWork} cells was "
                            + "exhausted; candidate admission is incomplete."));
            }

            return new CandidatePopulation(
                union.ToImmutable(),
                [.. groups.Select(static group => group.Build())],
                inspected,
                pairs,
                overBudget,
                _nameWork.Charged - chargedBefore,
                failures.ToImmutable());
        }

        /// <summary>
        /// Scores one decoded candidate name against every distinct seed name,
        /// returning null when the bounded name work is exhausted.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A name exactly equal to a seed name under
        /// <see cref="StringComparison.OrdinalIgnoreCase"/> scores <c>1.0</c>
        /// at no cost; every other comparison folds both names invariantly and
        /// charges its edit-distance cells before
        /// <see cref="StringDistance.Similarity"/> runs.
        /// </para>
        /// <para>
        /// The whole vector's logical work is charged before the cache is
        /// consulted, and the charge is the same on a hit and on a miss. The
        /// name-work budget therefore reaches exhaustion at the same candidate
        /// for every cache capacity, which is what keeps
        /// <see cref="WorkspaceStructuralCloneSearchLimits.MaximumNameCacheCells"/>
        /// a pure optimization: it can change how much
        /// <see cref="StringDistance"/> runs, never which candidates a bounded
        /// search admits.
        /// </para>
        /// <para>
        /// The charge is also atomic per name. A vector that does not fit the
        /// remaining budget latches exhaustion instead of admitting the
        /// candidate on a partially scored vector.
        /// </para>
        /// </remarks>
        double[]? Score(
            string candidate,
            ImmutableArray<string> seedNames,
            ImmutableArray<string> foldedSeedNames,
            NameScoreCache cache,
            Dictionary<string, double[]> names)
        {
            if (_nameWork.Exhausted)
            {
                return null;
            }

            string folded = Fold(candidate);
            long work = 0;
            for (int index = 0; index < seedNames.Length; index++)
            {
                if (string.Equals(
                        candidate,
                        seedNames[index],
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                work +=
                    ((long)folded.Length + 1)
                    * (foldedSeedNames[index].Length + 1);
            }

            if (!_nameWork.TryChargeUnits(work))
            {
                return null;
            }
            if (names.TryGetValue(candidate, out double[]? cached))
            {
                return cached;
            }

            var scores = new double[seedNames.Length];
            for (int index = 0; index < seedNames.Length; index++)
            {
                scores[index] =
                    string.Equals(
                        candidate,
                        seedNames[index],
                        StringComparison.OrdinalIgnoreCase)
                        ? 1.0
                        : StringDistance.Similarity(
                            folded,
                            foldedSeedNames[index]);
            }

            cache.Retain(names, candidate, scores);
            return scores;
        }

        /// <summary>
        /// Records one candidate in the group of every seed name pair that
        /// clears the fixed threshold on both its declaring-type and member
        /// name, with that pair's own scores as the admission evidence.
        /// </summary>
        /// <remarks>
        /// Admission is per seed-candidate pair. A candidate that qualifies
        /// only against seed A joins only seed A's group, so seed B never
        /// retrieves it. A candidate that qualifies against several seeds
        /// joins each of their groups and is ranked against each of them under
        /// that pair's own evidence.
        /// </remarks>
        /// <returns>
        /// Whether the candidate joined at least one seed's group.
        /// </returns>
        bool Qualify(
            double[] typeScores,
            double[] memberScores,
            MethodDefinitionHandle candidate,
            CandidateGroupBuilder[] groups,
            ref long pairs,
            out bool overBudget)
        {
            overBudget = false;

            // The admission scan itself is quadratic in seed and candidate
            // populations, so it charges the same visible name-work budget as
            // the edit-distance comparisons.
            if (!_nameWork.TryChargeUnits(seeds.Names.Pairs.Length + 1))
            {
                return false;
            }

            bool admitted = false;
            for (int group = 0; group < groups.Length; group++)
            {
                SeedNameKey key = seeds.Names.Pairs[group];
                double type = typeScores[key.DeclaringTypeIndex];
                double member = memberScores[key.MemberIndex];
                if (type < NameSimilarityThreshold
                    || member < NameSimilarityThreshold)
                {
                    continue;
                }

                pairs += _seedsPerGroup[group];
                if (pairs > _retrievalPairBudget)
                {
                    overBudget = true;
                    return admitted;
                }

                groups[group].Add(
                    candidate,
                    new StructuralCloneNameQualification(type, member));
                admitted = true;
            }

            return admitted;
        }

        internal void Merge(
            SeedMethod seed,
            StructuralCloneParticipantIdentity candidateIdentity,
            StructuralCloneParticipantEntry candidateEntry,
            CandidateGroup group,
            StructuralCloneRetrievalResult retrieval,
            LibraryAnalysisBlockers analysisBlockers)
        {
            _retrievalCalls++;
            SeedCoverageState coverage = SeedCoverage(seed);
            coverage.Observe(retrieval);

            // The same retrieval is evidence for two owners: the seed it ran
            // for, and the participant whose candidate methods it produced.
            analysisBlockers.Observe(retrieval);
            bool orientationOrdered =
                input.Seed is not StructuralCloneSearchSeed.Member;
            bool sameLibrary =
                ReferenceEquals(
                    candidateIdentity,
                    input.Participants.ContainingLibraryIdentity);
            foreach (StructuralCloneRetrievalCandidate candidate
                in retrieval.Candidates)
            {
                if (sameLibrary)
                {
                    if (candidate.Method.Handle == seed.Handle)
                    {
                        coverage.Suppress();
                        _suppressed++;
                        continue;
                    }
                    if (orientationOrdered
                        && seeds.Handles.Contains(candidate.Method.Handle)
                        && MetadataTokens.GetRowNumber(seed.Handle)
                            > MetadataTokens.GetRowNumber(
                                candidate.Method.Handle))
                    {
                        coverage.Suppress();
                        _suppressed++;
                        continue;
                    }
                }

                group.Qualifications.TryGetValue(
                    candidate.Method.Handle,
                    out StructuralCloneNameQualification? qualification);
                Add(
                    new StructuralCloneSearchPair(
                        Rank: 0,
                        coverage.Endpoint,
                        new StructuralCloneSearchEndpoint(
                            candidateIdentity,
                            candidateEntry,
                            candidate.Method),
                        candidate.Similarity,
                        qualification));
                coverage.Rank();
            }
        }

        void Add(StructuralCloneSearchPair pair)
        {
            _ranked++;
            _rows.Add(pair);
            if (_rows.Count >= _trimThreshold
                && _rows.Count > limits.MaximumResults)
            {
                Trim();
            }
        }

        void Trim()
        {
            _rows.Sort(StructuralCloneSearchPairComparer.Instance);
            if (_rows.Count > limits.MaximumResults)
            {
                _rows.RemoveRange(
                    limits.MaximumResults,
                    _rows.Count - limits.MaximumResults);
            }
        }

        SeedCoverageState SeedCoverage(SeedMethod seed)
        {
            if (!_seedCoverage.TryGetValue(
                    seed.Handle,
                    out SeedCoverageState? state))
            {
                state =
                    new SeedCoverageState(
                        new StructuralCloneSearchEndpoint(
                            input.Participants.ContainingLibraryIdentity,
                            input.Participants.ContainingLibrary,
                            seed.Address),
                        seed.NameAdmitted
                            ? []
                            :
                            [
                                new StructuralCloneSearchFailure(
                                    StructuralCloneSearchFailureKind
                                        .NameDecodeFailed,
                                    input.Participants.ContainingLibrary
                                        .Subject,
                                    "The seed method's declaring-type or "
                                        + "member name could not be decoded "
                                        + "within "
                                        + $"{limits.MaximumNameCharacters} "
                                        + "characters, so it admitted no "
                                        + "candidate by name."),
                            ]);
                _seedCoverage.Add(seed.Handle, state);
            }

            return state;
        }

        internal WorkspaceStructuralCloneSearchResult Complete(
            AssemblyContextSubject seedSubject)
        {
            Trim();
            ImmutableArray<StructuralCloneSearchPair> pairs =
            [
                .. _rows.Select(
                    (row, index) => row with { Rank = index + 1 }),
            ];
            ImmutableArray<StructuralCloneSearchSeedCoverage> seedCoverage =
            [
                .. seeds.Methods.Select(
                    seed => SeedCoverage(seed).Build()),
            ];
            return new WorkspaceStructuralCloneSearchResult.Available(
                seedSubject,
                input.Seed,
                input.Breadth,
                input.Discovery,
                NameSimilarityThreshold,
                limits,
                input.Participants.StartingRevision,
                input.Participants.EffectiveRevision,
                input.Participants.Identity,
                pairs,
                seedCoverage,
                [.. _libraries],
                new StructuralCloneSearchReceipt(
                    seeds.Methods.Length,
                    _admittedLibraries,
                    _excludedLibraries,
                    _candidateMethods,
                    _discoveredMethods,
                    _retrievalPairs,
                    _retrievalCalls,
                    _ranked,
                    _suppressed,
                    pairs.Length,
                    _ranked > pairs.Length));
        }
    }

    /// <summary>Accumulates one seed's candidate group for one participant.</summary>
    sealed class CandidateGroupBuilder
    {
        readonly ImmutableArray<MethodDefinitionHandle>.Builder _methods =
            ImmutableArray.CreateBuilder<MethodDefinitionHandle>();
        readonly Dictionary<
            MethodDefinitionHandle,
            StructuralCloneNameQualification> _qualifications = [];

        internal void Add(
            MethodDefinitionHandle method,
            StructuralCloneNameQualification qualification)
        {
            _methods.Add(method);
            _qualifications.Add(method, qualification);
        }

        internal CandidateGroup Build()
            => _methods.Count == 0
                ? CandidateGroup.Empty
                : new CandidateGroup(
                    _methods.ToImmutable(),
                    _qualifications.ToFrozenDictionary());
    }

    sealed class SeedCoverageState(
        StructuralCloneSearchEndpoint endpoint,
        ImmutableArray<StructuralCloneSearchFailure> failures)
    {
        readonly ImmutableArray<StructuralCloneRetrievalBlocker>.Builder
            _blockers =
                ImmutableArray.CreateBuilder<
                    StructuralCloneRetrievalBlocker>();
        readonly HashSet<StructuralCloneRetrievalBlocker> _seenBlockers = [];
        StructuralCloneRetrievalDisposition _disposition =
            StructuralCloneRetrievalDisposition.Completed;
        int _ranked;
        int _suppressed;

        internal StructuralCloneSearchEndpoint Endpoint => endpoint;

        /// <summary>
        /// Folds one retrieval outcome into this seed's coverage.
        /// </summary>
        /// <remarks>
        /// One seed observes many retrievals — one per candidate chunk per
        /// participant — and a blocker carries no subject, so an identical
        /// kind and detail from two retrievals is indistinguishable evidence.
        /// It is recorded once, which keeps the reported blockers bounded by
        /// the distinct failures Analysis reported rather than by the chunk
        /// and participant counts. The most severe disposition still wins, so
        /// no failure becomes success-shaped.
        /// </remarks>
        internal void Observe(StructuralCloneRetrievalResult retrieval)
        {
            if (Severity(retrieval.Disposition) > Severity(_disposition))
            {
                _disposition = retrieval.Disposition;
            }

            foreach (StructuralCloneRetrievalBlocker blocker
                in retrieval.Blockers)
            {
                if (_seenBlockers.Add(blocker))
                {
                    _blockers.Add(blocker);
                }
            }
        }

        internal void Rank() => _ranked++;

        internal void Suppress() => _suppressed++;

        internal StructuralCloneSearchSeedCoverage Build()
            => new(
                endpoint,
                _disposition,
                _ranked,
                _suppressed,
                _blockers.ToImmutable(),
                failures);

        static int Severity(StructuralCloneRetrievalDisposition disposition)
            => disposition switch
            {
                StructuralCloneRetrievalDisposition.Completed => 0,
                StructuralCloneRetrievalDisposition.Unsupported => 1,
                StructuralCloneRetrievalDisposition.LimitReached => 2,
                _ => 3,
            };
    }

    /// <summary>
    /// The single global ranking order: the Analysis score and its existing
    /// component order, then the exact left and right endpoint identities.
    /// </summary>
    /// <remarks>
    /// Endpoint identity is the snapshot's opaque participant identity and the
    /// physical method address. The participant ordinal is the snapshot's
    /// owner-issued snapshot-local order, which is part of that snapshot's
    /// exact identity; both endpoints of one search always come from the same
    /// snapshot, so the ordinal is a total order over distinct registrations.
    /// </remarks>
    sealed class StructuralCloneSearchPairComparer
        : IComparer<StructuralCloneSearchPair>
    {
        internal static StructuralCloneSearchPairComparer Instance { get; } =
            new();

        public int Compare(
            StructuralCloneSearchPair? left,
            StructuralCloneSearchPair? right)
        {
            ArgumentNullException.ThrowIfNull(left);
            ArgumentNullException.ThrowIfNull(right);
            int result =
                right.Similarity.Score.CompareTo(left.Similarity.Score);
            if (result != 0) return result;
            result = right.Similarity.OperationScore.CompareTo(
                left.Similarity.OperationScore);
            if (result != 0) return result;
            result = right.Similarity.PositionScore.CompareTo(
                left.Similarity.PositionScore);
            if (result != 0) return result;
            result = right.Similarity.BlockScore.CompareTo(
                left.Similarity.BlockScore);
            if (result != 0) return result;
            result = right.Similarity.EdgeScore.CompareTo(
                left.Similarity.EdgeScore);
            if (result != 0) return result;
            result = right.Similarity.LocalScore.CompareTo(
                left.Similarity.LocalScore);
            if (result != 0) return result;
            result = CompareEndpoints(left.Left, right.Left);
            return result != 0
                ? result
                : CompareEndpoints(left.Right, right.Right);
        }

        static int CompareEndpoints(
            StructuralCloneSearchEndpoint left,
            StructuralCloneSearchEndpoint right)
        {
            int result =
                left.Participant.Ordinal.CompareTo(
                    right.Participant.Ordinal);
            return result != 0
                ? result
                : MetadataTokens.GetRowNumber(left.Method.Handle)
                    .CompareTo(
                        MetadataTokens.GetRowNumber(
                            right.Method.Handle));
        }
    }
}
