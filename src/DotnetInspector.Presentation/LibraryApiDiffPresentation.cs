using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using DotnetInspector.Queries;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;
using InertText;

namespace DotnetInspector.Presentation;

/// <summary>Why one Library API endpoint could not participate in a portable diff.</summary>
public abstract record LibraryApiDiffEndpointIssue
{
    private LibraryApiDiffEndpointIssue()
    {
    }

    private protected abstract void EnsureKnownIssue();

    public sealed record Truncated(ApiSurfaceProjectionTruncation Truncation)
        : LibraryApiDiffEndpointIssue
    {
        public ApiSurfaceProjectionTruncation Truncation { get; } =
            Truncation ?? throw new ArgumentNullException(nameof(Truncation));

        private protected override void EnsureKnownIssue()
        {
        }
    }

    public sealed record Rejected(
        CandidateOpenFailureKind Kind,
        InertString Detail,
        MetadataRootMalformedReason? MetadataRootReason)
        : LibraryApiDiffEndpointIssue
    {
        private protected override void EnsureKnownIssue()
        {
        }
    }

    public sealed record Failed(InertString Detail)
        : LibraryApiDiffEndpointIssue
    {
        private protected override void EnsureKnownIssue()
        {
        }
    }

    public sealed record InspectionFailures(int Count)
        : LibraryApiDiffEndpointIssue
    {
        public int Count { get; } = Count > 0
            ? Count
            : throw new ArgumentOutOfRangeException(nameof(Count));

        private protected override void EnsureKnownIssue()
        {
        }
    }

    public sealed record DegradedSignatures(int Count)
        : LibraryApiDiffEndpointIssue
    {
        public int Count { get; } = Count > 0
            ? Count
            : throw new ArgumentOutOfRangeException(nameof(Count));

        private protected override void EnsureKnownIssue()
        {
        }
    }

    public sealed record UnexpectedAssemblyPopulation(int Count)
        : LibraryApiDiffEndpointIssue
    {
        public int Count { get; } = Count >= 0
            ? Count
            : throw new ArgumentOutOfRangeException(nameof(Count));

        private protected override void EnsureKnownIssue()
        {
        }
    }
}

/// <summary>Portable evidence for one independently projected Library API endpoint.</summary>
public sealed record LibraryApiDiffEndpointSummary
{
    public LibraryApiDiffEndpointSummary(
        AssemblyReferenceIdentity Identity,
        ApiSurfaceScope Scope,
        bool IsComplete,
        ImmutableArray<LibraryApiDiffEndpointIssue> Issues)
    {
        this.Identity = Identity ?? throw new ArgumentNullException(nameof(Identity));
        this.Scope = Scope switch
        {
            ApiSurfaceScope.Public => Scope,
            ApiSurfaceScope.IncludeAll => Scope,
            ApiSurfaceScope.PublicWithNonPublicTypes => Scope,
            _ => throw new ArgumentOutOfRangeException(nameof(Scope)),
        };
        if (Issues.IsDefault)
            throw new ArgumentException("Issues must be initialized.", nameof(Issues));
        if (Issues.Any(issue => issue is null))
            throw new ArgumentException("Issues must not contain null values.", nameof(Issues));
        if (IsComplete != Issues.IsEmpty)
        {
            throw new ArgumentException(
                "A complete endpoint must have no issues, and an incomplete endpoint must have at least one.",
                nameof(Issues));
        }

        this.IsComplete = IsComplete;
        this.Issues = Issues;
    }

    public AssemblyReferenceIdentity Identity { get; }
    public ApiSurfaceScope Scope { get; }
    public bool IsComplete { get; }
    public ImmutableArray<LibraryApiDiffEndpointIssue> Issues { get; }

    public bool Equals(LibraryApiDiffEndpointSummary? other)
        => other is not null
            && Identity == other.Identity
            && Scope == other.Scope
            && IsComplete == other.IsComplete
            && PresentationValueEquality.SequenceEqual(Issues, other.Issues);

    public override int GetHashCode()
        => HashCode.Combine(
            Identity,
            Scope,
            IsComplete,
            PresentationValueEquality.SequenceHashCode(Issues));
}

/// <summary>Which endpoint prevented a Library API comparison from being produced.</summary>
public enum LibraryApiDiffUnavailableKind
{
    BeforeIncomplete,
    AfterIncomplete,
    BothIncomplete,
}

/// <summary>Why complete producer evidence could not be represented without invention.</summary>
public enum LibraryApiDiffRejectionKind
{
    LogicalLibraryMismatch,
    FindingComparisonFailed,
    CompatibilityInspectionFailed,
    MissingExactTypeIdentity,
    MissingMemberAnchor,
    DuplicateExactTypeIdentity,
    UnassociatedStructuredSubject,
    ContradictoryOccupiedSideTopology,
}

/// <summary>How a classified Type pair occupies the Before and After endpoints.</summary>
public enum LibraryApiTypePairKind
{
    Present,
    Changed,
    Added,
    Removed,
}

/// <summary>How a distinct member relation occupies the Before and After endpoints.</summary>
public enum LibraryApiMemberPairKind
{
    Changed,
    Added,
    Removed,
}

/// <summary>Which occupied side of a member relation belongs to one Type payload.</summary>
public enum LibraryApiMemberRelationRole
{
    Before,
    After,
    Both,
}

/// <summary>An exact portable Type endpoint identity plus its producer display text.</summary>
public sealed record LibraryApiTypeIdentity
{
    public LibraryApiTypeIdentity(
        MetadataTypeDefinitionName DefinitionName,
        string Display)
    {
        this.DefinitionName =
            DefinitionName ?? throw new ArgumentNullException(nameof(DefinitionName));
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Display = Display;
    }

    public MetadataTypeDefinitionName DefinitionName { get; }
    public string Display { get; }
    public string Identifier => DefinitionName.ToEscapedFullName();
}

/// <summary>An exact portable member endpoint identity.</summary>
public sealed record LibraryApiMemberIdentity
{
    public LibraryApiMemberIdentity(
        LibraryApiTypeIdentity DeclaringType,
        MemberAnchor Anchor,
        string Display)
    {
        this.DeclaringType =
            DeclaringType ?? throw new ArgumentNullException(nameof(DeclaringType));
        this.Anchor = Anchor ?? throw new ArgumentNullException(nameof(Anchor));
        ArgumentException.ThrowIfNullOrWhiteSpace(Display);
        this.Display = Display;
    }

    public LibraryApiTypeIdentity DeclaringType { get; }
    public MemberAnchor Anchor { get; }
    public string Display { get; }
}

/// <summary>One complete distinct-member relation retained from Finding correspondence.</summary>
public sealed record LibraryApiMemberRelation
{
    public LibraryApiMemberRelation(
        string Identifier,
        LibraryApiMemberPairKind PairKind,
        LibraryApiMemberIdentity? Before,
        LibraryApiMemberIdentity? After,
        FindingMatchProvenance? Match)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Identifier);
        ValidateTopology(PairKind, Before, After);

        this.Identifier = Identifier;
        this.PairKind = PairKind;
        this.Before = Before;
        this.After = After;
        this.Match = Match;
    }

    public string Identifier { get; }
    public LibraryApiMemberPairKind PairKind { get; }
    public LibraryApiMemberIdentity? Before { get; }
    public LibraryApiMemberIdentity? After { get; }
    public FindingMatchProvenance? Match { get; }

    static void ValidateTopology(
        LibraryApiMemberPairKind kind,
        LibraryApiMemberIdentity? before,
        LibraryApiMemberIdentity? after)
    {
        bool valid = kind switch
        {
            LibraryApiMemberPairKind.Changed => before is not null && after is not null,
            LibraryApiMemberPairKind.Added => before is null && after is not null,
            LibraryApiMemberPairKind.Removed => before is not null && after is null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (!valid)
            throw new ArgumentException("The member relation sides do not match its pair kind.");
    }
}

