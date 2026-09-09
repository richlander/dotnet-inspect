using System.Collections.Immutable;
using System.Globalization;

using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Presentation;

/// <summary>The selected subject population for one Clone Candidates search.</summary>
public enum CloneCandidateSeedKind
{
    Library,
    Type,
    Member,
}

/// <summary>Portable seed coordinates for one Clone Candidates search.</summary>
public sealed record CloneCandidateSeed
{
    public CloneCandidateSeed(
        CloneCandidateSeedKind kind,
        MetadataTypeDefinitionName? type,
        MemberAnchor? member)
    {
        bool valid = kind switch
        {
            CloneCandidateSeedKind.Library =>
                type is null && member is null,
            CloneCandidateSeedKind.Type =>
                type is not null && member is null,
            CloneCandidateSeedKind.Member =>
                type is not null && member is not null,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException(
                "The seed coordinates do not match the seed kind.");
        }

        Kind = kind;
        Type = type;
        Member = member;
    }

    public CloneCandidateSeedKind Kind { get; }
    public MetadataTypeDefinitionName? Type { get; }
    public MemberAnchor? Member { get; }
}

/// <summary>
/// One snapshot-relative portable participant identity.
/// </summary>
/// <remarks>
/// The ordinal preserves distinct registrations of identical bytes inside one
/// document without serializing the Query's process-local registration or
/// participant handles.
/// </remarks>
public sealed record CloneCandidateParticipantIdentity(
    int Ordinal,
    AssemblyReferenceIdentity Assembly,
    AssemblyResolutionProvenance Provenance,
    Guid? ModuleVersionId)
{
    public int Ordinal { get; } = Ordinal >= 0
        ? Ordinal
        : throw new ArgumentOutOfRangeException(nameof(Ordinal));

    public AssemblyReferenceIdentity Assembly { get; } =
        Assembly ?? throw new ArgumentNullException(nameof(Assembly));

    public AssemblyResolutionProvenance Provenance { get; } =
        Provenance ?? throw new ArgumentNullException(nameof(Provenance));
}

/// <summary>One exact portable method endpoint in a candidate pair.</summary>
public sealed record CloneCandidateMethodIdentity
{
    public CloneCandidateMethodIdentity(
        CloneCandidateParticipantIdentity participant,
        Guid moduleVersionId,
        int methodDefinitionToken,
        InertString addressDisplay)
    {
        if ((methodDefinitionToken & unchecked((int)0xFF000000))
            != 0x06000000
            || (methodDefinitionToken & 0x00FFFFFF) == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(methodDefinitionToken));
        }

        Participant =
            participant
            ?? throw new ArgumentNullException(nameof(participant));
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A method endpoint requires a module version ID.",
                nameof(moduleVersionId));
        }
        ModuleVersionId = moduleVersionId;
        MethodDefinitionToken = methodDefinitionToken;
        AddressDisplay = addressDisplay;
    }

    public CloneCandidateParticipantIdentity Participant { get; }
    public Guid ModuleVersionId { get; }
    public int MethodDefinitionToken { get; }
    public InertString AddressDisplay { get; }
}

/// <summary>Portable structural similarity components for one candidate row.</summary>
public sealed record CloneCandidateSimilarity(
    int Score,
    int OperationScore,
    int PositionScore,
    int BlockScore,
    int EdgeScore,
    int LocalScore,
    int SeedInstructions,
    int CandidateInstructions,
    int SeedBlocks,
    int CandidateBlocks,
    int SeedEdges,
    int CandidateEdges,
    int SeedLocals,
    int CandidateLocals);

/// <summary>Optional SimilarNames admission evidence for one pair.</summary>
public sealed record CloneCandidateNameQualification(
    double DeclaringTypeSimilarity,
    double MemberSimilarity);

