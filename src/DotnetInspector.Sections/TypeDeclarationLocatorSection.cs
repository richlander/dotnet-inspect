using System.Collections.Immutable;
using System.Text.Json.Serialization;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// L2 row-selection intent for reverse type-declaration answers.
/// </summary>
public sealed class TypeDeclarationLocatorSectionPlan
{
    public TypeDeclarationLocatorSectionPlan(
        RowSelectionIntent<string> rowSelection)
        : this(rowSelection, visibility: null)
    {
    }

    public TypeDeclarationLocatorSectionPlan(
        RowSelectionIntent<string> rowSelection,
        TypeDeclarationVisibilityPlan? visibility)
    {
        ArgumentNullException.ThrowIfNull(rowSelection);
        if (rowSelection.Operations.Any(
                static operation =>
                    operation.Kind is RowSelectionStageKind.Top))
        {
            throw new ArgumentException(
                "Type declaration locator rows have stable sequence order but no ranking order for Top.",
                nameof(rowSelection));
        }

        RowSelection = rowSelection;
        Visibility = visibility;
    }

    public static TypeDeclarationLocatorSectionPlan All { get; } =
        new(RowSelectionIntent<string>.Empty);

    public RowSelectionIntent<string> RowSelection { get; }
    public TypeDeclarationVisibilityPlan? Visibility { get; }
}

/// <summary>
/// Request identity retained in the shared locator result.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionRequest.ExactRequest), "exact")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionRequest.PatternRequest), "pattern")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionRequest.NamespaceRequest), "namespace")]
public abstract record TypeDeclarationLocatorSectionRequest
{
    private TypeDeclarationLocatorSectionRequest()
    {
    }

    public sealed record ExactRequest(MetadataTypeDefinitionName Name)
        : TypeDeclarationLocatorSectionRequest;

    public sealed record PatternRequest(string Text)
        : TypeDeclarationLocatorSectionRequest;

    public sealed record NamespaceRequest(
        string Name,
        MetadataNamespaceMatch Match)
        : TypeDeclarationLocatorSectionRequest;
}

/// <summary>
/// Typed transport projection of an exact Library source coordinate.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionCoordinate.PackageCoordinate), "package")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionCoordinate.PlatformCoordinate), "platform")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionCoordinate.ProjectCoordinate), "project")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionCoordinate.LocalCoordinate), "local")]
public abstract record TypeDeclarationLocatorSectionCoordinate
{
    private TypeDeclarationLocatorSectionCoordinate(
        AssemblyReferenceIdentity libraryIdentity)
    {
        LibraryIdentity =
            libraryIdentity
            ?? throw new ArgumentNullException(nameof(libraryIdentity));
    }

    public AssemblyReferenceIdentity LibraryIdentity { get; }

    private protected static bool LibraryIdentityEquals(
        AssemblyReferenceIdentity left,
        AssemblyReferenceIdentity right) =>
        AssemblyReferenceIdentity.EquivalentComparer.Equals(
            left,
            right);