/// <summary>One Type-local placement of a complete distinct-member relation.</summary>
public sealed record LibraryApiMemberDiff
{
    public LibraryApiMemberDiff(
        LibraryApiMemberRelation Relation,
        LibraryApiMemberRelationRole Role)
    {
        this.Relation =
            Relation ?? throw new ArgumentNullException(nameof(Relation));
        this.Role = ValidateRole(Role, Relation);
    }

    public LibraryApiMemberRelation Relation { get; }
    public LibraryApiMemberRelationRole Role { get; }

    static LibraryApiMemberRelationRole ValidateRole(
        LibraryApiMemberRelationRole role,
        LibraryApiMemberRelation relation)
    {
        string? before = relation.Before?.DeclaringType.Identifier;
        string? after = relation.After?.DeclaringType.Identifier;
        bool sameType = before is not null
            && after is not null
            && StringComparer.Ordinal.Equals(before, after);
        bool valid = role switch
        {
            LibraryApiMemberRelationRole.Before =>
                before is not null && !sameType,
            LibraryApiMemberRelationRole.After =>
                after is not null && !sameType,
            LibraryApiMemberRelationRole.Both => sameType,
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };
        return valid
            ? role
            : throw new ArgumentException(
                "The relation role does not match its occupied Type sides.",
                nameof(role));
    }
}

/// <summary>Portable structured endpoint identities for one compatibility change.</summary>
public sealed record LibraryApiChangeSubject
{
    public LibraryApiChangeSubject(
        ApiChangeSubjectKind Kind,
        LibraryApiTypeIdentity? BeforeType,
        LibraryApiTypeIdentity? AfterType,
        LibraryApiMemberIdentity? BeforeMember,
        LibraryApiMemberIdentity? AfterMember)
    {
        ValidateTopology(
            Kind,
            BeforeType,
            AfterType,
            BeforeMember,
            AfterMember);
        this.Kind = Kind;
        this.BeforeType = BeforeType;
        this.AfterType = AfterType;
        this.BeforeMember = BeforeMember;
        this.AfterMember = AfterMember;
    }

    public ApiChangeSubjectKind Kind { get; }
    public LibraryApiTypeIdentity? BeforeType { get; }
    public LibraryApiTypeIdentity? AfterType { get; }
    public LibraryApiMemberIdentity? BeforeMember { get; }
    public LibraryApiMemberIdentity? AfterMember { get; }

    static void ValidateTopology(
        ApiChangeSubjectKind kind,
        LibraryApiTypeIdentity? beforeType,
        LibraryApiTypeIdentity? afterType,
        LibraryApiMemberIdentity? beforeMember,
        LibraryApiMemberIdentity? afterMember)
    {
        bool valid = kind switch
        {
            ApiChangeSubjectKind.Type =>
                (beforeType is not null || afterType is not null)
                && beforeMember is null
                && afterMember is null,
            ApiChangeSubjectKind.Member =>
                (beforeMember is not null || afterMember is not null)
                && MemberAgrees(beforeType, beforeMember)
                && MemberAgrees(afterType, afterMember),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (!valid)
            throw new ArgumentException("The change subject sides do not match its kind.");
    }

    static bool MemberAgrees(
        LibraryApiTypeIdentity? type,
        LibraryApiMemberIdentity? member)
        => type is null && member is null
            || type is not null
                && member is not null
                && StringComparer.Ordinal.Equals(
                    type.Identifier,
                    member.DeclaringType.Identifier);
}

/// <summary>One Metadata-owned compatibility change without mutable API model graphs.</summary>
public sealed record LibraryApiCompatibilityChange
{
    public LibraryApiCompatibilityChange(
        ChangeKind Kind,
        ChangeClassification Classification,
        ApiChangeCategory Category,
        InertString Message,
        InertString? OldValue,
        InertString? NewValue,
        LibraryApiChangeSubject Subject)
    {
        this.Kind = Kind;
        this.Classification = Classification;
        this.Category = Category;
        this.Message = Message;
        this.OldValue = OldValue;
        this.NewValue = NewValue;
        this.Subject =
            Subject ?? throw new ArgumentNullException(nameof(Subject));
    }

    public ChangeKind Kind { get; }
    public ChangeClassification Classification { get; }
    public ApiChangeCategory Category { get; }
    public InertString Message { get; }
    public InertString? OldValue { get; }
    public InertString? NewValue { get; }
    public LibraryApiChangeSubject Subject { get; }
}

/// <summary>One complete changed-Type payload in a portable Library API diff document.</summary>
public sealed record LibraryApiTypeDiff
{
    public LibraryApiTypeDiff(
        LibraryApiTypeIdentity? Before,
        LibraryApiTypeIdentity? After,
        LibraryApiTypePairKind PairKind,
        bool? TypeDefinitionChanged,
        ImmutableArray<LibraryApiCompatibilityChange> CompatibilityChanges,
        ImmutableArray<LibraryApiMemberDiff> Members)
    {
        ValidateTopology(PairKind, Before, After, TypeDefinitionChanged);
        ValidateArray(CompatibilityChanges, nameof(CompatibilityChanges));
        ValidateArray(Members, nameof(Members));

        this.Before = Before;
        this.After = After;
        this.PairKind = PairKind;
        this.TypeDefinitionChanged = TypeDefinitionChanged;
        this.CompatibilityChanges = CompatibilityChanges;
        this.Members = Members;
    }

    public LibraryApiTypeIdentity? Before { get; }
    public LibraryApiTypeIdentity? After { get; }
    public LibraryApiTypePairKind PairKind { get; }
    public bool? TypeDefinitionChanged { get; }
    public ImmutableArray<LibraryApiCompatibilityChange> CompatibilityChanges { get; }
    public ImmutableArray<LibraryApiMemberDiff> Members { get; }
    public int BreakingCount =>
        CountChanges(ChangeClassification.Breaking);
    public int AdditiveCount =>
        CountChanges(ChangeClassification.Additive);
    public int PotentiallyBreakingCount =>
        CountChanges(ChangeClassification.PotentiallyBreaking);
    public int ChangedMemberCount => Members.Length;

    public bool Equals(LibraryApiTypeDiff? other)
        => other is not null
            && Before == other.Before
            && After == other.After
            && PairKind == other.PairKind
            && TypeDefinitionChanged == other.TypeDefinitionChanged
            && PresentationValueEquality.SequenceEqual(
                CompatibilityChanges,
                other.CompatibilityChanges)
            && PresentationValueEquality.SequenceEqual(Members, other.Members);

    public override int GetHashCode()
        => HashCode.Combine(
            Before,
            After,
            PairKind,
            TypeDefinitionChanged,
            PresentationValueEquality.SequenceHashCode(CompatibilityChanges),
            PresentationValueEquality.SequenceHashCode(Members));

