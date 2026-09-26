using System.Collections.Immutable;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Libraries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Workspace-issued identity and stable order of one observed assembly.</summary>
public sealed class WorkspaceDeclarationOccurrence
{
    internal WorkspaceDeclarationOccurrence(
        InspectionWorkspaceIdentity workspace, int contextOrder, int memberOrder)
    {
        Workspace = workspace;
        ContextOrder = contextOrder;
        MemberOrder = memberOrder;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public int ContextOrder { get; }
    public int MemberOrder { get; }
}

public enum WorkspaceDeclarationCoordinateStatus
{
    Available,
    CoordinateUnavailable,
}

/// <summary>Detached source and selection evidence for one exact occurrence.</summary>
public sealed class WorkspaceDeclarationMember
{
    internal WorkspaceDeclarationMember(
        WorkspaceDeclarationOccurrence occurrence,
        ExactLibrarySourceCoordinate? coordinate,
        AssemblyReferenceIdentity assemblyIdentity,
        WorkspaceDeclarationOrigin origin,
        AssemblyResolutionProvenance selection)
    {
        Occurrence = occurrence;
        Coordinate = coordinate;
        AssemblyIdentity = assemblyIdentity;
        Origin = origin;
        Selection = selection;
    }

    public WorkspaceDeclarationOccurrence Occurrence { get; }
    public ExactLibrarySourceCoordinate? Coordinate { get; }
    public WorkspaceDeclarationCoordinateStatus CoordinateStatus =>
        Coordinate is null
            ? WorkspaceDeclarationCoordinateStatus.CoordinateUnavailable
            : WorkspaceDeclarationCoordinateStatus.Available;
    public AssemblyReferenceIdentity AssemblyIdentity { get; }
    public WorkspaceDeclarationOrigin Origin { get; }
    public AssemblyResolutionProvenance Selection { get; }
}

/// <summary>
/// Detached request, realization coverage, and roster from one admitted context.
/// </summary>
public sealed class WorkspaceDeclarationContextReceipt
{
    internal WorkspaceDeclarationContextReceipt(
        InspectionWorkspaceIdentity workspace,
        int order,
        WorkspaceDeclarationRequest request,
        bool isRealized,
        ImmutableArray<WorkspaceDeclarationMember> members,
        ImmutableArray<WorkspaceDeclarationFailure> failures)
    {
        Workspace = workspace;
        Order = order;
        Request = request;
        IsRealized = isRealized;
        Members = members;
        Failures = failures;
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public int Order { get; }
    public WorkspaceDeclarationRequest Request { get; }
    public bool IsRealized { get; }
    public ImmutableArray<WorkspaceDeclarationMember> Members { get; }
    public ImmutableArray<WorkspaceDeclarationFailure> Failures { get; }
}

/// <summary>
/// An admitted context. Live group access remains separate from its receipt.
/// </summary>
public sealed class WorkspaceDeclarationContext
{
    internal WorkspaceDeclarationContext(
        WorkspaceDeclarationContextReceipt receipt,
        WorkspaceContextLoadOutcome outcome)
        : this(receipt,
            outcome is WorkspaceContextLoadOutcome.Loaded loaded ? loaded.Group : null)
    {
        ContextLoadOutcome = outcome;
    }

    internal WorkspaceDeclarationContext(
        WorkspaceDeclarationContextReceipt receipt,
        AssemblyContextGroup? group)
    {
        Receipt = receipt;
        Group = group;
        LibraryOccurrences = [];
    }

    internal WorkspaceDeclarationContext(
        WorkspaceDeclarationContextReceipt receipt,
        ImmutableArray<WorkspaceLibraryOccurrence> libraryOccurrences,
        LibraryTypeDeclarationInventoryInspectionBounds libraryInspectionBounds)
    {
        Receipt = receipt;
        LibraryOccurrences = libraryOccurrences;
        LibraryInspectionBounds = libraryInspectionBounds;
    }

    public WorkspaceDeclarationContextReceipt Receipt { get; }
    public AssemblyContextGroup? Group { get; }
    internal ImmutableArray<WorkspaceLibraryOccurrence> LibraryOccurrences
    { get; }
    internal LibraryTypeDeclarationInventoryInspectionBounds?
        LibraryInspectionBounds
    { get; }

    /// <summary>The original loader result, when admitted by the context loader.</summary>
    public WorkspaceContextLoadOutcome? ContextLoadOutcome { get; }
}

/// <summary>Identity of one captured association, not an arithmetic revision.</summary>
public sealed class WorkspaceDeclarationPopulationIdentity
{
    internal WorkspaceDeclarationPopulationIdentity() { }
}

/// <summary>Detached evidence for one explicitly selected finite population.</summary>
public sealed class WorkspaceDeclarationPopulationReceipt
{
    internal WorkspaceDeclarationPopulationReceipt(
        InspectionWorkspaceIdentity workspace,
        ImmutableArray<WorkspaceDeclarationContextReceipt> contexts)
    {
        Workspace = workspace;
        Contexts = contexts;
        Members = [.. contexts.SelectMany(static context => context.Members)];
    }