    private protected static int LibraryIdentityHashCode(
        AssemblyReferenceIdentity identity) =>
        AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
            identity);

    public static TypeDeclarationLocatorSectionCoordinate FromSource(
        ExactLibrarySourceCoordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        AssemblyReferenceIdentity library =
            coordinate.LibraryIdentity.Identity;
        return coordinate switch
        {
            ExactLibrarySourceCoordinate.Package package =>
                new PackageCoordinate(
                    package.PackageCoordinate,
                    library),
            ExactLibrarySourceCoordinate.Platform platform =>
                new PlatformCoordinate(
                    platform.Population.Family,
                    library),
            ExactLibrarySourceCoordinate.Project =>
                new ProjectCoordinate(library),
            ExactLibrarySourceCoordinate.Local =>
                new LocalCoordinate(library),
            _ => throw new InvalidOperationException(
                "Unknown exact Library source coordinate."),
        };
    }

    public sealed record PackageCoordinate
        : TypeDeclarationLocatorSectionCoordinate
    {
        public PackageCoordinate(
            PackageSourceCoordinate packageCoordinate,
            AssemblyReferenceIdentity libraryIdentity)
            : base(libraryIdentity)
        {
            Package =
                packageCoordinate
                ?? throw new ArgumentNullException(
                    nameof(packageCoordinate));
        }

        public PackageSourceCoordinate Package { get; }

        public bool Equals(PackageCoordinate? other) =>
            other is not null
            && Package == other.Package
            && LibraryIdentityEquals(
                LibraryIdentity,
                other.LibraryIdentity);

        public override int GetHashCode() =>
            HashCode.Combine(
                0,
                Package,
                LibraryIdentityHashCode(LibraryIdentity));
    }

    public sealed record PlatformCoordinate
        : TypeDeclarationLocatorSectionCoordinate
    {
        public PlatformCoordinate(
            PlatformFamily family,
            AssemblyReferenceIdentity libraryIdentity)
            : base(libraryIdentity)
        {
            if (!Enum.IsDefined(family))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(family));
            }

            Family = family;
        }

        public PlatformFamily Family { get; }

        public bool Equals(PlatformCoordinate? other) =>
            other is not null
            && Family == other.Family
            && LibraryIdentityEquals(
                LibraryIdentity,
                other.LibraryIdentity);

        public override int GetHashCode() =>
            HashCode.Combine(
                1,
                Family,
                LibraryIdentityHashCode(LibraryIdentity));
    }

    public sealed record ProjectCoordinate
        : TypeDeclarationLocatorSectionCoordinate
    {
        public ProjectCoordinate(
            AssemblyReferenceIdentity libraryIdentity)
            : base(libraryIdentity)
        {
        }

        public bool Equals(ProjectCoordinate? other) =>
            other is not null
            && LibraryIdentityEquals(
                LibraryIdentity,
                other.LibraryIdentity);

        public override int GetHashCode() =>
            HashCode.Combine(
                2,
                LibraryIdentityHashCode(LibraryIdentity));
    }

    public sealed record LocalCoordinate
        : TypeDeclarationLocatorSectionCoordinate
    {
        public LocalCoordinate(
            AssemblyReferenceIdentity libraryIdentity)
            : base(libraryIdentity)
        {
        }

        public bool Equals(LocalCoordinate? other) =>
            other is not null
            && LibraryIdentityEquals(
                LibraryIdentity,
                other.LibraryIdentity);

        public override int GetHashCode() =>
            HashCode.Combine(
                3,
                LibraryIdentityHashCode(LibraryIdentity));
    }
}

/// <summary>
/// Exact realized source and target context for one observation.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorRealization.PackageRealization), "package")]
[JsonDerivedType(typeof(TypeDeclarationLocatorRealization.PlatformRealization), "platform")]
[JsonDerivedType(typeof(TypeDeclarationLocatorRealization.EmbeddedRealization), "embedded")]
[JsonDerivedType(typeof(TypeDeclarationLocatorRealization.PlatformReferenceRealization), "platform-reference")]
public abstract record TypeDeclarationLocatorRealization
{
    private TypeDeclarationLocatorRealization()
    {
    }

    public sealed record PackageRealization(
        string PackageId,
        string Version,
        string Producer,
        string? Framework,
        string? RuntimeIdentifier)
        : TypeDeclarationLocatorRealization;

    public sealed record PlatformRealization(
        string Family,
        string Version,
        string Producer,
        string Framework,
        string? Assembly,
        DotnetInspector.PlatformHouse.PlatformPopulationMemberRole? Role)
        : TypeDeclarationLocatorRealization;

    public sealed record EmbeddedRealization(
        string ContentRef,
        string Digest,
        string DeclaredName)
        : TypeDeclarationLocatorRealization;