/// <summary>
/// One globally ranked retrieval candidate. It is not a checked clone relation.
/// </summary>
public sealed record CloneCandidateRow(
    int Rank,
    CloneCandidateMethodIdentity Left,
    CloneCandidateMethodIdentity Right,
    CloneCandidateSimilarity Similarity,
    CloneCandidateNameQualification? NameQualification)
{
    public int Rank { get; } = Rank > 0
        ? Rank
        : throw new ArgumentOutOfRangeException(nameof(Rank));

    public CloneCandidateMethodIdentity Left { get; } =
        Left ?? throw new ArgumentNullException(nameof(Left));

    public CloneCandidateMethodIdentity Right { get; } =
        Right ?? throw new ArgumentNullException(nameof(Right));

    public CloneCandidateSimilarity Similarity { get; } =
        Similarity ?? throw new ArgumentNullException(nameof(Similarity));
}

/// <summary>One visible Clone Candidates search failure.</summary>
public sealed record CloneCandidateFailure(
    StructuralCloneSearchFailureKind Kind,
    AssemblyReferenceIdentity? Subject,
    InertString Detail);

/// <summary>One visible Analysis blocker within a participant search.</summary>
public sealed record CloneCandidateAnalysisBlocker(
    StructuralCloneRetrievalBlockerKind Kind,
    InertString Detail);

/// <summary>Portable coverage for one exact seed method body.</summary>
public sealed record CloneCandidateSeedCoverage
{
    public CloneCandidateSeedCoverage(
        CloneCandidateMethodIdentity seed,
        StructuralCloneRetrievalDisposition disposition,
        int rankedPairs,
        int suppressedPairs,
        ImmutableArray<CloneCandidateAnalysisBlocker> blockers,
        ImmutableArray<CloneCandidateFailure> failures)
    {
        Seed = seed ?? throw new ArgumentNullException(nameof(seed));
        ArgumentOutOfRangeException.ThrowIfNegative(rankedPairs);
        ArgumentOutOfRangeException.ThrowIfNegative(suppressedPairs);
        CloneCandidatePresentationValidation.EnsureArray(
            blockers,
            nameof(blockers));
        CloneCandidatePresentationValidation.EnsureArray(
            failures,
            nameof(failures));
        Disposition = disposition;
        RankedPairs = rankedPairs;
        SuppressedPairs = suppressedPairs;
        Blockers = blockers;
        Failures = failures;
    }

    public CloneCandidateMethodIdentity Seed { get; }
    public StructuralCloneRetrievalDisposition Disposition { get; }
    public int RankedPairs { get; }
    public int SuppressedPairs { get; }
    public ImmutableArray<CloneCandidateAnalysisBlocker> Blockers { get; }
    public ImmutableArray<CloneCandidateFailure> Failures { get; }
    public bool IsComplete =>
        Disposition == StructuralCloneRetrievalDisposition.Completed
        && Blockers.IsEmpty
        && Failures.IsEmpty;

    public bool Equals(CloneCandidateSeedCoverage? other)
        => other is not null
            && Seed == other.Seed
            && Disposition == other.Disposition
            && RankedPairs == other.RankedPairs
            && SuppressedPairs == other.SuppressedPairs
            && PresentationValueEquality.SequenceEqual(
                Blockers,
                other.Blockers)
            && PresentationValueEquality.SequenceEqual(
                Failures,
                other.Failures);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Seed);
        hash.Add(Disposition);
        hash.Add(RankedPairs);
        hash.Add(SuppressedPairs);
        hash.Add(PresentationValueEquality.SequenceHashCode(Blockers));
        hash.Add(PresentationValueEquality.SequenceHashCode(Failures));
        return hash.ToHashCode();
    }
}