    public InspectionWorkspaceIdentity Workspace { get; }
    public WorkspaceDeclarationPopulationIdentity Identity { get; } = new();
    public ImmutableArray<WorkspaceDeclarationContextReceipt> Contexts { get; }
    public ImmutableArray<WorkspaceDeclarationMember> Members { get; }
    /// <summary>
    /// Whether every selected context realized. Coordinate and inventory coverage
    /// are separate: this does not certify a complete declaration query.
    /// </summary>
    public bool IsRealizationComplete => Contexts.All(static context => context.IsRealized);
}

public enum WorkspaceDeclarationPopulationFailure
{
    MalformedSelection,
    ForeignWorkspace,
    DuplicateContext,
    ContextUnavailable,
    WorkspaceClosing,
    WorkspaceClosed,
    OccurrenceNotSelected,
}

public abstract class WorkspaceDeclarationPopulationCapture
{
    private protected WorkspaceDeclarationPopulationCapture() { }

    public sealed class Captured : WorkspaceDeclarationPopulationCapture
    {
        internal Captured(WorkspaceDeclarationPopulation population) => Population = population;
        public WorkspaceDeclarationPopulation Population { get; }
    }

    public sealed class Rejected : WorkspaceDeclarationPopulationCapture
    {
        internal Rejected(WorkspaceDeclarationPopulationFailure failure) => Failure = failure;
        public WorkspaceDeclarationPopulationFailure Failure { get; }
    }
}

public abstract class WorkspaceDeclarationInventoryOutcome
{
    private protected WorkspaceDeclarationInventoryOutcome() { }

    public sealed class Inspected : WorkspaceDeclarationInventoryOutcome
    {
        internal Inspected(
            AssemblyTypeDeclarationInventoryOutcome outcome,
            Guid moduleVersionId)
        {
            Outcome = outcome;
            ModuleVersionId = moduleVersionId;
        }

        public AssemblyTypeDeclarationInventoryOutcome Outcome { get; }
        public Guid ModuleVersionId { get; }
    }

    public sealed class LibraryInspected :
        WorkspaceDeclarationInventoryOutcome
    {
        internal LibraryInspected(
            LibraryTypeDeclarationInventoryInspectionOutcome outcome) =>
            Outcome = outcome;

        public LibraryTypeDeclarationInventoryInspectionOutcome Outcome
        { get; }
    }

    public sealed class InventoryRejected :
        WorkspaceDeclarationInventoryOutcome
    {
        internal InventoryRejected(CandidateOpenFailure failure) =>
            Failure = failure;

        public CandidateOpenFailure Failure { get; }
    }

    public sealed class AcquisitionRejected : WorkspaceDeclarationInventoryOutcome
    {
        internal AcquisitionRejected(CandidateOpenFailure failure) => Failure = failure;
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class Unavailable : WorkspaceDeclarationInventoryOutcome
    {
        internal Unavailable(WorkspaceDeclarationPopulationFailure failure) => Failure = failure;
        public WorkspaceDeclarationPopulationFailure Failure { get; }
    }

    public sealed class NotEvaluated : WorkspaceDeclarationInventoryOutcome
    {
        internal NotEvaluated(WorkspaceDeclarationInventoryBound bound) => Bound = bound;
        public WorkspaceDeclarationInventoryBound Bound { get; }
    }
}

public enum WorkspaceDeclarationInventoryBound
{
    ReadAttempts,
    RetainedInventories,
}

/// <summary>
/// Live query input over an exact captured roster. This is not a resident index.
/// </summary>
public sealed class WorkspaceDeclarationPopulation
{
    readonly InspectionWorkspace _workspace;
    readonly IReadOnlyDictionary<
        WorkspaceDeclarationOccurrence,
        WorkspaceDeclarationMemberAccess> _access;

    internal WorkspaceDeclarationPopulation(
        InspectionWorkspace workspace,
        WorkspaceDeclarationPopulationReceipt receipt,
        IReadOnlyDictionary<
            WorkspaceDeclarationOccurrence,
            WorkspaceDeclarationMemberAccess> access)
    {
        _workspace = workspace;
        Receipt = receipt;
        _access = access;
        RelationAuthority =
            SubjectRelationPopulationAuthority.Capture(
                StructuralSubjectIdentity.ForWorkspace(receipt.Workspace),
                receipt.Identity);
    }

    public WorkspaceDeclarationPopulationReceipt Receipt { get; }

    internal SubjectRelationPopulationAuthority RelationAuthority { get; }

    internal WorkspaceDeclarationPopulationFailure? Availability() =>
        _workspace.DeclarationPopulationAvailability();