    public sealed record PlatformReferenceRealization(
        TypeDeclarationLocatorReferenceEvidence Source, string Path)
        : TypeDeclarationLocatorRealization;
}

/// <summary>
/// Typed image-selection provenance retained separately from source identity.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.PackageSelection), "package")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.PlatformSelection), "platform")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.ProjectSelection), "project")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.LocalSelection), "local")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.EmbeddedSelection), "embedded")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSelection.DesignatedSelection), "designated")]
public abstract record TypeDeclarationLocatorSelection
{
    private TypeDeclarationLocatorSelection()
    {
    }

    public sealed record PackageSelection(
        string PackageId,
        string PackageVersion,
        string? Tfm,
        string? Rid,
        string? AssetPath)
        : TypeDeclarationLocatorSelection;

    public sealed record PlatformSelection(
        string Framework,
        string? FrameworkVersion,
        string ResolverSource)
        : TypeDeclarationLocatorSelection;

    public sealed record ProjectSelection(
        string ProjectName,
        string? Tfm,
        string? Rid)
        : TypeDeclarationLocatorSelection;

    public sealed record LocalSelection(string ResolverSource)
        : TypeDeclarationLocatorSelection;

    public sealed record EmbeddedSelection(
        string ContentRef,
        string Digest,
        string DeclaredName)
        : TypeDeclarationLocatorSelection;

    public sealed record DesignatedSelection(string ResolverSource)
        : TypeDeclarationLocatorSelection;
}

/// <summary>
/// Detached occurrence, source realization, and image-selection context.
/// </summary>
public sealed record TypeDeclarationLocatorObservation
{
    internal TypeDeclarationLocatorObservation(
        int contextOrder,
        int memberOrder,
        AssemblyReferenceIdentity assemblyIdentity,
        TypeDeclarationLocatorRealization realization,
        TypeDeclarationLocatorSelection selection)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contextOrder);
        ArgumentOutOfRangeException.ThrowIfNegative(memberOrder);
        ContextOrder = contextOrder;
        MemberOrder = memberOrder;
        AssemblyIdentity =
            assemblyIdentity
            ?? throw new ArgumentNullException(nameof(assemblyIdentity));
        Realization =
            realization
            ?? throw new ArgumentNullException(nameof(realization));
        Selection =
            selection
            ?? throw new ArgumentNullException(nameof(selection));
    }

    public int ContextOrder { get; }
    public int MemberOrder { get; }
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public TypeDeclarationLocatorRealization Realization { get; }
    public TypeDeclarationLocatorSelection Selection { get; }
}

/// <summary>
/// One selected logical row: one coordinate plus one attached observation.
/// </summary>
public sealed record TypeDeclarationLocatorSectionCandidate(
    TypeDeclarationLocatorSectionCoordinate Coordinate,
    MetadataTypeDefinitionName Name,
    AssemblyTypeDeclarationKind DeclarationKind,
    Guid ModuleVersionId,
    TypeDeclarationLocatorObservation Observation)
{
    [JsonIgnore]
    public AssemblyTypeDefinitionKind? DefinitionKind { get; init; }
    [JsonIgnore]
    public bool? IsDefinitionPublic { get; init; }
    [JsonIgnore]
    public int DeclarationOrder { get; init; }
    public bool IsPublicSurface { get; init; }
    public TypeDeclarationDiscoveryAttributes? DiscoveryAttributes { get; init; }
}

/// <summary>
/// One unsupported Metadata declaration retained as coverage evidence.
/// </summary>
public sealed record TypeDeclarationLocatorUnsupportedDeclaration(
    MetadataTypeDefinitionName Name,
    AssemblyTypeDeclarationKind DeclarationKind);

public enum TypeDeclarationLocatorMemberCoverageKind
{
    Searched,
    InventoryRejected,
    AccessRejected,
    Unavailable,
    CoordinateUnavailable,
    NotEvaluated,
}