/// <summary>Portable coverage for one exact participant registration.</summary>
public sealed record CloneCandidateLibraryCoverage
{
    public CloneCandidateLibraryCoverage(
        CloneCandidateParticipantIdentity participant,
        StructuralCloneParticipantMembership membership,
        bool admitted,
        int candidateMethods,
        int discoveredMethods,
        long retrievalPairs,
        long nameComparisonWork,
        ImmutableArray<CloneCandidateFailure> failures,
        ImmutableArray<CloneCandidateAnalysisBlocker> analysisBlockers)
    {
        Participant =
            participant
            ?? throw new ArgumentNullException(nameof(participant));
        ArgumentOutOfRangeException.ThrowIfNegative(candidateMethods);
        ArgumentOutOfRangeException.ThrowIfNegative(discoveredMethods);
        ArgumentOutOfRangeException.ThrowIfNegative(retrievalPairs);
        ArgumentOutOfRangeException.ThrowIfNegative(nameComparisonWork);
        CloneCandidatePresentationValidation.EnsureArray(
            failures,
            nameof(failures));
        CloneCandidatePresentationValidation.EnsureArray(
            analysisBlockers,
            nameof(analysisBlockers));
        Membership = membership;
        Admitted = admitted;
        CandidateMethods = candidateMethods;
        DiscoveredMethods = discoveredMethods;
        RetrievalPairs = retrievalPairs;
        NameComparisonWork = nameComparisonWork;
        Failures = failures;
        AnalysisBlockers = analysisBlockers;
    }

    public CloneCandidateParticipantIdentity Participant { get; }
    public StructuralCloneParticipantMembership Membership { get; }
    public bool Admitted { get; }
    public int CandidateMethods { get; }
    public int DiscoveredMethods { get; }
    public long RetrievalPairs { get; }
    public long NameComparisonWork { get; }
    public ImmutableArray<CloneCandidateFailure> Failures { get; }
    public ImmutableArray<CloneCandidateAnalysisBlocker> AnalysisBlockers
    {
        get;
    }

    public bool IsComplete =>
        Failures.IsEmpty && AnalysisBlockers.IsEmpty;

    public bool Equals(CloneCandidateLibraryCoverage? other)
        => other is not null
            && Participant == other.Participant
            && Membership == other.Membership
            && Admitted == other.Admitted
            && CandidateMethods == other.CandidateMethods
            && DiscoveredMethods == other.DiscoveredMethods
            && RetrievalPairs == other.RetrievalPairs
            && NameComparisonWork == other.NameComparisonWork
            && PresentationValueEquality.SequenceEqual(
                Failures,
                other.Failures)
            && PresentationValueEquality.SequenceEqual(
                AnalysisBlockers,
                other.AnalysisBlockers);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Participant);
        hash.Add(Membership);
        hash.Add(Admitted);
        hash.Add(CandidateMethods);
        hash.Add(DiscoveredMethods);
        hash.Add(RetrievalPairs);
        hash.Add(NameComparisonWork);
        hash.Add(
            PresentationValueEquality.SequenceHashCode(Failures));
        hash.Add(
            PresentationValueEquality.SequenceHashCode(AnalysisBlockers));
        return hash.ToHashCode();
    }
}

/// <summary>Portable bounded-work receipt for one Clone Candidates search.</summary>
public sealed record CloneCandidateReceipt(
    int SeedMethods,
    int CandidateMethods,
    int DiscoveredMethods,
    int AdmittedLibraries,
    int ExcludedLibraries,
    long NameComparisonWork,
    long RetrievalPairs,
    int RetrievalCalls,
    int RankedPairs,
    int SuppressedPairs,
    int ReturnedPairs,
    bool ResultLimitReached);

/// <summary>
/// Host-neutral Clone Candidates search evidence.
/// </summary>
public sealed record CloneCandidateDocument
{
    public const int CurrentSchemaVersion = 1;
    public const string SectionName = "Clone Candidates";