    int CountChanges(ChangeClassification classification)
        => CompatibilityChanges.Count(change => change.Classification == classification);

    static void ValidateTopology(
        LibraryApiTypePairKind kind,
        LibraryApiTypeIdentity? before,
        LibraryApiTypeIdentity? after,
        bool? typeDefinitionChanged)
    {
        bool valid = kind switch
        {
            LibraryApiTypePairKind.Present =>
                before is not null && after is not null && typeDefinitionChanged is false,
            LibraryApiTypePairKind.Changed =>
                before is not null && after is not null && typeDefinitionChanged is true,
            LibraryApiTypePairKind.Added =>
                before is null && after is not null && typeDefinitionChanged is null,
            LibraryApiTypePairKind.Removed =>
                before is not null && after is null && typeDefinitionChanged is null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (!valid)
            throw new ArgumentException("The Type payload sides do not match its pair kind.");
    }

    static void ValidateArray<T>(ImmutableArray<T> values, string parameterName)
    {
        if (values.IsDefault)
            throw new ArgumentException("The array must be initialized.", parameterName);
        if (values.Any(value => value is null))
            throw new ArgumentException("The array must not contain null values.", parameterName);
    }
}

/// <summary>Root-wide counts derived from producer-issued compatibility rows and relations.</summary>
public sealed record LibraryApiDiffSummary
{
    public LibraryApiDiffSummary(
        int ChangedTypeCount,
        int AddedTypeCount,
        int RemovedTypeCount,
        int ChangedMemberCount,
        int BreakingCount,
        int AdditiveCount,
        int PotentiallyBreakingCount)
    {
        this.ChangedTypeCount = NonNegative(ChangedTypeCount);
        this.AddedTypeCount = NonNegative(AddedTypeCount);
        this.RemovedTypeCount = NonNegative(RemovedTypeCount);
        this.ChangedMemberCount = NonNegative(ChangedMemberCount);
        this.BreakingCount = NonNegative(BreakingCount);
        this.AdditiveCount = NonNegative(AdditiveCount);
        this.PotentiallyBreakingCount = NonNegative(PotentiallyBreakingCount);
        if (AddedTypeCount + RemovedTypeCount > ChangedTypeCount)
        {
            throw new ArgumentException(
                "Added and removed Type counts cannot exceed the changed-Type count.");
        }
    }

    public int ChangedTypeCount { get; }
    public int AddedTypeCount { get; }
    public int RemovedTypeCount { get; }
    public int ChangedMemberCount { get; }
    public int BreakingCount { get; }
    public int AdditiveCount { get; }
    public int PotentiallyBreakingCount { get; }

    static int NonNegative(int value)
        => value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value));
}

/// <summary>Closed outcome of projecting one query result into portable presentation data.</summary>
public abstract record LibraryApiDiffPresentationResult
{
    private LibraryApiDiffPresentationResult()
    {
    }

    private protected abstract void EnsureKnownResult();

    public sealed record Available : LibraryApiDiffPresentationResult
    {
        public Available(
            LibraryApiDiffEndpointSummary Before,
            LibraryApiDiffEndpointSummary After,
            LibraryApiDiffSummary Summary,
            ComparisonDocument<LibraryApiTypeDiff> Document)
        {
            this.Before = Before ?? throw new ArgumentNullException(nameof(Before));
            this.After = After ?? throw new ArgumentNullException(nameof(After));
            this.Summary = Summary ?? throw new ArgumentNullException(nameof(Summary));
            this.Document = Document ?? throw new ArgumentNullException(nameof(Document));
            if (!Before.IsComplete || !After.IsComplete)
            {
                throw new ArgumentException(
                    "An available Library API diff requires two complete endpoints.");
            }
            if (Summary.ChangedTypeCount != Document.Subjects.Length)
            {
                throw new ArgumentException(
                    "The summary changed-Type count must match the document.");
            }
        }

        public LibraryApiDiffEndpointSummary Before { get; }
        public LibraryApiDiffEndpointSummary After { get; }
        public LibraryApiDiffSummary Summary { get; }
        public ComparisonDocument<LibraryApiTypeDiff> Document { get; }

        private protected override void EnsureKnownResult()
        {
        }
    }

    public sealed record Unavailable : LibraryApiDiffPresentationResult
    {
        public Unavailable(
            LibraryApiDiffUnavailableKind Kind,
            LibraryApiDiffEndpointSummary Before,
            LibraryApiDiffEndpointSummary After)
        {
            this.Kind = Kind;
            this.Before = Before ?? throw new ArgumentNullException(nameof(Before));
            this.After = After ?? throw new ArgumentNullException(nameof(After));
            bool valid = Kind switch
            {
                LibraryApiDiffUnavailableKind.BeforeIncomplete =>
                    !Before.IsComplete && After.IsComplete,
                LibraryApiDiffUnavailableKind.AfterIncomplete =>
                    Before.IsComplete && !After.IsComplete,
                LibraryApiDiffUnavailableKind.BothIncomplete =>
                    !Before.IsComplete && !After.IsComplete,
                _ => throw new ArgumentOutOfRangeException(nameof(Kind)),
            };
            if (!valid)
            {
                throw new ArgumentException(
                    "The unavailable kind must identify the incomplete endpoints.");
            }
        }

        public LibraryApiDiffUnavailableKind Kind { get; }
        public LibraryApiDiffEndpointSummary Before { get; }
        public LibraryApiDiffEndpointSummary After { get; }

        private protected override void EnsureKnownResult()
        {
        }
    }

    public sealed record Rejected : LibraryApiDiffPresentationResult
    {
        public Rejected(
            LibraryApiDiffRejectionKind Kind,
            LibraryApiDiffEndpointSummary Before,
            LibraryApiDiffEndpointSummary After)
        {
            this.Kind = Kind;
            this.Before = Before ?? throw new ArgumentNullException(nameof(Before));
            this.After = After ?? throw new ArgumentNullException(nameof(After));
            if (!Before.IsComplete || !After.IsComplete)
            {
                throw new ArgumentException(
                    "A representational rejection requires complete endpoint evidence.");
            }
        }

        public LibraryApiDiffRejectionKind Kind { get; }
        public LibraryApiDiffEndpointSummary Before { get; }
        public LibraryApiDiffEndpointSummary After { get; }

        private protected override void EnsureKnownResult()
        {
        }
    }
}

/// <summary>
/// Projects a complete two-endpoint Library API query into a portable comparison document.
/// </summary>
public static class LibraryApiDiffPresentationAdapter
{
    const string LibraryIdentifierPrefix = "library-api.v1";
    const string MemberRelationIdentifierPrefix = "library-api-member-relation.v1";