/// <summary>Coverage for one selected population member.</summary>
public sealed record TypeDeclarationLocatorMemberCoverage(
    TypeDeclarationLocatorObservation Observation,
    TypeDeclarationLocatorMemberCoverageKind Outcome,
    bool IsComplete,
    ImmutableArray<TypeDeclarationLocatorUnsupportedDeclaration>
        UnsupportedDeclarations,
    CandidateOpenFailure? CandidateFailure,
    WorkspaceDeclarationPopulationFailure? WorkspaceFailure);

/// <summary>Coverage for one requested population context.</summary>
public sealed record TypeDeclarationLocatorContextCoverage(
    int ContextOrder,
    bool IsRealized,
    ImmutableArray<TypeDeclarationLocatorContextFailure> Failures);

/// <summary>
/// Owner-issued identity for one answer row set. Ordinal is correspondence,
/// not equality.
/// </summary>
public sealed class TypeDeclarationLocatorAnswerIdentity
{
    internal TypeDeclarationLocatorAnswerIdentity(int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ordinal);
        Ordinal = ordinal;
    }

    public int Ordinal { get; }
}

/// <summary>
/// One answer after row selection, retaining upstream cardinality and coverage.
/// </summary>
public sealed record TypeDeclarationLocatorSectionAnswer
{
    internal TypeDeclarationLocatorSectionAnswer(
        TypeDeclarationLocatorAnswerIdentity identity,
        TypeDeclarationLocatorSectionRequest request,
        int availableCandidateCount,
        ImmutableArray<TypeDeclarationLocatorSectionCandidate> candidates,
        bool isRealizationComplete,
        bool isEvaluationComplete,
        TypeDeclarationVisibilityCoverage? visibility = null)
    {
        Identity =
            identity
            ?? throw new ArgumentNullException(nameof(identity));
        Request =
            request
            ?? throw new ArgumentNullException(nameof(request));
        ArgumentOutOfRangeException.ThrowIfNegative(
            availableCandidateCount);
        if (candidates.IsDefault)
        {
            throw new ArgumentException(
                "Candidates must be initialized.",
                nameof(candidates));
        }
        if (candidates.Length > availableCandidateCount)
        {
            throw new ArgumentException(
                "Selected candidates cannot exceed available candidates.",
                nameof(candidates));
        }

        AvailableCandidateCount = availableCandidateCount;
        Candidates = candidates;
        IsRealizationComplete = isRealizationComplete;
        IsEvaluationComplete = isEvaluationComplete;
        Visibility = visibility;
    }

    public TypeDeclarationLocatorAnswerIdentity Identity { get; }
    public TypeDeclarationLocatorSectionRequest Request { get; }
    public int AvailableCandidateCount { get; }
    public ImmutableArray<TypeDeclarationLocatorSectionCandidate> Candidates
    { get; }
    public bool IsRealizationComplete { get; }
    public bool IsEvaluationComplete { get; }
    public TypeDeclarationVisibilityCoverage? Visibility { get; }
    public bool IsComplete =>
        IsRealizationComplete && IsEvaluationComplete
        && (Visibility?.IsComplete ?? true);
    public bool IsRowSelectionComplete =>
        Candidates.Length == AvailableCandidateCount;
}

/// <summary>
/// Strict semantic row-window failure bound to its exact answer.
/// </summary>
public sealed record TypeDeclarationLocatorRowSelectionFailure(
    TypeDeclarationLocatorAnswerIdentity Answer,
    TypeDeclarationLocatorSectionRequest Request,
    int StageNumber,
    int RequiredPosition,
    int AvailableCount);

/// <summary>
/// Shared typed result for rejected or evaluated locator projection.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionResult.Rejected), "rejected")]
[JsonDerivedType(typeof(TypeDeclarationLocatorSectionResult.Evaluated), "evaluated")]
public abstract record TypeDeclarationLocatorSectionResult
{
    private TypeDeclarationLocatorSectionResult()
    {
    }

