using System.Collections.Immutable;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>An exact Metadata name or a Metadata-owned type-filter pattern.</summary>
public abstract record TypeDeclarationLocatorRequest
{
    private protected TypeDeclarationLocatorRequest() { }

    public sealed record Exact(MetadataTypeDefinitionName Name) : TypeDeclarationLocatorRequest;
    public sealed record Pattern(string Text) : TypeDeclarationLocatorRequest;
    public sealed record Namespace(
        string Name,
        MetadataNamespaceMatch Match = MetadataNamespaceMatch.Exact)
        : TypeDeclarationLocatorRequest;
}

/// <summary>One detached declaration choice, including its source observation.</summary>
public sealed class TypeDeclarationLocatorCandidate
{
    internal TypeDeclarationLocatorCandidate(
        ExactLibrarySourceCoordinate coordinate,
        AssemblyTypeDeclaration declaration,
        Guid moduleVersionId,
        int declarationOrder,
        WorkspaceDeclarationMember observation)
    {
        Coordinate = coordinate;
        Name = declaration.Name;
        Kind = declaration.Kind;
        ModuleVersionId = moduleVersionId;
        DefinitionKind = declaration.DefinitionKind;
        IsDefinitionPublic = declaration.IsDefinitionPublic;
        IsPublicSurface = declaration.IsPublicSurface;
        DiscoveryAttributes = declaration.DiscoveryAttributes;
        DeclarationOrder = declarationOrder;
        Observation = observation;
    }

    public ExactLibrarySourceCoordinate Coordinate { get; }
    public MetadataTypeDefinitionName Name { get; }
    public AssemblyTypeDeclarationKind Kind { get; }
    public Guid ModuleVersionId { get; }
    public AssemblyTypeDefinitionKind? DefinitionKind { get; }
    public bool? IsDefinitionPublic { get; }
    public bool IsPublicSurface { get; }
    public TypeDeclarationDiscoveryAttributes? DiscoveryAttributes { get; }
    public int DeclarationOrder { get; }
    public WorkspaceDeclarationMember Observation { get; }
}

/// <summary>Attributed evaluation evidence for one selected occurrence.</summary>
public abstract record TypeDeclarationLocatorMemberOutcome(WorkspaceDeclarationMember Member)
{
    public bool IsComplete => this is Searched { UnsupportedDeclarations.IsEmpty: true };

    public sealed record Searched(
        WorkspaceDeclarationMember Member,
        ImmutableArray<AssemblyTypeDeclaration> UnsupportedDeclarations)
        : TypeDeclarationLocatorMemberOutcome(Member);

    public sealed record InventoryRejected(
        WorkspaceDeclarationMember Member, CandidateOpenFailure Failure)
        : TypeDeclarationLocatorMemberOutcome(Member);

    public sealed record AccessRejected(
        WorkspaceDeclarationMember Member, CandidateOpenFailure Failure)
        : TypeDeclarationLocatorMemberOutcome(Member);

    public sealed record Unavailable(
        WorkspaceDeclarationMember Member, WorkspaceDeclarationPopulationFailure Failure)
        : TypeDeclarationLocatorMemberOutcome(Member);

    public sealed record CoordinateUnavailable(WorkspaceDeclarationMember Member)
        : TypeDeclarationLocatorMemberOutcome(Member);

    public sealed record NotEvaluated(
        WorkspaceDeclarationMember Member,
        WorkspaceDeclarationInventoryBound? Bound = null)
        : TypeDeclarationLocatorMemberOutcome(Member);
}

/// <summary>Always a candidate vector, with separate realization and evaluation coverage.</summary>
public sealed class TypeDeclarationLocatorAnswer
{
    internal TypeDeclarationLocatorAnswer(
        TypeDeclarationLocatorRequest request,
        ImmutableArray<TypeDeclarationLocatorCandidate> candidates,
        bool isRealizationComplete,
        bool isEvaluationComplete)
    {
        Request = request;
        Candidates = candidates;
        IsRealizationComplete = isRealizationComplete;
        IsEvaluationComplete = isEvaluationComplete;
    }

    public TypeDeclarationLocatorRequest Request { get; }
    public ImmutableArray<TypeDeclarationLocatorCandidate> Candidates { get; }
    public bool IsRealizationComplete { get; }
    public bool IsEvaluationComplete { get; }
    public bool IsComplete => IsRealizationComplete && IsEvaluationComplete;
}

public enum TypeDeclarationLocatorRejectionKind
{
    EmptyRequests,
    InvalidRequest,
    InvalidInventoryReadLimit,
    PopulationUnavailable,
}

/// <summary>A rejected admission or detached results for every admitted request.</summary>
public abstract class TypeDeclarationLocatorResult
{
    private protected TypeDeclarationLocatorResult() { }

    public sealed class Rejected : TypeDeclarationLocatorResult
    {
        internal Rejected(
            TypeDeclarationLocatorRejectionKind kind,
            int? requestIndex = null,
            WorkspaceDeclarationPopulationFailure? populationFailure = null)
        {
            Kind = kind;
            RequestIndex = requestIndex;
            PopulationFailure = populationFailure;
        }

        public TypeDeclarationLocatorRejectionKind Kind { get; }
        public int? RequestIndex { get; }
        public WorkspaceDeclarationPopulationFailure? PopulationFailure { get; }
    }

    public sealed class Evaluated : TypeDeclarationLocatorResult
    {
        internal Evaluated(
            WorkspaceDeclarationPopulationReceipt population,
            bool includeAll,
            int? maxInventoryReads,
            ImmutableArray<TypeDeclarationLocatorMemberOutcome> members,
            ImmutableArray<TypeDeclarationLocatorAnswer> answers)
        {
            Population = population;
            IncludeAll = includeAll;
            MaxInventoryReads = maxInventoryReads;
            Members = members;
            Answers = answers;
        }

        public WorkspaceDeclarationPopulationReceipt Population { get; }
        public bool IncludeAll { get; }
        public int? MaxInventoryReads { get; }
        public ImmutableArray<TypeDeclarationLocatorMemberOutcome> Members { get; }
        public ImmutableArray<TypeDeclarationLocatorAnswer> Answers { get; }
    }
}