    internal bool TryGetAccess(
        WorkspaceDeclarationOccurrence occurrence,
        out WorkspaceDeclarationMember? member,
        out AssemblyContextGroup? group,
        out ResolvedAssemblyReference? assembly)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        member = Receipt.Members.FirstOrDefault(candidate =>
            ReferenceEquals(candidate.Occurrence, occurrence));
        if (member is null
            || !_access.TryGetValue(occurrence, out var access))
        {
            group = null;
            assembly = null;
            return false;
        }

        group = access.Group;
        assembly = access.Assembly;
        return true;
    }

    internal IEnumerable<(
        WorkspaceDeclarationMember Member,
        AssemblyContextGroup Group,
        ResolvedAssemblyReference Assembly)> ReadAccesses()
    {
        foreach (WorkspaceDeclarationMember member in Receipt.Members)
        {
            if (_access.TryGetValue(
                    member.Occurrence,
                    out var access))
            {
                yield return (
                    member,
                    access.Group,
                    access.Assembly);
            }
        }
    }

    public WorkspaceDeclarationOccurrence? FindOccurrence(
        AssemblyAcquisitionRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        foreach (var (member, _, assembly) in ReadAccesses())
        {
            if (ReferenceEquals(
                    assembly.Registration,
                    registration))
            {
                return member.Occurrence;
            }
        }

        return null;
    }

    /// <summary>Inspects one selected occurrence through its existing group owner.</summary>
    public WorkspaceDeclarationInventoryOutcome ReadDeclarations(
        WorkspaceDeclarationOccurrence occurrence,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_access.TryGetValue(occurrence, out var access))
        {
            return new WorkspaceDeclarationInventoryOutcome.Unavailable(
                WorkspaceDeclarationPopulationFailure.OccurrenceNotSelected);
        }

        if (Availability() is { } unavailable)
            return new WorkspaceDeclarationInventoryOutcome.Unavailable(unavailable);

        try
        {
            return access switch
            {
                WorkspaceDeclarationMemberAccess.AssemblyContext group =>
                    ReadFromAssemblyContext(group, cancellationToken),
                WorkspaceDeclarationMemberAccess.LibraryOccurrence library =>
                    ReadFromLibraryOccurrence(library, cancellationToken),
                _ => throw new InvalidOperationException(
                    "Unknown Workspace declaration member access."),
            };
        }
        catch (ObjectDisposedException exception)
            when (exception.ObjectName == typeof(AssemblyContextGroup).FullName)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkspaceDeclarationInventoryOutcome.Unavailable(
                WorkspaceDeclarationPopulationFailure.ContextUnavailable);
        }
    }

    static WorkspaceDeclarationInventoryOutcome ReadFromAssemblyContext(
        WorkspaceDeclarationMemberAccess.AssemblyContext access,
        CancellationToken cancellationToken)
    {
        var result = access.Group.UseAssemblySession(
            access.Assembly,
            static session =>
                (session.ModuleVersionId(), session.TypeDeclarations()));
        cancellationToken.ThrowIfCancellationRequested();
        return result switch
        {
            AssemblyImageAccessResult<
                (Guid ModuleVersionId,
                    AssemblyTypeDeclarationInventoryOutcome Outcome)>
                .Available available =>
                    new WorkspaceDeclarationInventoryOutcome.Inspected(
                        available.Value.Outcome,
                        available.Value.ModuleVersionId),
            AssemblyImageAccessResult<
                (Guid ModuleVersionId,
                    AssemblyTypeDeclarationInventoryOutcome Outcome)>
                .Rejected rejected =>
                    new WorkspaceDeclarationInventoryOutcome
                        .AcquisitionRejected(rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly-image access result."),
        };
    }

    WorkspaceDeclarationInventoryOutcome ReadFromLibraryOccurrence(
        WorkspaceDeclarationMemberAccess.LibraryOccurrence access,
        CancellationToken cancellationToken)
    {
        WorkspaceLibraryOperationIssueOutcome issued =
            _workspace.IssueLibraryOperation(access.Occurrence);
        if (issued is not WorkspaceLibraryOperationIssueOutcome.Issued
            available)
        {
            return new WorkspaceDeclarationInventoryOutcome.Unavailable(
                WorkspaceDeclarationPopulationFailure.ContextUnavailable);
        }

        using (available.Lease)
        {
            LibraryTypeDeclarationInventoryInspectionOutcome outcome =
                LibraryTypeDeclarationInventoryInspection.Execute(
                    new(
                        access.Occurrence.Library,
                        access.Bounds),
                    available.Lease,
                    cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new WorkspaceDeclarationInventoryOutcome
                .LibraryInspected(outcome);
        }
    }
}

internal abstract record WorkspaceDeclarationMemberAccess
{
    private WorkspaceDeclarationMemberAccess()
    {
    }

    internal sealed record AssemblyContext(
        AssemblyContextGroup Group,
        ResolvedAssemblyReference Assembly)
        : WorkspaceDeclarationMemberAccess;

    internal sealed record LibraryOccurrence(
        WorkspaceLibraryOccurrence Occurrence,
        LibraryTypeDeclarationInventoryInspectionBounds Bounds)
        : WorkspaceDeclarationMemberAccess;
}