    public sealed record Rejected(
        TypeDeclarationLocatorRejectionKind RejectionKind,
        int? RequestIndex,
        WorkspaceDeclarationPopulationFailure? PopulationFailure)
        : TypeDeclarationLocatorSectionResult;

    public sealed record Evaluated(
        bool IncludeAll,
        int? MaxInventoryReads,
        ImmutableArray<TypeDeclarationLocatorContextCoverage> Contexts,
        ImmutableArray<TypeDeclarationLocatorMemberCoverage> Members,
        ImmutableArray<TypeDeclarationLocatorSectionAnswer> Answers,
        TypeDeclarationLocatorRowSelectionFailure? RowSelectionFailure)
        : TypeDeclarationLocatorSectionResult
    {
        public TypeDeclarationVisibilityPlan? Visibility { get; init; }
        public TypeDeclarationVisibilityInputFailure? VisibilityFailure { get; init; }
        public bool IsSuccess => RowSelectionFailure is null && VisibilityFailure is null;
    }
}

public static class TypeDeclarationLocatorSection
{
    public static TypeDeclarationLocatorSectionResult Project(
        TypeDeclarationLocatorResult result,
        TypeDeclarationLocatorSectionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(plan);
        return result switch
        {
            TypeDeclarationLocatorResult.Rejected rejected =>
                new TypeDeclarationLocatorSectionResult.Rejected(
                    rejected.Kind,
                    rejected.RequestIndex,
                    rejected.PopulationFailure),
            TypeDeclarationLocatorResult.Evaluated evaluated =>
                Project(evaluated, plan),
            _ => throw new InvalidOperationException(
                "Unknown type declaration locator result."),
        };
    }