    public static LibraryApiDiffPresentationResult Create(
        AssemblyContextApiComparisonResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        LibraryApiDiffEndpointSummary before =
            ProjectEndpoint(result.Before, result.Scope);
        LibraryApiDiffEndpointSummary after =
            ProjectEndpoint(result.After, result.Scope);
        if (!before.IsComplete || !after.IsComplete)
        {
            LibraryApiDiffUnavailableKind kind =
                !before.IsComplete && !after.IsComplete
                    ? LibraryApiDiffUnavailableKind.BothIncomplete
                    : !before.IsComplete
                        ? LibraryApiDiffUnavailableKind.BeforeIncomplete
                        : LibraryApiDiffUnavailableKind.AfterIncomplete;
            return new LibraryApiDiffPresentationResult.Unavailable(
                kind,
                before,
                after);
        }

        if (result.Comparison is not { } comparison)
        {
            return Rejected(
                LibraryApiDiffRejectionKind.FindingComparisonFailed,
                before,
                after);
        }

        if (comparison.Types is not FindingComparison<ApiTypeHandle>.Complete typeComparison
            || comparison.Members is not FindingComparison<ApiMemberHandle>.Complete
                memberComparison)
        {
            return Rejected(
                LibraryApiDiffRejectionKind.FindingComparisonFailed,
                before,
                after);
        }
        if (comparison.ApiDiff.InspectionFailures.Count > 0)
        {
            return Rejected(
                LibraryApiDiffRejectionKind.CompatibilityInspectionFailed,
                before,
                after);
        }

        if (!TryCreateRootIdentity(
                before.Identity,
                after.Identity,
                out string rootIdentifier,
                out string rootDisplay))
        {
            return Rejected(
                LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
                before,
                after);
        }

        if (!TryIndexEndpointTypes(
                GetCompleteSurface(result.Before),
                out Dictionary<string, LibraryApiTypeIdentity> beforeTypes,
                out var endpointRejection)
            || !TryIndexEndpointTypes(
                GetCompleteSurface(result.After),
                out Dictionary<string, LibraryApiTypeIdentity> afterTypes,
                out endpointRejection))
        {
            return Rejected(endpointRejection, before, after);
        }

        var typesByIdentifier =
            new Dictionary<string, TypeProjectionBuilder>(StringComparer.Ordinal);
        var orderedTypes = new List<TypeProjectionBuilder>(typeComparison.Pairs.Length);
        foreach (PairFinding<ApiTypeHandle> pair in typeComparison.Pairs)
        {
            if (!TryProjectTypePair(pair, out TypeProjectionBuilder type, out var rejection))
                return Rejected(rejection, before, after);
            if (!TypePairAgreesWithEndpoints(type, beforeTypes, afterTypes))
            {
                return Rejected(
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
                    before,
                    after);
            }
            if (!typesByIdentifier.TryAdd(type.Identifier, type))
            {
                return Rejected(
                    LibraryApiDiffRejectionKind.DuplicateExactTypeIdentity,
                    before,
                    after);
            }
            orderedTypes.Add(type);
        }

        var relationOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var beforeMemberRelations = new Dictionary<
            MemberEndpointIdentity,
            List<LibraryApiMemberRelation>>();
        var afterMemberRelations = new Dictionary<
            MemberEndpointIdentity,
            List<LibraryApiMemberRelation>>();
        foreach (PairFinding<ApiMemberHandle> pair in memberComparison.Pairs)
        {
            if (pair is PairFinding<ApiMemberHandle>.Present)
                continue;
            if (!TryProjectMemberPair(
                    pair,
                    relationOccurrences,
                    out LibraryApiMemberRelation relation,
                    out var rejection))
            {
                return Rejected(rejection, before, after);
            }
            if (!TryPlaceMemberRelation(
                    relation,
                    beforeTypes,
                    afterTypes,
                    typesByIdentifier,
                    orderedTypes,
                    out rejection))
            {
                return Rejected(rejection, before, after);
            }
            IndexMemberRelation(
                relation,
                beforeMemberRelations,
                afterMemberRelations);
        }

        int breakingCount = 0;
        int additiveCount = 0;
        int potentiallyBreakingCount = 0;
        foreach (TypeDiff typeDiff in comparison.ApiDiff.TypeDiffs)
        {
            foreach (ApiChange change in typeDiff.Changes)
            {
                if (!TryProjectCompatibilityChange(
                        typeDiff,
                        change,
                        out LibraryApiCompatibilityChange projected,
                        out ImmutableArray<LibraryApiTypeIdentity> occupiedTypes,
                        out var rejection))
                {
                    return Rejected(rejection, before, after);
                }
                foreach (LibraryApiTypeIdentity occupiedType in occupiedTypes)
                {
                    if (!TryEnsureType(
                            occupiedType,
                            beforeTypes,
                            afterTypes,
                            typesByIdentifier,
                            orderedTypes,
                            out TypeProjectionBuilder type,
                            out rejection))
                    {
                        return Rejected(rejection, before, after);
                    }
                }
                if (!CompatibilityTopologyAgrees(
                        projected,
                        typesByIdentifier,
                        beforeMemberRelations,
                        afterMemberRelations))
                {
                    return Rejected(
                        LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology,
                        before,
                        after);
                }
                foreach (LibraryApiTypeIdentity occupiedType in occupiedTypes)
                {
                    typesByIdentifier[occupiedType.Identifier]
                        .CompatibilityChanges
                        .Add(projected);
                }

                switch (projected.Classification)
                {
                    case ChangeClassification.Breaking:
                        breakingCount++;
                        break;
                    case ChangeClassification.Additive:
                        additiveCount++;
                        break;
                    case ChangeClassification.PotentiallyBreaking:
                        potentiallyBreakingCount++;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "The compatibility change has an unknown classification.");
                }
            }
        }

        List<TypeProjectionBuilder> selectedTypes =
            ApplyExactIdentityTieBreak(orderedTypes);
        var subjects =
            ImmutableArray.CreateBuilder<ComparisonSubject<LibraryApiTypeDiff>>(
                selectedTypes.Count);
        int addedTypes = 0;
        int removedTypes = 0;
        foreach (TypeProjectionBuilder type in selectedTypes)
        {
            ComparisonSubjectChange subjectChange = type.PairKind switch
            {
                LibraryApiTypePairKind.Added => new ComparisonSubjectChange.Addition(),
                LibraryApiTypePairKind.Removed => new ComparisonSubjectChange.Deletion(),
                LibraryApiTypePairKind.Present or LibraryApiTypePairKind.Changed =>
                    new ComparisonSubjectChange.Diff(),
                _ => throw new InvalidOperationException("The Type pair kind is unknown."),
            };
            if (type.PairKind == LibraryApiTypePairKind.Added)
                addedTypes++;
            if (type.PairKind == LibraryApiTypePairKind.Removed)
                removedTypes++;

            subjects.Add(
                new ComparisonSubject<LibraryApiTypeDiff>(
                    type.Identifier,
                    type.Display,
                    subjectChange,
                    type.Build()));
        }