    public CloneCandidateDocument(
        int schemaVersion,
        CloneCandidateSeed seed,
        StructuralCloneCandidateBreadth breadth,
        StructuralCloneCandidateDiscovery discovery,
        double nameSimilarityThreshold,
        WorkspaceStructuralCloneSearchLimits limits,
        bool scopeChangedDuringSearch,
        bool coverageIsComplete,
        ImmutableArray<CloneCandidateRow> rows,
        ImmutableArray<CloneCandidateSeedCoverage> seeds,
        ImmutableArray<CloneCandidateLibraryCoverage> libraries,
        CloneCandidateReceipt receipt)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }
        if (nameSimilarityThreshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nameSimilarityThreshold));
        }
        _ = breadth switch
        {
            StructuralCloneCandidateBreadth.Self => breadth,
            StructuralCloneCandidateBreadth
                .SelfAndRegisteredEcosystems => breadth,
            StructuralCloneCandidateBreadth.Everything => breadth,
            _ => throw new ArgumentOutOfRangeException(nameof(breadth)),
        };
        _ = discovery switch
        {
            StructuralCloneCandidateDiscovery.SimilarNames => discovery,
            StructuralCloneCandidateDiscovery.All => discovery,
            _ => throw new ArgumentOutOfRangeException(nameof(discovery)),
        };
        CloneCandidatePresentationValidation.EnsureArray(
            rows,
            nameof(rows));
        CloneCandidatePresentationValidation.EnsureArray(
            seeds,
            nameof(seeds));
        CloneCandidatePresentationValidation.EnsureArray(
            libraries,
            nameof(libraries));
        for (int index = 0; index < rows.Length; index++)
        {
            if (rows[index].Rank != index + 1)
            {
                throw new ArgumentException(
                    "Candidate rows must retain consecutive global rank order.",
                    nameof(rows));
            }
        }
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ReturnedPairs != rows.Length
            || receipt.SeedMethods != seeds.Length
            || receipt.AdmittedLibraries + receipt.ExcludedLibraries
                != libraries.Length
            || receipt.RankedPairs < receipt.ReturnedPairs
            || receipt.ResultLimitReached
                != (receipt.RankedPairs > receipt.ReturnedPairs))
        {
            throw new ArgumentException(
                "The receipt does not describe the projected populations.",
                nameof(receipt));
        }
        bool derivedCoverage =
            seeds.All(seed => seed.IsComplete)
            && libraries.All(library => library.IsComplete);
        if (coverageIsComplete != derivedCoverage)
        {
            throw new ArgumentException(
                "Overall coverage must match seed and library coverage.",
                nameof(coverageIsComplete));
        }

        SchemaVersion = schemaVersion;
        Seed = seed ?? throw new ArgumentNullException(nameof(seed));
        Breadth = breadth;
        Discovery = discovery;
        NameSimilarityThreshold = nameSimilarityThreshold;
        Limits = limits ?? throw new ArgumentNullException(nameof(limits));
        ScopeChangedDuringSearch = scopeChangedDuringSearch;
        CoverageIsComplete = coverageIsComplete;
        Rows = rows;
        Seeds = seeds;
        Libraries = libraries;
        Receipt = receipt;
    }

    public int SchemaVersion { get; }
    public CloneCandidateSeed Seed { get; }
    public StructuralCloneCandidateBreadth Breadth { get; }
    public StructuralCloneCandidateDiscovery Discovery { get; }
    public double NameSimilarityThreshold { get; }
    public WorkspaceStructuralCloneSearchLimits Limits { get; }
    public bool ScopeChangedDuringSearch { get; }
    public bool CoverageIsComplete { get; }
    public ImmutableArray<CloneCandidateRow> Rows { get; }
    public ImmutableArray<CloneCandidateSeedCoverage> Seeds { get; }
    public ImmutableArray<CloneCandidateLibraryCoverage> Libraries { get; }
    public CloneCandidateReceipt Receipt { get; }

    public bool ResultLimitReached => Receipt.ResultLimitReached;
    public int ResultLimitOmittedPairs =>
        Receipt.RankedPairs - Receipt.ReturnedPairs;

    public bool Equals(CloneCandidateDocument? other)
        => other is not null
            && SchemaVersion == other.SchemaVersion
            && Seed == other.Seed
            && Breadth == other.Breadth
            && Discovery == other.Discovery
            && NameSimilarityThreshold.Equals(
                other.NameSimilarityThreshold)
            && Limits == other.Limits
            && ScopeChangedDuringSearch == other.ScopeChangedDuringSearch
            && CoverageIsComplete == other.CoverageIsComplete
            && PresentationValueEquality.SequenceEqual(Rows, other.Rows)
            && PresentationValueEquality.SequenceEqual(Seeds, other.Seeds)
            && PresentationValueEquality.SequenceEqual(
                Libraries,
                other.Libraries)
            && Receipt == other.Receipt;

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Seed);
        hash.Add(Breadth);
        hash.Add(Discovery);
        hash.Add(NameSimilarityThreshold);
        hash.Add(Limits);
        hash.Add(ScopeChangedDuringSearch);
        hash.Add(CoverageIsComplete);
        hash.Add(PresentationValueEquality.SequenceHashCode(Rows));
        hash.Add(PresentationValueEquality.SequenceHashCode(Seeds));
        hash.Add(
            PresentationValueEquality.SequenceHashCode(Libraries));
        hash.Add(Receipt);
        return hash.ToHashCode();
    }
}