    private static TypeDeclarationLocatorSectionResult.Evaluated Project(
        TypeDeclarationLocatorResult.Evaluated result,
        TypeDeclarationLocatorSectionPlan plan)
    {
        var references = new TypeDeclarationLocatorReferenceProjection();
        var observations =
            new Dictionary<
                WorkspaceDeclarationMember,
                TypeDeclarationLocatorObservation>();
        TypeDeclarationLocatorObservation Observation(
            WorkspaceDeclarationMember member)
        {
            if (!observations.TryGetValue(member, out var observation))
            {
                observation = ProjectObservation(member, references);
                observations.Add(member, observation);
            }

            return observation;
        }

        ImmutableArray<TypeDeclarationLocatorContextCoverage> contexts =
        [
            .. result.Population.Contexts.Select(
                context =>
                    new TypeDeclarationLocatorContextCoverage(
                        context.Order,
                        context.IsRealized,
                        [
                            .. context.Failures.Select(references.Failure),
                        ])),
        ];
        ImmutableArray<TypeDeclarationLocatorMemberCoverage> members =
        [
            .. result.Members.Select(
                member => ProjectCoverage(
                    member,
                    Observation(member.Member))),
        ];

        var answerIdentities =
            new TypeDeclarationLocatorAnswerIdentity[
                result.Answers.Length];
        var requests =
            new TypeDeclarationLocatorSectionRequest[
                result.Answers.Length];
        var allCandidates =
            new ImmutableArray<TypeDeclarationLocatorSectionCandidate>[
                result.Answers.Length];
        var visibilityCoverage =
            new TypeDeclarationVisibilityCoverage?[result.Answers.Length];
        TypeDeclarationVisibilityInputFailure? visibilityFailure =
            plan.Visibility is not null && !result.IncludeAll
                ? TypeDeclarationVisibilityInputFailure.AllDeclarationsRequired
                : null;
        var sequences =
            new RowsCohortSequence<
                TypeDeclarationLocatorAnswerIdentity,
                TypeDeclarationLocatorSectionCandidate>[
                result.Answers.Length];
        for (int index = 0; index < result.Answers.Length; index++)
        {
            TypeDeclarationLocatorAnswer answer =
                result.Answers[index];
            TypeDeclarationLocatorAnswerIdentity identity =
                new(index + 1);
            TypeDeclarationLocatorSectionRequest request =
                ProjectRequest(answer.Request);
            ImmutableArray<TypeDeclarationLocatorSectionCandidate>
                candidates =
                [
                    .. answer.Candidates.Select(
                        candidate =>
                            new TypeDeclarationLocatorSectionCandidate(
                                TypeDeclarationLocatorSectionCoordinate
                                    .FromSource(candidate.Coordinate),
                                candidate.Name,
                                candidate.Kind,
                                candidate.ModuleVersionId,
                                Observation(candidate.Observation))
                            {
                                DefinitionKind = candidate.DefinitionKind,
                                IsDefinitionPublic =
                                    candidate.IsDefinitionPublic,
                                DeclarationOrder =
                                    candidate.DeclarationOrder,
                                IsPublicSurface = candidate.IsPublicSurface,
                                DiscoveryAttributes = candidate.DiscoveryAttributes,
                            }),
                ];
            if (visibilityFailure is not null)
            {
                visibilityCoverage[index] = new(candidates.Length, 0, [], IsEvaluated: false);
                candidates = [];
            }
            else if (plan.Visibility is { } visibility)
            {
                TypeDeclarationVisibilitySelection selection = visibility.Select(candidates);
                visibilityCoverage[index] = selection.Coverage;
                candidates = selection.Candidates;
            }
            answerIdentities[index] = identity;
            requests[index] = request;
            allCandidates[index] = candidates;
            sequences[index] =
                RowsCohortSequence<
                    TypeDeclarationLocatorAnswerIdentity,
                    TypeDeclarationLocatorSectionCandidate>.Create(
                        identity,
                        candidates);
        }

        RowsCohortResult<
            TypeDeclarationLocatorAnswerIdentity,
            TypeDeclarationLocatorSectionCandidate>? selected =
            visibilityFailure is null
                ? RowsCohortExecutor.ApplyUnordered(sequences, plan.RowSelection)
                : null;
        IReadOnlyDictionary<
            TypeDeclarationLocatorAnswerIdentity,
            IReadOnlyList<TypeDeclarationLocatorSectionCandidate>>
            selectedByAnswer =
            selected is { IsSuccess: true }
                ? selected.RowSets.ToDictionary(
                    static rowSet => rowSet.Identity,
                    static rowSet => rowSet.Values)
                : new Dictionary<
                    TypeDeclarationLocatorAnswerIdentity,
                    IReadOnlyList<
                        TypeDeclarationLocatorSectionCandidate>>();

        var answers =
            ImmutableArray.CreateBuilder<
                TypeDeclarationLocatorSectionAnswer>(
                    result.Answers.Length);
        for (int index = 0; index < result.Answers.Length; index++)
        {
            TypeDeclarationLocatorAnswer answer =
                result.Answers[index];
            IReadOnlyList<TypeDeclarationLocatorSectionCandidate>
                candidates =
                selectedByAnswer.TryGetValue(
                    answerIdentities[index],
                    out var selectedCandidates)
                    ? selectedCandidates
                    : [];
            answers.Add(
                new TypeDeclarationLocatorSectionAnswer(
                    answerIdentities[index],
                    requests[index],
                    allCandidates[index].Length,
                    [.. candidates],
                    answer.IsRealizationComplete,
                    answer.IsEvaluationComplete,
                    visibilityCoverage[index]));
        }

        TypeDeclarationLocatorRowSelectionFailure? failure = null;
        if (selected?.Failure is { } semanticFailure)
        {
            int index =
                Array.IndexOf(
                    answerIdentities,
                    semanticFailure.Identity);
            if (index < 0)
            {
                throw new InvalidOperationException(
                    "Row selection returned an unknown locator answer.");
            }

            failure =
                new TypeDeclarationLocatorRowSelectionFailure(
                    semanticFailure.Identity,
                    requests[index],
                    semanticFailure.Failure.StageNumber,
                    semanticFailure.Failure.RequiredPosition,
                    semanticFailure.Failure.AvailableCount);
        }

        return new(
            result.IncludeAll,
            result.MaxInventoryReads,
            contexts,
            members,
            answers.MoveToImmutable(),
            failure)
        {
            Visibility = plan.Visibility,
            VisibilityFailure = visibilityFailure,
        };
    }