        var document = new ComparisonDocument<LibraryApiTypeDiff>(
            ComparisonDocument<LibraryApiTypeDiff>.CurrentSchemaVersion,
            SubjectCoordinateBasis.RootRelative,
            rootIdentifier,
            rootDisplay,
            new ComparisonSubjectChange.Diff(),
            new ComparisonRootComparison<LibraryApiTypeDiff>.NotApplicable(),
            subjects.ToImmutable(),
            []);
        var summary = new LibraryApiDiffSummary(
            document.Subjects.Length,
            addedTypes,
            removedTypes,
            relationOccurrences.Values.Sum(),
            breakingCount,
            additiveCount,
            potentiallyBreakingCount);
        return new LibraryApiDiffPresentationResult.Available(
            before,
            after,
            summary,
            document);
    }

    static LibraryApiDiffPresentationResult.Rejected Rejected(
        LibraryApiDiffRejectionKind kind,
        LibraryApiDiffEndpointSummary before,
        LibraryApiDiffEndpointSummary after)
        => new(kind, before, after);

    static LibraryApiDiffEndpointSummary ProjectEndpoint(
        AssemblyContextApiComparisonEndpoint endpoint,
        ApiSurfaceScope scope)
    {
        var issues = ImmutableArray.CreateBuilder<LibraryApiDiffEndpointIssue>();
        if (endpoint.Projection.Truncation is { } truncation)
            issues.Add(new LibraryApiDiffEndpointIssue.Truncated(truncation));

        ImmutableArray<AssemblyContextEntry<AssemblyApiSurface>> assemblies =
            endpoint.Projection.Assemblies.Assemblies;
        if (assemblies.Length != 1)
        {
            issues.Add(
                new LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation(
                    assemblies.Length));
        }
        else
        {
            switch (assemblies[0])
            {
                case AssemblyContextEntry<AssemblyApiSurface>.Available available:
                    if (available.Value.InspectionFailures.Length > 0)
                    {
                        issues.Add(
                            new LibraryApiDiffEndpointIssue.InspectionFailures(
                                available.Value.InspectionFailures.Length));
                    }
                    int degradedSignatures = available.Value.Surface.Types.Sum(
                        type => type.Members.Count(
                            member =>
                                member.SignatureDecodeStatus
                                    is SignatureDecodeStatus.Degraded));
                    if (degradedSignatures > 0)
                    {
                        issues.Add(
                            new LibraryApiDiffEndpointIssue.DegradedSignatures(
                                degradedSignatures));
                    }
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Rejected rejected:
                    issues.Add(
                        new LibraryApiDiffEndpointIssue.Rejected(
                            rejected.Failure.Kind,
                            new InertString(TextPolicy.Field, rejected.Failure.Detail),
                            rejected.Failure.MetadataRootReason));
                    break;
                case AssemblyContextEntry<AssemblyApiSurface>.Failed failed:
                    issues.Add(
                        new LibraryApiDiffEndpointIssue.Failed(
                            new InertString(TextPolicy.Field, failed.Error.Message)));
                    break;
            }
        }

        if (!endpoint.IsComplete && issues.Count == 0)
        {
            issues.Add(
                new LibraryApiDiffEndpointIssue.UnexpectedAssemblyPopulation(
                    assemblies.Length));
        }
        return new LibraryApiDiffEndpointSummary(
            endpoint.Subject.Identity,
            scope,
            endpoint.IsComplete,
            issues.ToImmutable());
    }

    static bool TryProjectTypePair(
        PairFinding<ApiTypeHandle> pair,
        out TypeProjectionBuilder type,
        out LibraryApiDiffRejectionKind rejection)
    {
        ApiTypeHandle? oldHandle = null;
        ApiTypeHandle? newHandle = null;
        LibraryApiTypePairKind pairKind;
        switch (pair)
        {
            case PairFinding<ApiTypeHandle>.Added added:
                newHandle = added.New.Payload;
                pairKind = LibraryApiTypePairKind.Added;
                break;
            case PairFinding<ApiTypeHandle>.Removed removed:
                oldHandle = removed.Old.Payload;
                pairKind = LibraryApiTypePairKind.Removed;
                break;
            case PairFinding<ApiTypeHandle>.Present present:
                oldHandle = present.Old.Payload;
                newHandle = present.New.Payload;
                pairKind = LibraryApiTypePairKind.Present;
                break;
            case PairFinding<ApiTypeHandle>.Changed changed:
                oldHandle = changed.Old.Payload;
                newHandle = changed.New.Payload;
                pairKind = LibraryApiTypePairKind.Changed;
                break;
            default:
                throw new InvalidOperationException("The Type comparison has an unknown pair case.");
        }

        if (!TryProjectTypeIdentity(oldHandle, out LibraryApiTypeIdentity? before)
            || !TryProjectTypeIdentity(newHandle, out LibraryApiTypeIdentity? after))
        {
            type = null!;
            rejection = LibraryApiDiffRejectionKind.MissingExactTypeIdentity;
            return false;
        }
        if (before is not null
            && after is not null
            && !StringComparer.Ordinal.Equals(before.Identifier, after.Identifier))
        {
            type = null!;
            rejection =
                LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
            return false;
        }

        type = new TypeProjectionBuilder(before, after, pairKind);
        rejection = default;
        return true;
    }

    static bool TryProjectMemberPair(
        PairFinding<ApiMemberHandle> pair,
        Dictionary<string, int> occurrences,
        out LibraryApiMemberRelation relation,
        out LibraryApiDiffRejectionKind rejection)
    {
        ApiMemberHandle? oldHandle = null;
        ApiMemberHandle? newHandle = null;
        FindingMatchProvenance? match = null;
        LibraryApiMemberPairKind pairKind;
        switch (pair)
        {
            case PairFinding<ApiMemberHandle>.Added added:
                newHandle = added.New.Payload;
                pairKind = LibraryApiMemberPairKind.Added;
                break;
            case PairFinding<ApiMemberHandle>.Removed removed:
                oldHandle = removed.Old.Payload;
                pairKind = LibraryApiMemberPairKind.Removed;
                break;
            case PairFinding<ApiMemberHandle>.Changed changed:
                oldHandle = changed.Old.Payload;
                newHandle = changed.New.Payload;
                match = changed.Match;
                pairKind = LibraryApiMemberPairKind.Changed;
                break;
            case PairFinding<ApiMemberHandle>.Present:
                throw new ArgumentException(
                    "Unchanged member pairs do not belong in the diff.",
                    nameof(pair));
            default:
                throw new InvalidOperationException(
                    "The member comparison has an unknown pair case.");
        }

        if (!TryProjectMemberIdentity(oldHandle, out LibraryApiMemberIdentity? before)
            || !TryProjectMemberIdentity(newHandle, out LibraryApiMemberIdentity? after))
        {
            relation = null!;
            rejection =
                HasMissingTypeIdentity(oldHandle)
                    || HasMissingTypeIdentity(newHandle)
                        ? LibraryApiDiffRejectionKind.MissingExactTypeIdentity
                        : LibraryApiDiffRejectionKind.MissingMemberAnchor;
            return false;
        }

        string baseIdentifier = EncodeMemberRelation(before, after);
        int occurrence = occurrences.TryGetValue(baseIdentifier, out int count)
            ? count
            : 0;
        occurrences[baseIdentifier] = occurrence + 1;
        string identifier = EncodeParts(
            MemberRelationIdentifierPrefix,
            baseIdentifier,
            occurrence.ToString(CultureInfo.InvariantCulture));
        relation = new LibraryApiMemberRelation(
            identifier,
            pairKind,
            before,
            after,
            match);
        rejection = default;
        return true;
    }

    static bool TryPlaceMemberRelation(
        LibraryApiMemberRelation relation,
        Dictionary<string, LibraryApiTypeIdentity> beforeTypes,
        Dictionary<string, LibraryApiTypeIdentity> afterTypes,
        Dictionary<string, TypeProjectionBuilder> types,
        List<TypeProjectionBuilder> orderedTypes,
        out LibraryApiDiffRejectionKind rejection)
    {
        LibraryApiTypeIdentity? beforeIdentity = relation.Before?.DeclaringType;
        LibraryApiTypeIdentity? afterIdentity = relation.After?.DeclaringType;
        if (beforeIdentity is not null
            && (!beforeTypes.ContainsKey(beforeIdentity.Identifier)
                || !TryEnsureType(
                    beforeIdentity,
                    beforeTypes,
                    afterTypes,
                    types,
                    orderedTypes,
                    out _,
                    out rejection)))
        {
            rejection =
                LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
            return false;
        }
        if (afterIdentity is not null
            && (!afterTypes.ContainsKey(afterIdentity.Identifier)
                || !TryEnsureType(
                    afterIdentity,
                    beforeTypes,
                    afterTypes,
                    types,
                    orderedTypes,
                    out _,
                    out rejection)))
        {
            rejection =
                LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
            return false;
        }

        string? beforeType = beforeIdentity?.Identifier;
        string? afterType = afterIdentity?.Identifier;
        if (beforeType is not null
            && afterType is not null
            && StringComparer.Ordinal.Equals(beforeType, afterType))
        {
            types[beforeType].Members.Add(
                new LibraryApiMemberDiff(
                    relation,
                    LibraryApiMemberRelationRole.Both));
            rejection = default;
            return true;
        }

        if (beforeType is not null)
        {
            types[beforeType].Members.Add(
                new LibraryApiMemberDiff(
                    relation,
                    LibraryApiMemberRelationRole.Before));
        }
        if (afterType is not null)
        {
            types[afterType].Members.Add(
                new LibraryApiMemberDiff(
                    relation,
                    LibraryApiMemberRelationRole.After));
        }
        rejection = default;
        return true;
    }

    static void IndexMemberRelation(
        LibraryApiMemberRelation relation,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>> before,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>> after)
    {
        if (relation.Before is { } beforeMember)
            Add(before, MemberEndpointIdentity.Create(beforeMember), relation);
        if (relation.After is { } afterMember)
            Add(after, MemberEndpointIdentity.Create(afterMember), relation);

        static void Add(
            Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>> index,
            MemberEndpointIdentity key,
            LibraryApiMemberRelation relation)
        {
            if (!index.TryGetValue(key, out List<LibraryApiMemberRelation>? relations))
            {
                relations = [];
                index.Add(key, relations);
            }
            relations.Add(relation);
        }
    }

    static bool CompatibilityTopologyAgrees(
        LibraryApiCompatibilityChange change,
        Dictionary<string, TypeProjectionBuilder> types,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>>
            beforeMemberRelations,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>>
            afterMemberRelations)
        => (change.Subject.Kind, IsTypeChangeKind(change.Kind)) switch
        {
            (ApiChangeSubjectKind.Type, true) =>
                TypeChangeTopologyAgrees(change, types),
            (ApiChangeSubjectKind.Member, false) =>
                MemberChangeTopologyAgrees(
                    change,
                    beforeMemberRelations,
                    afterMemberRelations),
            _ => false,
        };

    static bool TypeChangeTopologyAgrees(
        LibraryApiCompatibilityChange change,
        Dictionary<string, TypeProjectionBuilder> types)
    {
        LibraryApiTypeIdentity? before = change.Subject.BeforeType;
        LibraryApiTypeIdentity? after = change.Subject.AfterType;
        string? identifier = after?.Identifier ?? before?.Identifier;
        if (identifier is null || !types.TryGetValue(identifier, out var type))
            return false;

        return change.Kind switch
        {
            ChangeKind.TypeAdded =>
                before is null
                && after is not null
                && type.PairKind == LibraryApiTypePairKind.Added,
            ChangeKind.TypeRemoved =>
                before is not null
                && after is null
                && type.PairKind == LibraryApiTypePairKind.Removed,
            _ =>
                before is not null
                && after is not null
                && StringComparer.Ordinal.Equals(
                    before.Identifier,
                    after.Identifier)
                && type.PairKind == LibraryApiTypePairKind.Changed,
        };
    }

    static bool MemberChangeTopologyAgrees(
        LibraryApiCompatibilityChange change,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>>
            beforeMemberRelations,
        Dictionary<MemberEndpointIdentity, List<LibraryApiMemberRelation>>
            afterMemberRelations)
    {
        LibraryApiMemberIdentity? before = change.Subject.BeforeMember;
        LibraryApiMemberIdentity? after = change.Subject.AfterMember;
        return change.Kind switch
        {
            ChangeKind.MemberAdded =>
                before is null
                && after is not null
                && afterMemberRelations.TryGetValue(
                    MemberEndpointIdentity.Create(after),
                    out List<LibraryApiMemberRelation>? addedRelations)
                && addedRelations.Any(
                    relation =>
                        relation.PairKind == LibraryApiMemberPairKind.Added
                        || relation is
                        {
                            PairKind: LibraryApiMemberPairKind.Changed,
                            Match: not null,
                        }),
            ChangeKind.MemberRemoved =>
                before is not null
                && after is null
                && beforeMemberRelations.TryGetValue(
                    MemberEndpointIdentity.Create(before),
                    out List<LibraryApiMemberRelation>? removedRelations)
                && removedRelations.Any(
                    relation =>
                        relation.PairKind == LibraryApiMemberPairKind.Removed
                        || relation is
                        {
                            PairKind: LibraryApiMemberPairKind.Changed,
                            Match: not null,
                        }),
            _ =>
                before is not null
                && after is not null
                && beforeMemberRelations.TryGetValue(
                    MemberEndpointIdentity.Create(before),
                    out List<LibraryApiMemberRelation>? changedRelations)
                && changedRelations.Any(
                    relation =>
                        relation.PairKind == LibraryApiMemberPairKind.Changed
                        && relation.After is { } relationAfter
                        && MemberEndpointIdentity.Create(relationAfter)
                            == MemberEndpointIdentity.Create(after)),
        };
    }

    static bool IsTypeChangeKind(ChangeKind kind)
        => kind switch
        {
            ChangeKind.TypeAdded
                or ChangeKind.TypeRemoved
                or ChangeKind.TypeKindChanged
                or ChangeKind.SealedAdded
                or ChangeKind.SealedRemoved
                or ChangeKind.AbstractAdded
                or ChangeKind.AbstractRemoved
                or ChangeKind.BaseTypeChanged
                or ChangeKind.InterfaceAdded
                or ChangeKind.InterfaceRemoved
                or ChangeKind.TypeParameterCountChanged
                or ChangeKind.TypeParameterVarianceChanged
                or ChangeKind.TypeParameterConstraintTightened
                or ChangeKind.TypeParameterConstraintLoosened
                or ChangeKind.TypeAttributeAdded
                or ChangeKind.TypeAttributeRemoved => true,
            ChangeKind.MemberAdded
                or ChangeKind.MemberRemoved
                or ChangeKind.MemberSignatureChanged
                or ChangeKind.VirtualRemoved
                or ChangeKind.AbstractMemberAdded
                or ChangeKind.EnumValueChanged
                or ChangeKind.MemberAttributeAdded
                or ChangeKind.MemberAttributeRemoved => false,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    static List<TypeProjectionBuilder> ApplyExactIdentityTieBreak(
        List<TypeProjectionBuilder> orderedTypes)
    {
        var selected = orderedTypes.Where(type => type.IsSelected).ToList();
        foreach (IGrouping<string, TypeProjectionBuilder> displayGroup in
            selected.GroupBy(type => type.Display, StringComparer.Ordinal))
        {
            if (displayGroup.Count() < 2)
                continue;

            int[] positions =
            [
                .. selected
                    .Select((type, index) => (type, index))
                    .Where(item =>
                        StringComparer.Ordinal.Equals(
                            item.type.Display,
                            displayGroup.Key))
                    .Select(item => item.index),
            ];
            TypeProjectionBuilder[] sorted =
            [
                .. displayGroup.OrderBy(
                    type => type.Identifier,
                    StringComparer.Ordinal),
            ];
            for (int index = 0; index < positions.Length; index++)
                selected[positions[index]] = sorted[index];
        }
        return selected;
    }

    static bool TryProjectCompatibilityChange(
        TypeDiff typeDiff,
        ApiChange change,
        out LibraryApiCompatibilityChange projected,
        out ImmutableArray<LibraryApiTypeIdentity> occupiedTypeIdentities,
        out LibraryApiDiffRejectionKind rejection)
    {
        if (change.Subject is not { } subject)
        {
            projected = null!;
            occupiedTypeIdentities = [];
            rejection = LibraryApiDiffRejectionKind.UnassociatedStructuredSubject;
            return false;
        }

        if (!TryProjectChangeSubject(
                subject,
                out LibraryApiChangeSubject projectedSubject,
                out ImmutableArray<LibraryApiTypeIdentity> occupiedTypes,
                out rejection))
        {
            projected = null!;
            occupiedTypeIdentities = [];
            return false;
        }
        if (!occupiedTypes.Any(
                occupied =>
                    StringComparer.Ordinal.Equals(
                        occupied.Display,
                        typeDiff.TypeFullName)))
        {
            projected = null!;
            occupiedTypeIdentities = [];
            rejection = LibraryApiDiffRejectionKind.UnassociatedStructuredSubject;
            return false;
        }

        occupiedTypeIdentities =
        [
            .. occupiedTypes
                .DistinctBy(occupied => occupied.Identifier, StringComparer.Ordinal),
        ];
        projected = new LibraryApiCompatibilityChange(
            change.Kind,
            change.Classification,
            change.Category,
            change.GetMessageText(),
            change.GetOldValueText(),
            change.GetNewValueText(),
            projectedSubject);
        rejection = default;
        return true;
    }

    static bool TryProjectChangeSubject(
        ApiChangeSubject subject,
        out LibraryApiChangeSubject projected,
        out ImmutableArray<LibraryApiTypeIdentity> occupiedTypes,
        out LibraryApiDiffRejectionKind rejection)
    {
        var occupied = ImmutableArray.CreateBuilder<LibraryApiTypeIdentity>();
        if (subject.Kind == ApiChangeSubjectKind.Type)
        {
            if (subject.OldMember is not null || subject.NewMember is not null)
            {
                projected = null!;
                occupiedTypes = [];
                rejection =
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
                return false;
            }
            if (HasMissingTypeIdentity(subject.OldType)
                || HasMissingTypeIdentity(subject.NewType)
                || !TryProjectTypeIdentity(subject.OldType, out var before)
                || !TryProjectTypeIdentity(subject.NewType, out var after))
            {
                projected = null!;
                occupiedTypes = [];
                rejection = LibraryApiDiffRejectionKind.MissingExactTypeIdentity;
                return false;
            }
            if (before is null && after is null)
            {
                projected = null!;
                occupiedTypes = [];
                rejection =
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
                return false;
            }
            AddOccupied(before);
            AddOccupied(after);
            projected = new LibraryApiChangeSubject(
                subject.Kind,
                before,
                after,
                null,
                null);
        }
        else if (subject.Kind == ApiChangeSubjectKind.Member)
        {
            if ((subject.OldType is null) != (subject.OldMember is null)
                || (subject.NewType is null) != (subject.NewMember is null))
            {
                projected = null!;
                occupiedTypes = [];
                rejection =
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
                return false;
            }
            if (HasMissingTypeIdentity(subject.OldType)
                || HasMissingTypeIdentity(subject.NewType)
                || HasMissingTypeIdentity(subject.OldMember)
                || HasMissingTypeIdentity(subject.NewMember))
            {
                projected = null!;
                occupiedTypes = [];
                rejection = LibraryApiDiffRejectionKind.MissingExactTypeIdentity;
                return false;
            }
            if (HasMissingAnchor(subject.OldMember)
                || HasMissingAnchor(subject.NewMember)
                || !TryProjectMemberIdentity(subject.OldMember, out var beforeMember)
                || !TryProjectMemberIdentity(subject.NewMember, out var afterMember))
            {
                projected = null!;
                occupiedTypes = [];
                rejection = LibraryApiDiffRejectionKind.MissingMemberAnchor;
                return false;
            }
            if (beforeMember is null && afterMember is null)
            {
                projected = null!;
                occupiedTypes = [];
                rejection =
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
                return false;
            }
            if (!TypeHandleAgrees(subject.OldType, beforeMember)
                || !TypeHandleAgrees(subject.NewType, afterMember))
            {
                projected = null!;
                occupiedTypes = [];
                rejection =
                    LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
                return false;
            }
            AddOccupied(beforeMember?.DeclaringType);
            AddOccupied(afterMember?.DeclaringType);
            projected = new LibraryApiChangeSubject(
                subject.Kind,
                beforeMember?.DeclaringType,
                afterMember?.DeclaringType,
                beforeMember,
                afterMember);
        }
        else
        {
            throw new InvalidOperationException(
                "The compatibility change has an unknown subject kind.");
        }

        occupiedTypes = occupied.ToImmutable();
        rejection = default;
        return true;

        void AddOccupied(LibraryApiTypeIdentity? type)
        {
            if (type is not null)
                occupied.Add(type);
        }
    }

    static bool HasMissingTypeIdentity(ApiTypeHandle? handle)
        => handle is not null && handle.Type.DefinitionName is null;

    static bool HasMissingTypeIdentity(ApiMemberHandle? handle)
        => handle is not null && handle.Type.DefinitionName is null;

    static bool HasMissingAnchor(ApiMemberHandle? handle)
        => handle is not null && handle.Anchor is null;

    static bool TryEnsureType(
        LibraryApiTypeIdentity identity,
        Dictionary<string, LibraryApiTypeIdentity> beforeTypes,
        Dictionary<string, LibraryApiTypeIdentity> afterTypes,
        Dictionary<string, TypeProjectionBuilder> types,
        List<TypeProjectionBuilder> orderedTypes,
        out TypeProjectionBuilder type,
        out LibraryApiDiffRejectionKind rejection)
    {
        bool hasBefore = beforeTypes.TryGetValue(identity.Identifier, out var before);
        bool hasAfter = afterTypes.TryGetValue(identity.Identifier, out var after);
        if (!hasBefore && !hasAfter)
        {
        type = null!;
        rejection = LibraryApiDiffRejectionKind.UnassociatedStructuredSubject;
        return false;
        }
        if (types.TryGetValue(identity.Identifier, out type!))
        {
        if ((hasBefore != (type.Before is not null))
            || (hasAfter != (type.After is not null)))
        {
            rejection =
                LibraryApiDiffRejectionKind.ContradictoryOccupiedSideTopology;
            return false;
        }
        rejection = default;
        return true;
        }

        LibraryApiTypePairKind pairKind =
        hasBefore && hasAfter
            ? LibraryApiTypePairKind.Present
            : hasAfter
                ? LibraryApiTypePairKind.Added
                : LibraryApiTypePairKind.Removed;
        type = new TypeProjectionBuilder(before, after, pairKind);
        types.Add(type.Identifier, type);
        orderedTypes.Add(type);
        rejection = default;
        return true;
    }

    static bool TypePairAgreesWithEndpoints(
        TypeProjectionBuilder type,
        Dictionary<string, LibraryApiTypeIdentity> beforeTypes,
        Dictionary<string, LibraryApiTypeIdentity> afterTypes)
        => beforeTypes.ContainsKey(type.Identifier) == (type.Before is not null)
            && afterTypes.ContainsKey(type.Identifier) == (type.After is not null);

    static bool TryIndexEndpointTypes(
        ApiSurface surface,
        out Dictionary<string, LibraryApiTypeIdentity> types,
        out LibraryApiDiffRejectionKind rejection)
    {
        types = new Dictionary<string, LibraryApiTypeIdentity>(StringComparer.Ordinal);
        foreach (ApiType type in surface.Types)
        {
        if (!TryProjectTypeIdentity(
                new ApiTypeHandle(type),
                out LibraryApiTypeIdentity? identity)
            || identity is null)
        {
            rejection = LibraryApiDiffRejectionKind.MissingExactTypeIdentity;
            return false;
        }
        if (!types.TryAdd(identity.Identifier, identity))
        {
            rejection = LibraryApiDiffRejectionKind.DuplicateExactTypeIdentity;
            return false;
        }
        }
        rejection = default;
        return true;
    }

    static ApiSurface GetCompleteSurface(
        AssemblyContextApiComparisonEndpoint endpoint)
        => ((AssemblyContextEntry<AssemblyApiSurface>.Available)
            endpoint.Projection.Assemblies.Assemblies[0])
        .Value
        .Surface;

    static bool TypeHandleAgrees(
        ApiTypeHandle? handle,
        LibraryApiMemberIdentity? member)
    {
        if (handle is null || member is null)
            return handle is null && member is null;
        return handle.Type.DefinitionName is { } definitionName
            && StringComparer.Ordinal.Equals(
                definitionName.ToEscapedFullName(),
                member.DeclaringType.Identifier);
    }

    static bool TryProjectTypeIdentity(
        ApiTypeHandle? handle,
        out LibraryApiTypeIdentity? identity)
    {
        if (handle is null)
        {
            identity = null;
            return true;
        }
        if (handle.Type.DefinitionName is not { } definitionName
            || string.IsNullOrWhiteSpace(handle.TypeFullName))
        {
            identity = null;
            return false;
        }
        identity = new LibraryApiTypeIdentity(
            definitionName,
            handle.TypeFullName);
        return true;
    }

    static bool TryProjectMemberIdentity(
        ApiMemberHandle? handle,
        out LibraryApiMemberIdentity? identity)
    {
        if (handle is null)
        {
            identity = null;
            return true;
        }
        if (handle.Anchor is not { } anchor
            || !TryProjectTypeIdentity(
                new ApiTypeHandle(handle.Type),
                out LibraryApiTypeIdentity? declaringType)
            || declaringType is null
            || string.IsNullOrWhiteSpace(handle.MemberName))
        {
            identity = null;
            return false;
        }
        identity = new LibraryApiMemberIdentity(
            declaringType,
            anchor,
            handle.MemberName);
        return true;
    }

    static bool TryCreateRootIdentity(
        AssemblyReferenceIdentity before,
        AssemblyReferenceIdentity after,
        out string identifier,
        out string display)
    {
        string beforeCulture = NormalizeCulture(before.Culture);
        string afterCulture = NormalizeCulture(after.Culture);
        string beforeToken = before.PublicKeyToken ?? "";
        string afterToken = after.PublicKeyToken ?? "";
        if (!StringComparer.OrdinalIgnoreCase.Equals(before.Name, after.Name)
            || !StringComparer.OrdinalIgnoreCase.Equals(beforeCulture, afterCulture)
            || !StringComparer.OrdinalIgnoreCase.Equals(beforeToken, afterToken))
        {
            identifier = "";
            display = "";
            return false;
        }

        identifier = EncodeParts(
            LibraryIdentifierPrefix,
            before.Name.ToUpperInvariant(),
            beforeCulture.ToUpperInvariant(),
            beforeToken.ToUpperInvariant());
        display = before.Name;
        return true;
    }

    static string NormalizeCulture(string? value)
        => string.IsNullOrEmpty(value)
            || value.Equals("neutral", StringComparison.OrdinalIgnoreCase)
                ? ""
                : value;

    static string EncodeMemberRelation(
        LibraryApiMemberIdentity? before,
        LibraryApiMemberIdentity? after)
        => EncodeParts(
            MemberRelationIdentifierPrefix,
            before?.DeclaringType.Identifier,
            EncodeAnchor(before?.Anchor),
            after?.DeclaringType.Identifier,
            EncodeAnchor(after?.Anchor));

    static string? EncodeAnchor(MemberAnchor? anchor)
        => anchor is null
            ? null
            : EncodeParts(
                "member-anchor.v1",
                anchor.TypeFullName,
                anchor.MemberName,
                anchor.CanonicalSignature,
                anchor.Fingerprint,
                anchor.StableSelector);

    static string EncodeParts(string prefix, params string?[] parts)
    {
        var builder = new StringBuilder(prefix);
        foreach (string? part in parts)
        {
            builder.Append('|');
            if (part is null)
            {
                builder.Append("-1:");
                continue;
            }
            builder.Append(part.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(part);
        }
        return builder.ToString();
    }

    readonly record struct MemberEndpointIdentity(
        string TypeIdentifier,
        MemberAnchor Anchor)
    {
        internal static MemberEndpointIdentity Create(
            LibraryApiMemberIdentity member)
            => new(member.DeclaringType.Identifier, member.Anchor);
    }

    sealed class TypeProjectionBuilder
    {
        internal TypeProjectionBuilder(
            LibraryApiTypeIdentity? before,
            LibraryApiTypeIdentity? after,
            LibraryApiTypePairKind pairKind)
        {
            Before = before;
            After = after;
            PairKind = pairKind;
        }

        internal LibraryApiTypeIdentity? Before { get; }
        internal LibraryApiTypeIdentity? After { get; }
        internal LibraryApiTypePairKind PairKind { get; }
        internal string Identifier => After?.Identifier ?? Before!.Identifier;
        internal string Display => After?.Display ?? Before!.Display;
        internal List<LibraryApiCompatibilityChange> CompatibilityChanges { get; } = [];
        internal List<LibraryApiMemberDiff> Members { get; } = [];
        internal bool IsSelected =>
            PairKind is not LibraryApiTypePairKind.Present
            || CompatibilityChanges.Count > 0
            || Members.Count > 0;

        internal LibraryApiTypeDiff Build()
            => new(
                Before,
                After,
                PairKind,
                PairKind switch
                {
                    LibraryApiTypePairKind.Present => false,
                    LibraryApiTypePairKind.Changed => true,
                    _ => null,
                },
                [.. CompatibilityChanges],
                [.. Members]);
    }
}

static class PresentationValueEquality
{
    internal static bool SequenceEqual<T>(
        ImmutableArray<T> left,
        ImmutableArray<T> right)
        => left.Length == right.Length
            && left.AsSpan().SequenceEqual(right.AsSpan());

    internal static int SequenceHashCode<T>(ImmutableArray<T> values)
    {
        var hash = new HashCode();
        foreach (T value in values)
            hash.Add(value);
        return hash.ToHashCode();
    }
}