/// <summary>Why complete Query evidence could not be represented portably.</summary>
public enum CloneCandidatePresentationRejectionKind
{
    ParticipantCoverageMissing,
    ParticipantModuleInconsistent,
}

/// <summary>Closed Presentation outcome for one Workspace clone-search result.</summary>
public abstract record CloneCandidatePresentationResult
{
    private protected CloneCandidatePresentationResult()
    {
    }

    public sealed record Available(CloneCandidateDocument Document)
        : CloneCandidatePresentationResult
    {
        public CloneCandidateDocument Document { get; } =
            Document ?? throw new ArgumentNullException(nameof(Document));
    }

    public sealed record Rejected(
        AssemblyReferenceIdentity SeedLibrary,
        CandidateOpenFailureKind Kind,
        InertString Detail,
        MetadataRootMalformedReason? MetadataRootReason)
        : CloneCandidatePresentationResult
    {
        public AssemblyReferenceIdentity SeedLibrary { get; } =
            SeedLibrary
            ?? throw new ArgumentNullException(nameof(SeedLibrary));
    }

    public sealed record Failed(
        AssemblyReferenceIdentity SeedLibrary,
        CloneCandidateFailure Failure)
        : CloneCandidatePresentationResult
    {
        public AssemblyReferenceIdentity SeedLibrary { get; } =
            SeedLibrary
            ?? throw new ArgumentNullException(nameof(SeedLibrary));

        public CloneCandidateFailure Failure { get; } =
            Failure ?? throw new ArgumentNullException(nameof(Failure));
    }

    public sealed record Unrepresentable(
        CloneCandidatePresentationRejectionKind Kind,
        AssemblyReferenceIdentity Subject,
        InertString Detail)
        : CloneCandidatePresentationResult
    {
        public AssemblyReferenceIdentity Subject { get; } =
            Subject ?? throw new ArgumentNullException(nameof(Subject));
    }
}