    private static TypeDeclarationLocatorSectionRequest ProjectRequest(
        TypeDeclarationLocatorRequest request) =>
        request switch
        {
            TypeDeclarationLocatorRequest.Exact exact =>
                new TypeDeclarationLocatorSectionRequest.ExactRequest(
                    exact.Name),
            TypeDeclarationLocatorRequest.Pattern pattern =>
                new TypeDeclarationLocatorSectionRequest.PatternRequest(
                    pattern.Text),
            TypeDeclarationLocatorRequest.Namespace @namespace =>
                new TypeDeclarationLocatorSectionRequest.NamespaceRequest(
                    @namespace.Name,
                    @namespace.Match),
            _ => throw new InvalidOperationException(
                "Unknown type declaration locator request."),
        };

    private static TypeDeclarationLocatorMemberCoverage ProjectCoverage(
        TypeDeclarationLocatorMemberOutcome outcome,
        TypeDeclarationLocatorObservation observation)
    {
        ImmutableArray<TypeDeclarationLocatorUnsupportedDeclaration>
            unsupported = [];
        CandidateOpenFailure? candidateFailure = null;
        WorkspaceDeclarationPopulationFailure? workspaceFailure = null;
        TypeDeclarationLocatorMemberCoverageKind kind =
            outcome switch
            {
                TypeDeclarationLocatorMemberOutcome.Searched searched =>
                    Searched(searched),
                TypeDeclarationLocatorMemberOutcome.InventoryRejected
                    rejected =>
                    Failed(
                        TypeDeclarationLocatorMemberCoverageKind
                            .InventoryRejected,
                        rejected.Failure),
                TypeDeclarationLocatorMemberOutcome.AccessRejected
                    rejected =>
                    Failed(
                        TypeDeclarationLocatorMemberCoverageKind
                            .AccessRejected,
                        rejected.Failure),
                TypeDeclarationLocatorMemberOutcome.Unavailable unavailable =>
                    Unavailable(unavailable.Failure),
                TypeDeclarationLocatorMemberOutcome.CoordinateUnavailable =>
                    TypeDeclarationLocatorMemberCoverageKind
                        .CoordinateUnavailable,
                TypeDeclarationLocatorMemberOutcome.NotEvaluated =>
                    TypeDeclarationLocatorMemberCoverageKind.NotEvaluated,
                _ => throw new InvalidOperationException(
                    "Unknown locator member outcome."),
            };
        return new(
            observation,
            kind,
            outcome.IsComplete,
            unsupported,
            candidateFailure,
            workspaceFailure);

        TypeDeclarationLocatorMemberCoverageKind Searched(
            TypeDeclarationLocatorMemberOutcome.Searched searched)
        {
            unsupported =
            [
                .. searched.UnsupportedDeclarations.Select(
                    static declaration =>
                        new TypeDeclarationLocatorUnsupportedDeclaration(
                            declaration.Name,
                            declaration.Kind)),
            ];
            return TypeDeclarationLocatorMemberCoverageKind.Searched;
        }

        TypeDeclarationLocatorMemberCoverageKind Failed(
            TypeDeclarationLocatorMemberCoverageKind failedKind,
            CandidateOpenFailure failure)
        {
            candidateFailure = failure;
            return failedKind;
        }

        TypeDeclarationLocatorMemberCoverageKind Unavailable(
            WorkspaceDeclarationPopulationFailure failure)
        {
            workspaceFailure = failure;
            return TypeDeclarationLocatorMemberCoverageKind.Unavailable;
        }
    }