/// <summary>Projects Workspace structural-clone search evidence for hosts.</summary>
public static class CloneCandidatePresentation
{
    public static CloneCandidatePresentationResult Create(
        WorkspaceStructuralCloneSearchResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result switch
        {
            WorkspaceStructuralCloneSearchResult.Available available =>
                CreateAvailable(available),
            WorkspaceStructuralCloneSearchResult.Rejected rejected =>
                new CloneCandidatePresentationResult.Rejected(
                    rejected.SeedSubject.Identity,
                    rejected.Failure.Kind,
                    Inert(rejected.Failure.Detail),
                    rejected.Failure.MetadataRootReason),
            WorkspaceStructuralCloneSearchResult.Failed failed =>
                new CloneCandidatePresentationResult.Failed(
                    failed.SeedSubject.Identity,
                    Project(failed.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown Workspace structural-clone search result."),
        };
    }

    static CloneCandidatePresentationResult CreateAvailable(
        WorkspaceStructuralCloneSearchResult.Available result)
    {
        var moduleIds = new Dictionary<int, Guid>();
        foreach (StructuralCloneSearchEndpoint endpoint in Endpoints(result))
        {
            if (moduleIds.TryGetValue(
                    endpoint.Participant.Ordinal,
                    out Guid moduleVersionId)
                && moduleVersionId != endpoint.Method.ModuleVersionId)
            {
                return new CloneCandidatePresentationResult.Unrepresentable(
                    CloneCandidatePresentationRejectionKind
                        .ParticipantModuleInconsistent,
                    endpoint.Subject.Identity,
                    Inert(
                        "One participant ordinal carries endpoint addresses "
                        + "from more than one module version ID."));
            }

            moduleIds[endpoint.Participant.Ordinal] =
                endpoint.Method.ModuleVersionId;
        }

        var participants =
            new Dictionary<int, CloneCandidateParticipantIdentity>();
        foreach (StructuralCloneSearchLibraryCoverage library
            in result.Libraries)
        {
            Guid? moduleVersionId =
                moduleIds.TryGetValue(
                    library.Participant.Ordinal,
                    out Guid endpointModule)
                    ? endpointModule
                    : library.Subject.Registration.ModuleVersionId;
            participants.Add(
                library.Participant.Ordinal,
                new CloneCandidateParticipantIdentity(
                    library.Participant.Ordinal,
                    library.Subject.Identity,
                    library.Subject.Provenance,
                    moduleVersionId));
        }
        foreach (StructuralCloneSearchEndpoint endpoint in Endpoints(result))
        {
            if (!participants.ContainsKey(endpoint.Participant.Ordinal))
            {
                return new CloneCandidatePresentationResult.Unrepresentable(
                    CloneCandidatePresentationRejectionKind
                        .ParticipantCoverageMissing,
                    endpoint.Subject.Identity,
                    Inert(
                        "An endpoint participant has no matching library "
                        + "coverage entry."));
            }
        }

        ImmutableArray<CloneCandidateRow> rows =
        [
            .. result.Pairs.Select(pair =>
                new CloneCandidateRow(
                    pair.Rank,
                    Project(pair.Left, participants),
                    Project(pair.Right, participants),
                    Project(pair.Similarity),
                    pair.NameQualification is null
                        ? null
                        : new CloneCandidateNameQualification(
                            pair.NameQualification
                                .DeclaringTypeSimilarity,
                            pair.NameQualification.MemberSimilarity))),
        ];
        ImmutableArray<CloneCandidateSeedCoverage> seeds =
        [
            .. result.Seeds.Select(seed =>
                new CloneCandidateSeedCoverage(
                    Project(seed.Seed, participants),
                    seed.Disposition,
                    seed.RankedPairs,
                    seed.SuppressedPairs,
                    [
                        .. seed.Blockers.Select(blocker =>
                            new CloneCandidateAnalysisBlocker(
                                blocker.Kind,
                                Inert(blocker.Detail))),
                    ],
                    Project(seed.Failures))),
        ];
        ImmutableArray<CloneCandidateLibraryCoverage> libraries =
        [
            .. result.Libraries.Select(library =>
                new CloneCandidateLibraryCoverage(
                    participants[library.Participant.Ordinal],
                    library.Membership,
                    library.Admitted,
                    library.CandidateMethods,
                    library.DiscoveredMethods,
                    library.RetrievalPairs,
                    library.NameComparisonWork,
                    Project(library.Failures),
                    [
                        .. library.AnalysisBlockers.Select(blocker =>
                            new CloneCandidateAnalysisBlocker(
                                blocker.Kind,
                                Inert(blocker.Detail))),
                    ])),
        ];
        var receipt =
            new CloneCandidateReceipt(
                result.Receipt.SeedMethods,
                result.Receipt.CandidateMethods,
                result.Receipt.DiscoveredCandidateMethods,
                result.Receipt.AdmittedLibraries,
                result.Receipt.ExcludedLibraries,
                result.Libraries.Sum(
                    library => library.NameComparisonWork),
                result.Receipt.RetrievalPairs,
                result.Receipt.RetrievalCalls,
                result.Receipt.RankedPairs,
                result.Receipt.SuppressedPairs,
                result.Receipt.ReturnedPairs,
                result.Receipt.ResultLimitReached);
        return new CloneCandidatePresentationResult.Available(
            new CloneCandidateDocument(
                CloneCandidateDocument.CurrentSchemaVersion,
                Project(result.Seed),
                result.Breadth,
                result.Discovery,
                result.NameSimilarityThreshold,
                result.Limits,
                !ReferenceEquals(
                    result.StartingRevision.Identity,
                    result.EffectiveRevision.Identity),
                result.CoverageIsComplete,
                rows,
                seeds,
                libraries,
                receipt));
    }

    static CloneCandidateSeed Project(StructuralCloneSearchSeed seed)
        => seed switch
        {
            StructuralCloneSearchSeed.Library =>
                new(
                    CloneCandidateSeedKind.Library,
                    type: null,
                    member: null),
            StructuralCloneSearchSeed.Type type =>
                new(
                    CloneCandidateSeedKind.Type,
                    type.Definition,
                    member: null),
            StructuralCloneSearchSeed.Member member =>
                new(
                    CloneCandidateSeedKind.Member,
                    member.Definition,
                    member.MemberIdentity),
            _ => throw new InvalidOperationException(
                "Unknown structural-clone seed."),
        };

    static CloneCandidateSimilarity Project(
        StructuralCloneSimilarityEvidence similarity)
        => new(
            similarity.Score,
            similarity.OperationScore,
            similarity.PositionScore,
            similarity.BlockScore,
            similarity.EdgeScore,
            similarity.LocalScore,
            similarity.SeedInstructions,
            similarity.CandidateInstructions,
            similarity.SeedBlocks,
            similarity.CandidateBlocks,
            similarity.SeedEdges,
            similarity.CandidateEdges,
            similarity.SeedLocals,
            similarity.CandidateLocals);

    static ImmutableArray<CloneCandidateFailure> Project(
        ImmutableArray<StructuralCloneSearchFailure> failures)
        =>
        [
            .. failures.Select(Project),
        ];

    static CloneCandidateFailure Project(
        StructuralCloneSearchFailure failure)
        => new(
            failure.Kind,
            failure.Subject?.Identity,
            Inert(failure.Detail));

    static CloneCandidateMethodIdentity Project(
        StructuralCloneSearchEndpoint endpoint,
        Dictionary<int, CloneCandidateParticipantIdentity> participants)
        => new(
            participants[endpoint.Participant.Ordinal],
            endpoint.Method.ModuleVersionId,
            endpoint.Method.Token,
            Inert(
                endpoint.Subject.Identity.Name
                + "!"
                + endpoint.Method.ModuleVersionId.ToString(
                    "N",
                    CultureInfo.InvariantCulture)
                + ":0x"
                + endpoint.Method.Token.ToString(
                    "X8",
                    CultureInfo.InvariantCulture)));

    static IEnumerable<StructuralCloneSearchEndpoint> Endpoints(
        WorkspaceStructuralCloneSearchResult.Available result)
    {
        foreach (StructuralCloneSearchPair pair in result.Pairs)
        {
            yield return pair.Left;
            yield return pair.Right;
        }
        foreach (StructuralCloneSearchSeedCoverage seed in result.Seeds)
            yield return seed.Seed;
    }

    static InertString Inert(string value)
        => new(TextPolicy.Field, value);

}

static class CloneCandidatePresentationValidation
{
    internal static void EnsureArray<T>(
        ImmutableArray<T> values,
        string parameterName)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException(
                "The array must be initialized.",
                parameterName);
        }
        if (values.Any(value => value is null))
        {
            throw new ArgumentException(
                "The array must not contain null values.",
                parameterName);
        }
    }
}