    internal static TypeDeclarationLocatorObservation ProjectObservation(
        WorkspaceDeclarationMember member,
        TypeDeclarationLocatorReferenceProjection references) =>
        new(
            member.Occurrence.ContextOrder,
            member.Occurrence.MemberOrder,
            member.AssemblyIdentity,
            ProjectRealization(member.Origin, references),
            member.Selection switch
            {
                AssemblyResolutionProvenance.PackageAsset package =>
                    new TypeDeclarationLocatorSelection.PackageSelection(
                        package.PackageId,
                        package.PackageVersion,
                        package.Tfm,
                        package.Rid,
                        package.AssetPath),
                AssemblyResolutionProvenance.PlatformAsset platform =>
                    new TypeDeclarationLocatorSelection.PlatformSelection(
                        platform.Framework,
                        platform.FrameworkVersion,
                        platform.ResolverSource),
                AssemblyResolutionProvenance.ProjectAsset project =>
                    new TypeDeclarationLocatorSelection.ProjectSelection(
                        project.Project,
                        project.Tfm,
                        project.Rid),
                AssemblyResolutionProvenance.LocalAsset local =>
                    new TypeDeclarationLocatorSelection.LocalSelection(
                        local.ResolverSource),
                AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                    new TypeDeclarationLocatorSelection.EmbeddedSelection(
                        embedded.ContentRef,
                        embedded.Digest,
                        embedded.DeclaredName),
                AssemblyResolutionProvenance.DesignatedAsset designated =>
                    new TypeDeclarationLocatorSelection.DesignatedSelection(
                        designated.ResolverSource),
                _ => throw new InvalidOperationException(
                    "Unknown assembly selection provenance."),
            });

    private static TypeDeclarationLocatorRealization ProjectRealization(
        WorkspaceDeclarationOrigin origin,
        TypeDeclarationLocatorReferenceProjection references) =>
        origin switch
        {
            WorkspaceDeclarationOrigin.ContextLoad { Realized: RealizedMemberCoordinate.Package package } =>
                new TypeDeclarationLocatorRealization.PackageRealization(
                    package.PackageId, package.Version, package.Producer,
                    package.Framework, package.RuntimeIdentifier),
            WorkspaceDeclarationOrigin.ContextLoad { Realized: RealizedMemberCoordinate.Platform platform } =>
                new TypeDeclarationLocatorRealization.PlatformRealization(
                    platform.Family, platform.Version, platform.Producer,
                    platform.Framework, platform.Assembly, Role: null),
            WorkspaceDeclarationOrigin.ContextLoad { Realized: RealizedMemberCoordinate.Embedded embedded } =>
                new TypeDeclarationLocatorRealization.EmbeddedRealization(
                    embedded.ContentRef, embedded.Digest, embedded.DeclaredName),
            WorkspaceDeclarationOrigin.PackageScope packageScope =>
                PackageRealization(
                    packageScope.Occurrence.Occurrence.Package.Coordinate),
            WorkspaceDeclarationOrigin.PlatformReference reference =>
                new TypeDeclarationLocatorRealization.PlatformReferenceRealization(
                    references.Evidence(reference.Source), reference.Path),
            WorkspaceDeclarationOrigin.PlatformPopulation population =>
                new TypeDeclarationLocatorRealization.PlatformRealization(
                    population.Target.Family.ToString(),
                    population.Target.Version.Value,
                    population.Producer,
                    population.Target.TargetFramework.ToString(),
                    population.Assembly,
                    population.Role),
            _ => throw new InvalidOperationException("Unknown declaration origin."),
        };

    private static TypeDeclarationLocatorRealization.PackageRealization
        PackageRealization(RealizedMemberCoordinate.Package package) =>
        new(
            package.PackageId,
            package.Version,
            package.Producer,
            package.Framework,
            package.RuntimeIdentifier);
}
